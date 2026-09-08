namespace ClaudeLauncher.App.Models;

/// <summary>A state signal written by the write-status-marker.py hook (see Services/StatusMarkerStore)
/// for a single Claude Code session/turn. Two kinds of state reach the launcher this way: the session
/// is blocked waiting for the user (<see cref="PermissionPromptReason"/>/<see cref="AskOrPlanReason"/>),
/// or it just finished a turn (<see cref="TurnCompleteReason"/>). Neither is written to the transcript,
/// so the hook is the only place they can be observed.
///
/// <para><see cref="SessionId"/> is the marker file's own name (Claude Code's session id). It is what
/// makes one announcement per event possible: the marker stays on disk for its whole freshness window,
/// so a poll loop needs to tell "the same event, seen again" from "a new event in the same
/// session".</para></summary>
public sealed record StatusMarker(string SessionId, string Cwd, string Reason, DateTimeOffset UpdatedAt)
{
    /// <summary>A tool is asking for permission (Notification hook, permission_prompt).</summary>
    public const string PermissionPromptReason = "permission_prompt";

    /// <summary>An AskUserQuestion/ExitPlanMode confirmation is on screen (PreToolUse hook).</summary>
    public const string AskOrPlanReason = "ask_or_plan";

    /// <summary>The turn ended and the session is waiting for the user's next prompt (Stop hook).</summary>
    public const string TurnCompleteReason = "turn_complete";

    /// <summary>Whether this marker means the session is blocked on the user rather than simply done.
    /// Only these reasons drive the dashboard's 承認待ち badge - a finished turn is idle, not blocked.</summary>
    public bool IsBlockedOnUser => Reason is PermissionPromptReason or AskOrPlanReason;
}
