using System.IO;
using System.Text.Json;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Discovers real Claude Code projects the user has already run `claude` in directly (not
/// via this launcher), by reading the `cwd` field Claude Code records in each project's transcripts
/// under ~/.claude/projects/&lt;hash of cwd&gt;/*.jsonl. The hashed folder name itself can't be reversed
/// back into the original path (the transform collapses many different characters onto '-'), so the
/// real path is recovered from transcript content instead - the same technique
/// <see cref="TranscriptLimitWatcher"/> relies on for usage-limit detection.</summary>
public static class ExistingProjectScanner
{
    public static IReadOnlyList<DiscoveredProjectInfo> Scan(string projectsRoot)
    {
        var results = new List<DiscoveredProjectInfo>();
        if (!Directory.Exists(projectsRoot))
        {
            return results;
        }

        foreach (var projectDir in Directory.EnumerateDirectories(projectsRoot))
        {
            var transcripts = Directory.EnumerateFiles(projectDir, "*.jsonl").ToList();
            if (transcripts.Count == 0)
            {
                continue;
            }

            var lastActivity = transcripts.Max(f => File.GetLastWriteTimeUtc(f));
            var cwd = transcripts.Select(TryExtractCwd).FirstOrDefault(c => c is not null);
            if (cwd is null)
            {
                continue;
            }

            var suggestedName = Path.GetFileName(cwd.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(suggestedName))
            {
                suggestedName = cwd;
            }

            results.Add(new DiscoveredProjectInfo(cwd, suggestedName, new DateTimeOffset(lastActivity, TimeSpan.Zero)));
        }

        return results;
    }

    /// <summary>Reads a transcript line by line (never loading the whole file) until a line with a
    /// parseable `cwd` field is found. Malformed lines are skipped rather than aborting the scan.</summary>
    private static string? TryExtractCwd(string transcriptFile)
    {
        using var reader = new StreamReader(transcriptFile);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!line.Contains("\"cwd\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("cwd", out var cwdEl) && cwdEl.ValueKind == JsonValueKind.String
                    && cwdEl.GetString() is { Length: > 0 } cwd)
                {
                    return cwd;
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines and keep scanning.
            }
        }

        return null;
    }
}
