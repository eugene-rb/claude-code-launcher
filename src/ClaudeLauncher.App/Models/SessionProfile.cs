namespace ClaudeLauncher.App.Models;

public sealed class SessionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    public string Executable { get; set; } = "claude";

    public string Arguments { get; set; } = string.Empty;

    public string AccentColorHex { get; set; } = "#0078D4";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? LastLaunchedAt { get; set; }

    public bool ScheduleEnabled { get; set; }

    public ScheduleRepeat Repeat { get; set; } = ScheduleRepeat.Once;

    public DateTimeOffset? ScheduledAt { get; set; }

    public TimeSpan? DailyTime { get; set; }

    /// <summary>When true, a running session's transcript is polled for a usage-limit ("rate_limit")
    /// event; on detection <see cref="AutoResumeAt"/> is set to the parsed reset time + 5 minutes.</summary>
    public bool LimitDetectionEnabled { get; set; }

    /// <summary>Set by auto-detection or the manual override once a resume time is known; cleared
    /// after the resume launch fires (see <c>ScheduleEvaluator.ShouldAutoResume</c>).</summary>
    public DateTimeOffset? AutoResumeAt { get; set; }

    public SessionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        WorkingDirectory = WorkingDirectory,
        Executable = Executable,
        Arguments = Arguments,
        AccentColorHex = AccentColorHex,
        CreatedAt = CreatedAt,
        LastLaunchedAt = LastLaunchedAt,
        ScheduleEnabled = ScheduleEnabled,
        Repeat = Repeat,
        ScheduledAt = ScheduledAt,
        DailyTime = DailyTime,
        LimitDetectionEnabled = LimitDetectionEnabled,
        AutoResumeAt = AutoResumeAt,
    };
}
