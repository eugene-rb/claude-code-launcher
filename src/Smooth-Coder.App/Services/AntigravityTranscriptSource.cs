using System.IO;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Best-effort <see cref="IAgentTranscriptSource"/> for Antigravity CLI. Documented (not
/// independently verified) layout: each conversation has a companion, human-readable JSONL transcript
/// at <c>~/.gemini/antigravity-cli/conversations/&lt;uuid&gt;.jsonl</c> alongside its SQLite database
/// (the `.db` is intentionally never read directly - see the plan's non-goals). How a conversation maps
/// back to the working directory it was started in is <em>not</em> documented anywhere this was
/// researched, so this is the least certain of the three non-Claude sources: if no cwd-shaped field can
/// be found in a conversation file, it is simply skipped rather than guessed at from file recency alone
/// - callers see <see langword="null"/> (surfaced as <see cref="ProjectActivityState.Unknown"/>) rather
/// than a wrong project's activity. Classification/preview use <see cref="GenericChatJsonlHeuristics"/>
/// (unverified against real Antigravity output). <see cref="SupportsUsageLimitAutoResume"/> is false:
/// no Antigravity-equivalent of Claude Code's "rate_limit" transcript event is documented.</summary>
public sealed class AntigravityTranscriptSource(string? conversationsRootOverride = null) : IAgentTranscriptSource
{
    private const int MaxFilesToScan = 300;
    private const int MaxLinesToProbeForCwd = 8;

    private static readonly string[] CwdKeyCandidates = ["cwd", "workingDirectory", "workspaceRoot", "projectRoot"];

    private readonly string _conversationsRoot = conversationsRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity-cli", "conversations");

    public bool SupportsUsageLimitAutoResume => false;

    public string? FindMostRecentTranscriptFile(string workingDirectory) =>
        FindTranscriptFile(workingDirectory, notBefore: null);

    public string? FindActiveTranscriptFile(string workingDirectory, DateTimeOffset notBefore) =>
        FindTranscriptFile(workingDirectory, notBefore);

    public ProjectActivityState? Classify(string tailText) => GenericChatJsonlHeuristics.ClassifyText(tailText);

    public string? ExtractPreview(string tailText) => GenericChatJsonlHeuristics.ExtractPreview(tailText, "あなた", "Antigravity");

    public DateTimeOffset? TryParseUsageLimitEvent(string jsonlLine) => null;

    /// <summary>Always null: no end-of-turn record is known for this CLI's log format.</summary>
    public DateTimeOffset? TryDetectTurnComplete(string tailText) => null;

    private string? FindTranscriptFile(string workingDirectory, DateTimeOffset? notBefore)
    {
        if (!Directory.Exists(_conversationsRoot))
        {
            return null;
        }

        IEnumerable<FileInfo> files;
        try
        {
            files = Directory.EnumerateFiles(_conversationsRoot, "*.jsonl")
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .Take(MaxFilesToScan);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var cutoffUtc = notBefore?.UtcDateTime.AddSeconds(-5);
        foreach (var file in files)
        {
            if (cutoffUtc is { } cutoff && file.LastWriteTimeUtc < cutoff)
            {
                continue;
            }

            var cwd = TryReadCwd(file.FullName);
            if (cwd is not null && WorkingDirectoryComparer.AreSame(cwd, workingDirectory))
            {
                return file.FullName;
            }
        }

        return null;
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
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var key in CwdKeyCandidates)
            {
                if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String
                    && el.GetString() is { Length: > 0 } value)
                {
                    return value;
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
