namespace ClaudeLauncher.App.Models;

public sealed class SessionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string WorkingDirectory { get; set; } = string.Empty;

    public string AccentColorHex { get; set; } = "#0078D4";

    /// <summary>Which CLI this project launches with. Defaults to <see cref="AgentKind.ClaudeCode"/>
    /// (enum value 0) so profiles saved before this field existed keep working unchanged.</summary>
    public AgentKind AgentKind { get; set; } = AgentKind.ClaudeCode;

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

    /// <summary>The CLI to launch when <see cref="AutoResumeAt"/> fires. Null means resume the
    /// profile's current CLI natively; a different value means create a shared checkpoint and start
    /// that CLI with a continuation prompt.</summary>
    public AgentKind? AutoResumeAgentKind { get; set; }

    public SessionProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        WorkingDirectory = WorkingDirectory,
        AccentColorHex = AccentColorHex,
        AgentKind = AgentKind,
        CreatedAt = CreatedAt,
        LastLaunchedAt = LastLaunchedAt,
        ScheduleEnabled = ScheduleEnabled,
        Repeat = Repeat,
        ScheduledAt = ScheduledAt,
        DailyTime = DailyTime,
        AutoResumeAt = AutoResumeAt,
        AutoResumeAgentKind = AutoResumeAgentKind,
    };
}
