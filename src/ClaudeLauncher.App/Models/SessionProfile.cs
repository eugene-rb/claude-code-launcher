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
    };
}
