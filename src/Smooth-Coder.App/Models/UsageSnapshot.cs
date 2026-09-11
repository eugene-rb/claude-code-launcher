namespace SmoothCoder.App.Models;

/// <summary>The most recent real usage reading pulled from Claude Code's status-line stdin JSON by
/// <see cref="Services.UsageStatusLineBridge"/> and persisted to
/// %APPDATA%\SmoothCoder\usage-snapshot.json. Anthropic only exposes these numbers to the
/// status-line command (for Pro/Max subscribers, after the first API response), never in the
/// transcript, so this file is the only channel the launcher has to the account's true
/// <c>used_percentage</c>. Each window can be independently absent - Claude Code drops a window from
/// its status-line payload once that window's <c>resets_at</c> has passed.</summary>
public sealed class UsageSnapshot
{
    /// <summary>Local time the bridge last wrote this file. Consumers treat a snapshot older than a
    /// few minutes as stale (no Claude session has been active to refresh it) and fall back to the
    /// token-based estimate.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    public UsageSnapshotWindow? FiveHour { get; set; }

    public UsageSnapshotWindow? SevenDay { get; set; }

    /// <summary>Session context-window fill from the same payload - not an account limit, kept only
    /// so the status-line text can show it alongside the rate-limit figures.</summary>
    public double? ContextUsedPercentage { get; set; }
}

/// <summary>One rate-limit window's real reading. <see cref="ResetsAt"/> is the true window boundary
/// (converted from the payload's Unix-epoch-seconds <c>resets_at</c>); a reading whose
/// <see cref="ResetsAt"/> is already in the past describes a window that has since reset and must be
/// ignored rather than used to calibrate.</summary>
public sealed class UsageSnapshotWindow
{
    public double UsedPercentage { get; set; }

    public DateTimeOffset ResetsAt { get; set; }
}
