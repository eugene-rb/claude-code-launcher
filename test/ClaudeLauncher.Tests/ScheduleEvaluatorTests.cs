using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class ScheduleEvaluatorTests
{
    private static readonly TimeSpan Offset = TimeSpan.Zero;

    [Fact]
    public void ShouldFire_ScheduleDisabled_NeverFires()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = false,
            Repeat = ScheduleRepeat.Once,
            ScheduledAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset),
        };

        var now = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset);

        Assert.False(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_AlreadyRunning_NeverFires()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Once,
            ScheduledAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset),
        };

        var now = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset);

        Assert.False(ScheduleEvaluator.ShouldFire(profile, isRunning: true, now));
    }

    [Fact]
    public void ShouldFire_Once_BeforeScheduledTime_DoesNotFire()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Once,
            ScheduledAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset),
        };

        var now = new DateTimeOffset(2026, 8, 19, 8, 59, 0, Offset);

        Assert.False(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_Once_WithinGraceWindow_Fires()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Once,
            ScheduledAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset),
        };

        var now = new DateTimeOffset(2026, 8, 19, 9, 1, 0, Offset);

        Assert.True(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_Once_AfterGraceWindow_DoesNotFire()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Once,
            ScheduledAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset),
        };

        var now = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset) + ScheduleEvaluator.GraceWindow + TimeSpan.FromMinutes(1);

        Assert.False(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_Once_AfterAlreadyLaunched_DoesNotFireAgain()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Once,
            ScheduledAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset),
            LastLaunchedAt = new DateTimeOffset(2026, 8, 19, 9, 1, 0, Offset),
        };

        var now = new DateTimeOffset(2026, 8, 19, 9, 1, 30, Offset);

        Assert.False(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_Daily_BeforeDailyTime_DoesNotFire()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Daily,
            DailyTime = new TimeSpan(9, 0, 0),
        };

        var now = new DateTimeOffset(2026, 8, 19, 8, 59, 0, Offset);

        Assert.False(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_Daily_WithinGraceWindow_Fires()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Daily,
            DailyTime = new TimeSpan(9, 0, 0),
        };

        var now = new DateTimeOffset(2026, 8, 19, 9, 1, 0, Offset);

        Assert.True(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_Daily_AfterGraceWindowSameDay_DoesNotFire()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Daily,
            DailyTime = new TimeSpan(9, 0, 0),
        };

        var now = new DateTimeOffset(2026, 8, 19, 15, 0, 0, Offset);

        Assert.False(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_Daily_AlreadyFiredToday_DoesNotFireAgain()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Daily,
            DailyTime = new TimeSpan(9, 0, 0),
            LastLaunchedAt = new DateTimeOffset(2026, 8, 19, 9, 1, 0, Offset),
        };

        var now = new DateTimeOffset(2026, 8, 19, 9, 1, 30, Offset);

        Assert.False(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldFire_Daily_FiredPreviousDay_FiresAgainToday()
    {
        var profile = new SessionProfile
        {
            ScheduleEnabled = true,
            Repeat = ScheduleRepeat.Daily,
            DailyTime = new TimeSpan(9, 0, 0),
            LastLaunchedAt = new DateTimeOffset(2026, 8, 18, 9, 1, 0, Offset),
        };

        var now = new DateTimeOffset(2026, 8, 19, 9, 1, 0, Offset);

        Assert.True(ScheduleEvaluator.ShouldFire(profile, isRunning: false, now));
    }

    [Fact]
    public void ShouldAutoResume_NoAutoResumeAt_NeverFires()
    {
        var profile = new SessionProfile { AutoResumeAt = null };

        Assert.False(ScheduleEvaluator.ShouldAutoResume(profile, new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset)));
    }

    [Fact]
    public void ShouldAutoResume_BeforeTargetTime_DoesNotFire()
    {
        var profile = new SessionProfile { AutoResumeAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset) };

        Assert.False(ScheduleEvaluator.ShouldAutoResume(profile, new DateTimeOffset(2026, 8, 19, 8, 59, 0, Offset)));
    }

    [Fact]
    public void ShouldAutoResume_AtOrAfterTargetTime_Fires()
    {
        var profile = new SessionProfile { AutoResumeAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset) };

        Assert.True(ScheduleEvaluator.ShouldAutoResume(profile, new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset)));
    }

    [Fact]
    public void ShouldAutoResume_PastDueWithinStaleWindow_StillFires()
    {
        // Unlike ShouldFire's Once/Daily grace window, a missed auto-resume should still fire a few
        // hours late (e.g. the app was briefly closed) rather than be treated as missed immediately.
        var profile = new SessionProfile { AutoResumeAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset) };

        Assert.True(ScheduleEvaluator.ShouldAutoResume(profile, new DateTimeOffset(2026, 8, 19, 12, 0, 0, Offset)));
    }

    [Fact]
    public void ShouldAutoResume_BeyondStaleWindow_DoesNotFire()
    {
        // But it IS bounded: reopening the app days later shouldn't silently relaunch a session the
        // user has moved on from.
        var profile = new SessionProfile { AutoResumeAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset) };

        Assert.False(ScheduleEvaluator.ShouldAutoResume(profile, new DateTimeOffset(2026, 8, 20, 9, 0, 0, Offset)));
    }

    [Fact]
    public void IsAutoResumeStale_NoAutoResumeAt_NeverStale()
    {
        var profile = new SessionProfile { AutoResumeAt = null };

        Assert.False(ScheduleEvaluator.IsAutoResumeStale(profile, new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset)));
    }

    [Fact]
    public void IsAutoResumeStale_WithinWindow_NotStale()
    {
        var profile = new SessionProfile { AutoResumeAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset) };

        Assert.False(ScheduleEvaluator.IsAutoResumeStale(profile, new DateTimeOffset(2026, 8, 19, 12, 0, 0, Offset)));
    }

    [Fact]
    public void IsAutoResumeStale_BeyondWindow_IsStale()
    {
        var profile = new SessionProfile { AutoResumeAt = new DateTimeOffset(2026, 8, 19, 9, 0, 0, Offset) };

        Assert.True(ScheduleEvaluator.IsAutoResumeStale(profile, new DateTimeOffset(2026, 8, 20, 9, 0, 0, Offset)));
    }
}
