using System.IO;
using System.Text;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Tracks account-wide Claude token consumption across every project under
/// <see cref="ClaudeProjectPathResolver.GetProjectsRoot"/> - not just the projects this app itself
/// launched - since Claude's 5-hour session limit and weekly limit are shared per-account, not
/// per-project. A Claude Code session running in a terminal this app never opened still counts against
/// the same budget, so scanning has to cover the whole `~/.claude/projects` tree directly rather than
/// going through any <see cref="ViewModels.SessionItemViewModel"/>.
///
/// <para>Anthropic never exposes the numeric token limit itself. Two ground-truth signals are used to
/// fit each window's 100% mark: a "you've hit it" transcript event (see
/// <see cref="UsageLimitEventParser"/>), and - when the status-line bridge is installed - a live
/// <c>used_percentage</c> plus real <c>resets_at</c> boundary from
/// <see cref="UsageSnapshotStore"/>. Both feed the same fit
/// (<c>baseline = tokens-in-window / (observed% / 100)</c>); a limit event is just the
/// <c>observed% = 100</c> case. Results are persisted via <see cref="UsageCalibrationStore"/> so they
/// survive restarts. Before a window has any ground truth, its baseline is null and callers should
/// treat the percentage as "not yet measured".</para>
///
/// <para><b>Window boundaries:</b> when a real <c>resets_at</c> is known, tokens are summed since the
/// true block start (<c>resets_at</c> minus the window length). Without it - no bridge, or no
/// status-line render since startup - the tracker falls back to a rolling window ("tokens in the last
/// 5 hours" / "last 7 days"), which can drift near a block boundary but is the best available
/// approximation.</para></summary>
public sealed class ClaudeAccountUsageTracker
{
    public static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);

    /// <summary>Also how far back samples are kept in memory - the longest window in use, so both
    /// <see cref="SessionWindow"/> and this one can be computed from the same sample list.</summary>
    public static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);

    /// <summary>Below this observed percentage a snapshot reading isn't used to fit a baseline: the
    /// <c>tokens / (percent/100)</c> division amplifies any window-boundary or sampling error without
    /// bound as the percentage approaches zero.</summary>
    private const double CalibrationFloorPercent = 12.0;

    private readonly UsageCalibrationStore _calibrationStore;
    private readonly UsageSnapshotStore _snapshotStore;
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
    /// %USERPROFILE%\.claude\projects. <paramref name="snapshotStore"/> likewise lets a test supply a
    /// temp-file snapshot instead of the real %APPDATA% one.</summary>
    public ClaudeAccountUsageTracker(UsageCalibrationStore calibrationStore, string? projectsRootOverride = null, UsageSnapshotStore? snapshotStore = null)
    {
        _calibrationStore = calibrationStore;
        _snapshotStore = snapshotStore ?? new UsageSnapshotStore();
        _projectsRoot = projectsRootOverride ?? ClaudeProjectPathResolver.GetProjectsRoot();
        _calibration = _calibrationStore.Load();
    }

    /// <summary>Scans every project transcript for newly-appended lines since the last call, folding new
    /// token samples into the rolling window and recalibrating on any newly-observed rate-limit event,
    /// then folds in the latest status-line snapshot. The first call also backfills up to
    /// <see cref="WeeklyWindow"/> of history from existing file contents before incremental reading
    /// takes over.</summary>
    public void Poll(DateTimeOffset now)
    {
        if (Directory.Exists(_projectsRoot))
        {
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

        IngestSnapshot(now);
    }

    /// <summary>Sum of every sample timestamped within <paramref name="window"/> of <paramref name="now"/>
    /// (rolling-window fallback).</summary>
    public long GetWindowTotal(TimeSpan window, DateTimeOffset now) => GetWindowTotalSince(now - window);

    /// <summary>Sum of every sample timestamped at or after <paramref name="start"/> - used to total a
    /// real block window (<c>resets_at</c> minus the window length).</summary>
    public long GetWindowTotalSince(DateTimeOffset start)
    {
        long total = 0;
        foreach (var sample in _samples)
        {
            if (sample.Timestamp >= start)
            {
                total += sample.Tokens;
            }
        }

        return total;
    }

    public long? SessionWindowBaselineTokens => _calibration.SessionWindowBaselineTokens;

    public long? WeeklyWindowBaselineTokens => _calibration.WeeklyWindowBaselineTokens;

    /// <summary>True block boundary from the latest status-line reading (raw - the caller decides
    /// whether it's still in the future), or null if no status-line reading has ever landed.</summary>
    public DateTimeOffset? SessionWindowResetsAt => _calibration.SessionWindowResetsAt;

    public DateTimeOffset? WeeklyWindowResetsAt => _calibration.WeeklyWindowResetsAt;

    public double? SessionWindowLastRealPercent => _calibration.SessionWindowLastRealPercent;

    public DateTimeOffset? SessionWindowLastRealAt => _calibration.SessionWindowLastRealAt;

    public double? WeeklyWindowLastRealPercent => _calibration.WeeklyWindowLastRealPercent;

    public DateTimeOffset? WeeklyWindowLastRealAt => _calibration.WeeklyWindowLastRealAt;

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

    /// <summary>Folds the latest status-line snapshot into the calibration: records each window's real
    /// boundary and percentage, and refits the baseline whenever the percentage is high enough to be
    /// a reliable divisor. A window whose <c>resets_at</c> has already passed is ignored - it describes
    /// a window that has since reset, and calibrating against it would persist a wrong baseline.</summary>
    private void IngestSnapshot(DateTimeOffset now)
    {
        var snapshot = _snapshotStore.Load();
        if (snapshot is null)
        {
            return;
        }

        var changed = IngestWindow(UsageLimitKind.Session, snapshot.FiveHour, snapshot.CapturedAt, now);
        changed |= IngestWindow(UsageLimitKind.Weekly, snapshot.SevenDay, snapshot.CapturedAt, now);

        if (changed)
        {
            _calibrationStore.Save(_calibration);
        }
    }

    private bool IngestWindow(UsageLimitKind kind, UsageSnapshotWindow? window, DateTimeOffset capturedAt, DateTimeOffset now)
    {
        if (window is null || window.ResetsAt <= now)
        {
            return false;
        }

        if (kind == UsageLimitKind.Session)
        {
            _calibration.SessionWindowResetsAt = window.ResetsAt;
            _calibration.SessionWindowLastRealPercent = window.UsedPercentage;
            _calibration.SessionWindowLastRealAt = capturedAt;
        }
        else
        {
            _calibration.WeeklyWindowResetsAt = window.ResetsAt;
            _calibration.WeeklyWindowLastRealPercent = window.UsedPercentage;
            _calibration.WeeklyWindowLastRealAt = capturedAt;
        }

        if (window.UsedPercentage >= CalibrationFloorPercent)
        {
            CalibrateFromReal(kind, window.UsedPercentage, window.ResetsAt, now);
        }

        return true;
    }

    /// <summary>Recalibrates from a rate-limit ("you've hit it") transcript event - the
    /// <c>observed% = 100</c> case of <see cref="CalibrateFromReal"/>. Always recalibrates on a fresh
    /// hit rather than keeping the first-ever value: the most recent real hit is the best available
    /// estimate, since usage patterns (and the plan itself) can change over time.</summary>
    private void Calibrate(UsageLimitKind kind, DateTimeOffset now)
    {
        var resetsAt = kind == UsageLimitKind.Session
            ? _calibration.SessionWindowResetsAt
            : _calibration.WeeklyWindowResetsAt;

        CalibrateFromReal(kind, 100.0, resetsAt is { } r && r > now ? r : null, now);
        _calibrationStore.Save(_calibration);
    }

    /// <summary>Fits the given window's baseline to a ground-truth percentage:
    /// <c>baseline = tokens-since-block-start / (percent / 100)</c>. Block start is
    /// <paramref name="resetsAt"/> minus the window length when the real boundary is known, else a
    /// rolling "one window ago". <paramref name="percent"/> is always &gt; 0 at the call sites
    /// (gated by <see cref="CalibrationFloorPercent"/>, or a literal 100).</summary>
    private void CalibrateFromReal(UsageLimitKind kind, double percent, DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        var window = kind == UsageLimitKind.Session ? SessionWindow : WeeklyWindow;
        var blockStart = resetsAt is { } r ? r - window : now - window;
        var tokens = GetWindowTotalSince(blockStart);
        var baseline = (long)Math.Round(tokens / (percent / 100.0));

        if (kind == UsageLimitKind.Session)
        {
            _calibration.SessionWindowBaselineTokens = baseline;
            _calibration.SessionWindowCalibratedAt = now;
        }
        else
        {
            _calibration.WeeklyWindowBaselineTokens = baseline;
            _calibration.WeeklyWindowCalibratedAt = now;
        }
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
