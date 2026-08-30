using System.IO;
using System.Text;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Tracks account-wide Claude token consumption across every project under
/// <see cref="ClaudeProjectPathResolver.GetProjectsRoot"/> - not just the projects this app itself
/// launched - since Claude's 5-hour session limit and weekly limit are shared per-account, not
/// per-project. A Claude Code session running in a terminal this app never opened still counts against
/// the same budget, so scanning has to cover the whole `~/.claude/projects` tree directly rather than
/// going through any <see cref="ViewModels.SessionItemViewModel"/>.
///
/// <para>Anthropic never exposes the numeric token limit itself, only a "you've hit it" event (see
/// <see cref="UsageLimitEventParser"/>). This class calibrates each window's 100% mark from the
/// account's own cumulative consumption at the moment such an event was last observed, persisting the
/// result via <see cref="UsageCalibrationStore"/> so it survives app restarts. Before a window's first
/// hit, its baseline is null and callers should treat the percentage as "not yet measured" rather than
/// guessing a number.</para>
///
/// <para><b>Rolling-window approximation:</b> Claude's real 5-hour session limit is believed to be a
/// fixed block starting from first use, not a rolling window, but there's no reliable transcript signal
/// for exactly when the current block started - only the reset time, and only once a limit is actually
/// hit. This tracker approximates both windows as rolling ("tokens in the last 5 hours" / "tokens in the
/// last 7 days") instead of reconstructing block boundaries. This can drift from the true percentage
/// near a block boundary, but is a reasonable best-effort given the available signals.</para></summary>
public sealed class ClaudeAccountUsageTracker
{
    public static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);

    /// <summary>Also how far back samples are kept in memory - the longest window in use, so both
    /// <see cref="SessionWindow"/> and this one can be computed from the same sample list.</summary>
    public static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    private readonly UsageCalibrationStore _calibrationStore;
    private readonly string _projectsRoot;
    private readonly Dictionary<string, long> _fileOffsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ClaudeTokenLineParser.TokenSample> _samples = [];
    private UsageCalibration _calibration;
    private bool _backfilled;

    public ClaudeAccountUsageTracker() : this(new UsageCalibrationStore())
    {
    }

    /// <summary><paramref name="projectsRootOverride"/> mirrors <see cref="ClaudeTranscriptSource"/>'s
    /// constructor - lets tests point this at a temp directory instead of the real
    /// %USERPROFILE%\.claude\projects.</summary>
    public ClaudeAccountUsageTracker(UsageCalibrationStore calibrationStore, string? projectsRootOverride = null)
    {
        _calibrationStore = calibrationStore;
        _projectsRoot = projectsRootOverride ?? ClaudeProjectPathResolver.GetProjectsRoot();
        _calibration = _calibrationStore.Load();
    }

    /// <summary>Scans every project transcript for newly-appended lines since the last call, folding new
    /// token samples into the rolling window and recalibrating on any newly-observed rate-limit event.
    /// The first call also backfills up to <see cref="WeeklyWindow"/> of history from existing file
    /// contents before incremental reading takes over.</summary>
    public void Poll(DateTimeOffset now)
    {
        if (!Directory.Exists(_projectsRoot))
        {
            return;
        }

        if (!_backfilled)
        {
            BackfillHistory(_projectsRoot, now);
            _backfilled = true;
        }
        else
        {
            foreach (var file in Directory.EnumerateFiles(_projectsRoot, "*.jsonl", SearchOption.AllDirectories))
            {
                ReadNewLines(file, now);
            }
        }

        PruneOldSamples(now);
    }

    /// <summary>Sum of every sample timestamped within <paramref name="window"/> of <paramref name="now"/>.</summary>
    public long GetWindowTotal(TimeSpan window, DateTimeOffset now)
    {
        var cutoff = now - window;
        long total = 0;
        foreach (var sample in _samples)
        {
            if (sample.Timestamp >= cutoff)
            {
                total += sample.Tokens;
            }
        }

        return total;
    }

    public long? SessionWindowBaselineTokens => _calibration.SessionWindowBaselineTokens;

    public long? WeeklyWindowBaselineTokens => _calibration.WeeklyWindowBaselineTokens;

    /// <summary>One-time full read of every project file touched within <see cref="WeeklyWindow"/> -
    /// files that haven't been modified that recently can't contain any in-window lines, so their
    /// (possibly many-megabyte) contents are never opened. Each backfilled file's offset is recorded at
    /// its current length so the next <see cref="Poll"/> resumes from there instead of re-scanning.</summary>
    private void BackfillHistory(string root, DateTimeOffset now)
    {
        var cutoffUtc = (now - WeeklyWindow).UtcDateTime;

        foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
        {
            FileInfo info;
            try
            {
                info = new FileInfo(file);
            }
            catch (IOException)
            {
                continue;
            }

            if (!info.Exists || info.LastWriteTimeUtc < cutoffUtc)
            {
                continue;
            }

            ReadNewLines(file, now);
        }
    }

    /// <summary>Reads only the bytes appended since this file's last recorded offset (0 for a file seen
    /// for the first time), same delta-read technique as <see cref="TranscriptLimitWatcher.ReadNewLines"/>.
    /// A trailing incomplete line is left for the next poll rather than mis-parsed.</summary>
    private void ReadNewLines(string file, DateTimeOffset now)
    {
        var fromOffset = _fileOffsets.GetValueOrDefault(file, 0L);

        byte[] buffer;
        int read;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < fromOffset)
            {
                // File shrank/was replaced (e.g. rotated) - restart from the beginning rather than
                // seeking past the new end.
                fromOffset = 0;
            }

            if (fs.Length <= fromOffset)
            {
                _fileOffsets[file] = fromOffset;
                return;
            }

            fs.Seek(fromOffset, SeekOrigin.Begin);
            buffer = new byte[fs.Length - fromOffset];
            read = fs.Read(buffer, 0, buffer.Length);
        }
        catch (IOException)
        {
            // Being written to / locked / deleted mid-scan - try again next poll.
            return;
        }

        var text = Encoding.UTF8.GetString(buffer, 0, read);
        var lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            // No complete line yet in the new bytes; leave the offset where it was and retry next poll.
            return;
        }

        var completeChunk = text[..lastNewline];
        _fileOffsets[file] = fromOffset + Encoding.UTF8.GetByteCount(completeChunk) + 1;

        foreach (var line in completeChunk.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (ClaudeTokenLineParser.TryParseLine(line) is { } sample)
            {
                _samples.Add(sample);
            }

            if (UsageLimitEventParser.TryParseLineWithKind(line) is { } limitEvent)
            {
                Calibrate(limitEvent.Kind, now);
            }
        }
    }

    /// <summary>Overwrites the given window's baseline with the account's current cumulative
    /// consumption for that window. Always recalibrates on a fresh hit rather than keeping the first-ever
    /// value - the most recent real hit is the best available estimate, since usage patterns (and the
    /// plan itself) can change over time.</summary>
    private void Calibrate(UsageLimitKind kind, DateTimeOffset now)
    {
        var window = kind == UsageLimitKind.Session ? SessionWindow : WeeklyWindow;
        var total = GetWindowTotal(window, now);

        if (kind == UsageLimitKind.Session)
        {
            _calibration.SessionWindowBaselineTokens = total;
            _calibration.SessionWindowCalibratedAt = now;
        }
        else
        {
            _calibration.WeeklyWindowBaselineTokens = total;
            _calibration.WeeklyWindowCalibratedAt = now;
        }

        _calibrationStore.Save(_calibration);
    }

    /// <summary>Uses <see cref="List{T}.RemoveAll"/> rather than trimming a prefix, since samples are
    /// appended file-by-file (not globally time-sorted) - a later file's lines can be older than an
    /// earlier file's, so "drop everything before the first in-window entry" would wrongly keep
    /// out-of-window samples that happen to sit after one that's still in range.</summary>
    private void PruneOldSamples(DateTimeOffset now)
    {
        var cutoff = now - WeeklyWindow;
        _samples.RemoveAll(s => s.Timestamp < cutoff);
    }
}
