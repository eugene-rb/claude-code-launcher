namespace ClaudeLauncher.App.Models;

public sealed class SessionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    public string AccentColorHex { get; set; } = "#0078D4";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? LastLaunchedAt { get; set; }

    public bool ScheduleEnabled { get; set; }

    public ScheduleRepeat Repeat { get; set; } = ScheduleRepeat.Once;

    public DateTimeOffset? ScheduledAt { get; set; }

    public TimeSpan? DailyTime { get; set; }

    /// <summary>Set by auto-detection or the manual override once a resume time is known; cleared
    /// after the resume launch fires (see <c>ScheduleEvaluator.ShouldAutoResume</c>). Detection itself
    /// is gated by the app-wide <see cref="AppSettings.AutoResumeOnLimitEnabled"/>, not a per-project
    /// flag - a project that hits its usage limit behaves the same as any other.</summary>
    public DateTimeOffset? AutoResumeAt { get; set; }

    public SessionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        WorkingDirectory = WorkingDirectory,
        AccentColorHex = AccentColorHex,
        CreatedAt = CreatedAt,
        LastLaunchedAt = LastLaunchedAt,
        ScheduleEnabled = ScheduleEnabled,
        Repeat = Repeat,
        ScheduledAt = ScheduledAt,
        DailyTime = DailyTime,
        AutoResumeAt = AutoResumeAt,
    };
}
