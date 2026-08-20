namespace ClaudeLauncher.App.Models;

/// <summary>App-wide defaults, shared by every project (as opposed to <see cref="SessionProfile"/>,
/// which holds per-project state like the working directory and schedule).</summary>
public sealed class AppSettings
{
    public string DefaultExecutable { get; set; } = "claude";

    public string DefaultArguments { get; set; } = string.Empty;

    /// <summary>When true, a running session's transcript is polled for a usage-limit ("rate_limit")
    /// event; on detection the session is auto-relaunched with `-c` 5 minutes after the reset time.
    /// Applies to every project - see <see cref="ViewModels.SessionItemViewModel.TryDetectUsageLimit"/>.</summary>
    public bool AutoResumeOnLimitEnabled { get; set; }
}
