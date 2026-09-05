using System.IO;
using System.Text.Json;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Best-effort <see cref="IAgentTranscriptSource"/> for Codex CLI. Session transcripts are
/// documented (not independently verified against a real installation) to live at
/// <c>~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl</c> in JSONL, one folder per day - unlike Claude
/// Code, files aren't bucketed by working directory, so finding "the transcript for this project"
/// means scanning recent days' files and reading each one's own recorded cwd. Classification/preview
/// use <see cref="GenericChatJsonlHeuristics"/>. Codex also writes account-window usage and reset
/// timestamps on <c>event_msg/token_count</c> records; those records drive limit detection.</summary>
public sealed class CodexTranscriptSource(string? sessionsRootOverride = null) : IAgentTranscriptSource
{
    private const int MaxDaysToScan = 30;
    private const int MaxFilesToScan = 300;
    private const int MaxLinesToProbeForCwd = 5;

    private readonly string _sessionsRoot = sessionsRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    public bool SupportsUsageLimitAutoResume => true;

    public string? FindMostRecentTranscriptFile(string workingDirectory) =>
        EnumerateCandidateFiles(notBefore: null)
            .FirstOrDefault(file => MatchesWorkingDirectory(file, workingDirectory));

    public string? FindActiveTranscriptFile(string workingDirectory, DateTimeOffset notBefore) =>
        EnumerateCandidateFiles(notBefore)
            .FirstOrDefault(file => MatchesWorkingDirectory(file, workingDirectory));

    public ProjectActivityState? Classify(string tailText) => GenericChatJsonlHeuristics.ClassifyText(tailText);

    public string? ExtractPreview(string tailText) => GenericChatJsonlHeuristics.ExtractPreview(tailText, "あなた", "Codex");

    public DateTimeOffset? TryParseUsageLimitEvent(string jsonlLine) =>
        CodexRateLimitParser.TryParseReachedReset(jsonlLine);

    /// <summary>Walks day-folders newest-first, bounded by <see cref="MaxDaysToScan"/> and
    /// <see cref="MaxFilesToScan"/> so a machine with years of history doesn't stall the dashboard's
    /// poll loop. Within a day, files are ordered by last-write time descending.</summary>
    private IEnumerable<string> EnumerateCandidateFiles(DateTimeOffset? notBefore)
    {
        if (!Directory.Exists(_sessionsRoot))
        {
            yield break;
        }

        var scanned = 0;
        var cutoffUtc = notBefore?.UtcDateTime.AddSeconds(-5);

        foreach (var dayDir in EnumerateDayDirectoriesNewestFirst())
        {
            IEnumerable<FileInfo> files;
            try
            {
                files = Directory.EnumerateFiles(dayDir, "rollout-*.jsonl")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(fi => fi.LastWriteTimeUtc);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (cutoffUtc is { } cutoff && file.LastWriteTimeUtc < cutoff)
                {
                    continue;
                }

                yield return file.FullName;

                if (++scanned >= MaxFilesToScan)
                {
                    yield break;
                }
            }
        }
    }

    private IEnumerable<string> EnumerateDayDirectoriesNewestFirst()
    {
        IEnumerable<string> yearDirs;
        try
        {
            yearDirs = Directory.EnumerateDirectories(_sessionsRoot).OrderByDescending(d => d);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        var daysYielded = 0;
        foreach (var yearDir in yearDirs)
        {
            foreach (var monthDir in SafeEnumerateDirectoriesDescending(yearDir))
            {
                foreach (var dayDir in SafeEnumerateDirectoriesDescending(monthDir))
                {
                    yield return dayDir;
                    if (++daysYielded >= MaxDaysToScan)
                    {
                        yield break;
                    }
                }
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectoriesDescending(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path).OrderByDescending(d => d);
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Probes only the first few lines (session metadata is expected up front, not scattered
    /// through the file) for a cwd-shaped field, trying a few plausible key paths since the exact
    /// rollout schema hasn't been verified against a real transcript.</summary>
    private static bool MatchesWorkingDirectory(string file, string workingDirectory)
    {
        var cwd = TryReadCwd(file);
        return cwd is not null && WorkingDirectoryComparer.AreSame(cwd, workingDirectory);
    }

    private static string? TryReadCwd(string file)
    {
        try
        {
            using var reader = new StreamReader(file);
            for (var i = 0; i < MaxLinesToProbeForCwd; i++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                if (!line.Contains("\"cwd\"", StringComparison.Ordinal))
                {
                    continue;
                }

                var cwd = TryExtractCwd(line);
                if (cwd is not null)
                {
                    return cwd;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string? TryExtractCwd(string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            if (TryGetCwdString(root, out var direct))
            {
                return direct;
            }

            foreach (var wrapperKey in new[] { "payload", "session_meta", "meta" })
            {
                if (root.TryGetProperty(wrapperKey, out var wrapper) && wrapper.ValueKind == JsonValueKind.Object
                    && TryGetCwdString(wrapper, out var nested))
                {
                    return nested;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetCwdString(JsonElement obj, out string? cwd)
    {
        cwd = null;
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("cwd", out var cwdEl)
            && cwdEl.ValueKind == JsonValueKind.String && cwdEl.GetString() is { Length: > 0 } value)
        {
            cwd = value;
            return true;
        }

        return false;
    }
}
