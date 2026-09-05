namespace ClaudeLauncher.App.Models;

/// <summary>Latest account-limit reading emitted by Codex CLI in a rollout transcript.</summary>
public sealed record CodexUsageSnapshot(
    DateTimeOffset CapturedAt,
    CodexUsageWindow? Primary,
    CodexUsageWindow? Secondary);

public sealed record CodexUsageWindow(
    double UsedPercentage,
    int WindowMinutes,
    DateTimeOffset ResetsAt);
