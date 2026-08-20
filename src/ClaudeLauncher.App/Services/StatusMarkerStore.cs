using System.IO;
using System.Text.Json;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Reads the "awaiting your approval" marker files written by the
/// ~/.claude/hooks/write-status-marker.py hook (one file per Claude Code session_id, under
/// %APPDATA%\ClaudeLauncher\status). A permission prompt or an AskUserQuestion/ExitPlanMode
/// confirmation is never written to the transcript itself, so this is the only reliable signal for
/// that state - the transcript-based <see cref="TranscriptActivityClassifier"/> can't see it.</summary>
public static class StatusMarkerStore
{
    /// <summary>Mirrors ScheduleEvaluator's staleness windows: generous enough to never cut off a
    /// marker while it's genuinely still relevant, tight enough that a killed process's leftover
    /// marker doesn't lie about a project's status indefinitely.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(10);

    public static string GetDefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeLauncher", "status");

    /// <summary>Returns every marker in <paramref name="statusDir"/> newer than <paramref name="maxAge"/>
    /// relative to <paramref name="now"/>. A killed process leaves its marker behind forever otherwise,
    /// which would make the dashboard lie about a project that's no longer running (mirrors
    /// ScheduleEvaluator.IsAutoResumeStale's "ignore anything past its window" approach). Malformed or
    /// unreadable marker files are skipped, never thrown.</summary>
    public static IReadOnlyList<StatusMarker> ReadFresh(string statusDir, TimeSpan maxAge, DateTimeOffset now)
    {
        if (!Directory.Exists(statusDir))
        {
            return [];
        }

        var results = new List<StatusMarker>();
        foreach (var path in Directory.EnumerateFiles(statusDir, "*.json"))
        {
            var marker = TryReadMarker(path);
            if (marker is not null && now - marker.UpdatedAt <= maxAge)
            {
                results.Add(marker);
            }
        }

        return results;
    }

    private static StatusMarker? TryReadMarker(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;

            if (!root.TryGetProperty("cwd", out var cwdEl) || cwdEl.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("reason", out var reasonEl) || reasonEl.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("updatedAt", out var updatedAtEl) || updatedAtEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(updatedAtEl.GetString(), out var updatedAt))
            {
                return null;
            }

            var cwd = cwdEl.GetString();
            if (string.IsNullOrEmpty(cwd))
            {
                return null;
            }

            return new StatusMarker(cwd, reasonEl.GetString()!, updatedAt);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            // Racing the hook script's own tmp-then-replace write; try again next poll.
            return null;
        }
    }
}
