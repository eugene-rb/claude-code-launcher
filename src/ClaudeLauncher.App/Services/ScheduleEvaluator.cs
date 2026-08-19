using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

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
}
