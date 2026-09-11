using System.IO;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Best-effort <see cref="IAgentTranscriptSource"/> for Kimi Code CLI. Documented (not
/// independently verified) layout: <c>~/.kimi-code/session_index.jsonl</c> is an append-only index of
/// <c>{sessionId, sessionDir, workDir}</c> records, and each session's transcript lives at
/// <c>~/.kimi-code/sessions/&lt;workDirKey&gt;/&lt;sessionId&gt;/agents/main/wire.jsonl</c>. Finding
/// "the transcript for this project" is a matter of filtering the index by <c>workDir</c> rather than
/// scanning a directory tree. Classification/preview use <see cref="GenericChatJsonlHeuristics"/>
/// (unverified against real Kimi Code output). <see cref="SupportsUsageLimitAutoResume"/> is false: no
/// Kimi-equivalent of Claude Code's "rate_limit" transcript event is documented anywhere this was
/// researched.</summary>
public sealed class KimiTranscriptSource(string? homeOverride = null) : IAgentTranscriptSource
{
    private readonly string _home = homeOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi-code");

    public bool SupportsUsageLimitAutoResume => false;

    public string? FindMostRecentTranscriptFile(string workingDirectory) =>
        FindTranscriptFile(workingDirectory, notBefore: null);

    public string? FindActiveTranscriptFile(string workingDirectory, DateTimeOffset notBefore) =>
        FindTranscriptFile(workingDirectory, notBefore);

    public ProjectActivityState? Classify(string tailText) => GenericChatJsonlHeuristics.ClassifyText(tailText);

    public string? ExtractPreview(string tailText) => GenericChatJsonlHeuristics.ExtractPreview(tailText, "あなた", "Kimi");

    public DateTimeOffset? TryParseUsageLimitEvent(string jsonlLine) => null;

    /// <summary>Always null: no end-of-turn record is known for this CLI's log format.</summary>
    public DateTimeOffset? TryDetectTurnComplete(string tailText) => null;

    private string? FindTranscriptFile(string workingDirectory, DateTimeOffset? notBefore)
    {
        var indexPath = Path.Combine(_home, "session_index.jsonl");
        if (!File.Exists(indexPath))
        {
            return null;
        }

        string? bestMatch = null;
        DateTime bestMatchWriteTimeUtc = DateTime.MinValue;

        // The index is append-only, so later lines are more recent; keep the last matching entry
        // rather than the first.
        foreach (var line in ReadLinesSafely(indexPath))
        {
            var entry = TryParseIndexEntry(line);
            if (entry is not { } parsed || !WorkingDirectoryComparer.AreSame(parsed.WorkDir, workingDirectory))
            {
                continue;
            }

            var wireFile = ResolveWireFile(parsed.SessionDir, parsed.SessionId);
            if (wireFile is null || !File.Exists(wireFile))
            {
                continue;
            }

            var writeTimeUtc = File.GetLastWriteTimeUtc(wireFile);
            if (notBefore is { } cutoff && writeTimeUtc < cutoff.UtcDateTime.AddSeconds(-5))
            {
                continue;
            }

            if (bestMatch is null || writeTimeUtc >= bestMatchWriteTimeUtc)
            {
                bestMatch = wireFile;
                bestMatchWriteTimeUtc = writeTimeUtc;
            }
        }

        return bestMatch;
    }

    /// <summary>Tries <paramref name="sessionDir"/> both as an absolute path and as a path relative to
    /// <c>~/.kimi-code/</c> (with and without an extra "sessions" segment), since the index's exact
    /// convention for that field isn't confirmed. Returns the first candidate whose
    /// <c>agents/main/wire.jsonl</c> actually exists.</summary>
    private string? ResolveWireFile(string sessionDir, string sessionId)
    {
        var candidates = new List<string>();
        if (Path.IsPathRooted(sessionDir))
        {
            candidates.Add(sessionDir);
        }
        else
        {
            candidates.Add(Path.Combine(_home, sessionDir));
            candidates.Add(Path.Combine(_home, "sessions", sessionDir));
            candidates.Add(Path.Combine(_home, "sessions", sessionDir, sessionId));
        }

        foreach (var candidate in candidates)
        {
            var wireFile = Path.Combine(candidate, "agents", "main", "wire.jsonl");
            if (File.Exists(wireFile))
            {
                return wireFile;
            }
        }

        return null;
    }

    private static IEnumerable<string> ReadLinesSafely(string path)
    {
        IEnumerable<string> lines;
        try
        {
            lines = File.ReadLines(path);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var line in lines)
        {
            yield return line;
        }
    }

    private static (string SessionId, string SessionDir, string WorkDir)? TryParseIndexEntry(string jsonLine)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("sessionId", out var idEl) || idEl.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("sessionDir", out var dirEl) || dirEl.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("workDir", out var workDirEl) || workDirEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var id = idEl.GetString();
            var dir = dirEl.GetString();
            var workDir = workDirEl.GetString();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(workDir))
            {
                return null;
            }

            return (id, dir, workDir);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
