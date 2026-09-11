using System.IO;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Reads the session-state marker files written by the ~/.claude/hooks/write-status-marker.py
/// hook (one file per Claude Code session_id, under %APPDATA%\SmoothCoder\status). A permission
/// prompt, an AskUserQuestion/ExitPlanMode confirmation, and the end of a turn are none of them written
/// to the transcript itself, so this is the only reliable signal for those states - the
/// transcript-based <see cref="TranscriptActivityClassifier"/> can't see any of them (see
/// <see cref="StatusMarker"/> for the reasons the hook writes).</summary>
public static class StatusMarkerStore
{
    /// <summary>Mirrors ScheduleEvaluator's staleness windows: generous enough to never cut off a
    /// marker while it's genuinely still relevant, tight enough that a killed process's leftover
    /// marker doesn't lie about a project's status indefinitely.</summary>
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMinutes(10);

    public static string GetDefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smooth-Coder", "status");

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

            // The file name is the session id the hook wrote the marker for - the hook doesn't repeat
            // it inside the JSON, and callers need it to tell repeat sightings of one event from a new
            // event in the same session.
            return new StatusMarker(Path.GetFileNameWithoutExtension(path), cwd, reasonEl.GetString()!, updatedAt);
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
