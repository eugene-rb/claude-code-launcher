using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>
/// Decides whether a session's scheduled launch is due right now. Pure/testable: takes the
/// current time as a parameter instead of reading the clock itself.
/// </summary>
public static class ScheduleEvaluator
{
    /// <summary>How long past the target time a schedule is still considered "due". Without this
    /// upper bound, a schedule set for earlier today (or missed while the app was closed) would
    /// fire immediately the moment it's next evaluated, instead of being skipped as missed.</summary>
    public static readonly TimeSpan GraceWindow = TimeSpan.FromMinutes(2);

    public static bool ShouldFire(SessionProfile profile, bool isRunning, DateTimeOffset now)
    {
        if (!profile.ScheduleEnabled || isRunning)
        {
            return false;
        }

        return profile.Repeat switch
        {
            ScheduleRepeat.Once => profile.ScheduledAt is { } at
                && IsDue(now - at)
                && (profile.LastLaunchedAt is null || profile.LastLaunchedAt < at),
            ScheduleRepeat.Daily => profile.DailyTime is { } time
                && IsDue(now - new DateTimeOffset(now.Date + time, now.Offset))
                && (profile.LastLaunchedAt is null || profile.LastLaunchedAt.Value.Date < now.Date),
            _ => false,
        };
    }

    private static bool IsDue(TimeSpan elapsedSinceTarget) =>
        elapsedSinceTarget >= TimeSpan.Zero && elapsedSinceTarget <= GraceWindow;

    /// <summary>How long past its target an auto-resume is still considered worth firing. Much wider
    /// than <see cref="GraceWindow"/> on purpose — a resume that was due while the app was briefly
    /// closed (e.g. the computer slept overnight) should still fire, unlike a user-authored schedule.
    /// But without any bound at all, reopening the app days later would silently relaunch a session
    /// the user has long since moved on from; beyond this window it's treated as stale instead
    /// (see <see cref="IsAutoResumeStale"/>).</summary>
    public static readonly TimeSpan AutoResumeStaleWindow = TimeSpan.FromHours(6);

    /// <summary>The same bound, but for a resume that was parked because both accounts were exhausted
    /// (<see cref="SessionProfile.AutoResumeIsWaitingForReset"/>). Those are armed for whenever the
    /// first account frees up, which for a weekly limit is days away - and a machine that sleeps
    /// overnight would blow straight past <see cref="AutoResumeStaleWindow"/> and cancel a wait the
    /// user was explicitly told about on the dashboard. The reason a normal resume goes stale (the
    /// user has long since moved on) doesn't apply the same way here: a park is a deliberate, visible
    /// multi-day wait, and by the time it is due the account it was waiting on really is free.</summary>
    public static readonly TimeSpan ParkedAutoResumeStaleWindow = TimeSpan.FromDays(7);

    private static TimeSpan StaleWindowFor(SessionProfile profile) =>
        profile.AutoResumeIsWaitingForReset ? ParkedAutoResumeStaleWindow : AutoResumeStaleWindow;

    /// <summary>Unlike <see cref="ShouldFire"/>, running state isn't considered here — the caller
    /// stops the still-blocked process itself right before relaunching.</summary>
    public static bool ShouldAutoResume(SessionProfile profile, DateTimeOffset now) =>
        profile.AutoResumeAt is { } at && now >= at && now - at <= StaleWindowFor(profile);

    /// <summary>True once an auto-resume is far enough past due that it should be cancelled instead
    /// of fired (see <see cref="AutoResumeStaleWindow"/> and <see cref="ParkedAutoResumeStaleWindow"/>).</summary>
    public static bool IsAutoResumeStale(SessionProfile profile, DateTimeOffset now) =>
        profile.AutoResumeAt is { } at && now - at > StaleWindowFor(profile);

    /// <summary>True if a just-detected usage-limit reset time is still worth arming as
    /// <see cref="SessionProfile.AutoResumeAt"/>. An auto-resume relaunch continues the same
    /// conversation the usage limit interrupted, which - depending on the CLI - can mean the freshly
    /// (re)started transcript watcher rescans the very rate-limit line that triggered the relaunch and
    /// hands back the same, already-past reset time. Arming that again would satisfy
    /// <see cref="ShouldAutoResume"/> on the very next check and relaunch a second time, then a third,
    /// looping indefinitely instead of firing once. A reset time already at or before now is therefore
    /// treated as an echo of a detection already acted on, not a new one.</summary>
    public static bool ShouldArmAutoResume(DateTimeOffset candidateAutoResumeAt, DateTimeOffset now) =>
        candidateAutoResumeAt > now;
}
