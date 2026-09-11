using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>The <c>SmoothCoder.App.exe usage-statusline</c> subcommand. Claude Code runs it as its
/// status-line command; it receives the session's status-line JSON on stdin - the only place Anthropic
/// exposes the account's real <c>rate_limits.five_hour / seven_day</c> <c>used_percentage</c> and
/// <c>resets_at</c> - snapshots those figures to <see cref="UsageSnapshotStore"/> for the running app
/// to read, and prints a compact usage line back for the status bar.
///
/// <para>Runs on Claude Code's ~300ms status-line cadence, so it stays minimal and never throws: any
/// failure falls through to printing whatever line it can (or a blank one) and exiting 0, because a
/// status-line command that errors or hangs degrades the user's Claude Code session.</para></summary>
public static class UsageStatusLineBridge
{
    private const string ResetWindowChainTimeoutMarker = "usage-statusline";

    public static void Run(TextReader stdin, TextWriter stdout)
        => Run(stdin, stdout, new UsageSnapshotStore(), new AppSettingsStore(), DateTimeOffset.Now);

    /// <summary>Test seam - lets a test drive the parse/merge/emit path with an in-memory snapshot
    /// store and a fixed clock, without spawning a process.</summary>
    public static void Run(TextReader stdin, TextWriter stdout, UsageSnapshotStore snapshotStore, AppSettingsStore settingsStore, DateTimeOffset now)
    {
        string input;
        try
        {
            input = stdin.ReadToEnd();
        }
        catch (IOException)
        {
            input = string.Empty;
        }

        UsageSnapshot? previous = SafeLoad(snapshotStore);
        UsageSnapshot snapshot = BuildSnapshot(input, previous, now);

        try
        {
            snapshotStore.Save(snapshot);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A snapshot-write failure must not disturb the status line render.
        }

        var line = new StringBuilder(FormatLine(snapshot));

        var chained = SafeChain(settingsStore, input);
        if (!string.IsNullOrWhiteSpace(chained))
        {
            if (line.Length > 0)
            {
                line.Append(" · ");
            }

            line.Append(chained.Trim());
        }

        stdout.Write(line.ToString());
    }

    /// <summary>Parses the status-line payload and folds it into <paramref name="previous"/>: a window
    /// present in the new payload wins; a window absent from it is carried forward from
    /// <paramref name="previous"/> only while that carried reading's <c>ResetsAt</c> is still in the
    /// future (Claude Code drops a window from the payload once it resets, so a stale carried value
    /// expires itself rather than lying).</summary>
    public static UsageSnapshot BuildSnapshot(string statusLineJson, UsageSnapshot? previous, DateTimeOffset now)
    {
        var snapshot = new UsageSnapshot { CapturedAt = now };

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(statusLineJson) ? "{}" : statusLineJson);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return CarryForward(snapshot, previous, now);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return CarryForward(snapshot, previous, now);
        }

        if (root.TryGetProperty("context_window", out var ctx) && ctx.ValueKind == JsonValueKind.Object
            && TryReadDouble(ctx, "used_percentage") is { } ctxPct)
        {
            snapshot.ContextUsedPercentage = ctxPct;
        }

        JsonElement rateLimits = default;
        var hasRateLimits = root.TryGetProperty("rate_limits", out rateLimits) && rateLimits.ValueKind == JsonValueKind.Object;

        snapshot.FiveHour = ReadWindow(hasRateLimits ? rateLimits : default, "five_hour")
            ?? CarryWindow(previous?.FiveHour, now);
        snapshot.SevenDay = ReadWindow(hasRateLimits ? rateLimits : default, "seven_day")
            ?? CarryWindow(previous?.SevenDay, now);

        return snapshot;
    }

    private static UsageSnapshot CarryForward(UsageSnapshot snapshot, UsageSnapshot? previous, DateTimeOffset now)
    {
        snapshot.ContextUsedPercentage = previous?.ContextUsedPercentage;
        snapshot.FiveHour = CarryWindow(previous?.FiveHour, now);
        snapshot.SevenDay = CarryWindow(previous?.SevenDay, now);
        return snapshot;
    }

    private static UsageSnapshotWindow? CarryWindow(UsageSnapshotWindow? window, DateTimeOffset now)
        => window is not null && window.ResetsAt > now ? window : null;

    private static UsageSnapshotWindow? ReadWindow(JsonElement rateLimits, string name)
    {
        if (rateLimits.ValueKind != JsonValueKind.Object
            || !rateLimits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryReadDouble(window, "used_percentage") is not { } pct
            || !window.TryGetProperty("resets_at", out var resetsEl) || resetsEl.ValueKind != JsonValueKind.Number
            || !resetsEl.TryGetInt64(out var resetsEpoch))
        {
            return null;
        }

        return new UsageSnapshotWindow
        {
            UsedPercentage = pct,
            ResetsAt = DateTimeOffset.FromUnixTimeSeconds(resetsEpoch).ToLocalTime(),
        };
    }

    private static double? TryReadDouble(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var value)
            ? value
            : null;

    /// <summary>"5h 24% · 週 41% · ctx 40%", omitting any part that has no reading.</summary>
    public static string FormatLine(UsageSnapshot snapshot)
    {
        var parts = new List<string>(3);
        if (snapshot.FiveHour is { } five)
        {
            parts.Add($"5h {Math.Round(five.UsedPercentage).ToString("0", CultureInfo.InvariantCulture)}%");
        }

        if (snapshot.SevenDay is { } week)
        {
            parts.Add($"週 {Math.Round(week.UsedPercentage).ToString("0", CultureInfo.InvariantCulture)}%");
        }

        if (snapshot.ContextUsedPercentage is { } ctx)
        {
            parts.Add($"ctx {Math.Round(ctx).ToString("0", CultureInfo.InvariantCulture)}%");
        }

        return string.Join(" · ", parts);
    }

    private static UsageSnapshot? SafeLoad(UsageSnapshotStore store)
    {
        try
        {
            return store.Load();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Runs the status-line command the user had configured before the launcher took the slot
    /// (persisted as raw JSON in the launcher's own settings), feeding it the same stdin, so enabling
    /// this feature never silently drops an existing status line. Absent for the common case where the
    /// user had no status line - then this returns null and the launcher's line stands alone.</summary>
    private static string? SafeChain(AppSettingsStore settingsStore, string input)
    {
        try
        {
            var raw = settingsStore.Load().ChainedStatusLine;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("command", out var cmdEl) || cmdEl.ValueKind != JsonValueKind.String
                || cmdEl.GetString() is not { Length: > 0 } command
                || command.Contains(ResetWindowChainTimeoutMarker, StringComparison.Ordinal))
            {
                return null;
            }

            var psi = new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return null;
            }

            proc.StandardInput.Write(input);
            proc.StandardInput.Close();
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(2000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }

            return output;
        }
        catch
        {
            return null;
        }
    }
}
