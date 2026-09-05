using System.IO;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Reads the newest real Codex account-limit snapshot from recent rollout files.</summary>
public sealed class CodexUsageSnapshotReader(string? sessionsRootOverride = null)
{
    private const int MaxFilesToProbe = 20;
    private const int TailBytes = 256 * 1024;

    private readonly string _sessionsRoot = sessionsRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    public CodexUsageSnapshot? ReadLatest()
    {
        if (!Directory.Exists(_sessionsRoot))
        {
            return null;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(_sessionsRoot, "rollout-*.jsonl", SearchOption.AllDirectories)
                         .Select(path => new FileInfo(path))
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Take(MaxFilesToProbe))
            {
                var tail = TranscriptTailFile.ReadTail(file.FullName, TailBytes);
                if (tail is null)
                {
                    continue;
                }

                foreach (var line in tail.Split('\n').Reverse())
                {
                    if (CodexRateLimitParser.TryParseSnapshot(line) is { } snapshot)
                    {
                        return snapshot;
                    }
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
}
