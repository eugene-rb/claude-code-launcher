namespace ClaudeLauncher.App.Models;

/// <summary>App-wide defaults, shared by every project (as opposed to <see cref="SessionProfile"/>,
/// which holds per-project state like the working directory and schedule).</summary>
public sealed class AppSettings
{
    /// <summary>Legacy pre-multi-AI fields, kept only so <see cref="Services.AppSettingsStore.Load"/>
    /// can migrate an existing settings.json's customized executable/arguments into
    /// <see cref="AgentSettings"/>'s Claude Code entry the first time it's loaded after upgrading.
    /// Not read anywhere else - use <see cref="AgentSettings"/> instead.</summary>
    public string DefaultExecutable { get; set; } = "claude";

    public string DefaultArguments { get; set; } = string.Empty;

    /// <summary>Per-agent executable/default-arguments, editable in the 設定 tab. Populated with
    /// <see cref="Services.AgentCatalog"/>'s defaults (migrated from <see cref="DefaultExecutable"/>/
    /// <see cref="DefaultArguments"/> for Claude Code) the first time settings are loaded.</summary>
    public Dictionary<AgentKind, AgentExecutionSettings> AgentSettings { get; set; } = new();

    /// <summary>When true, a running session's transcript is polled for a usage-limit ("rate_limit")
    /// event; on detection the session is auto-relaunched with `-c` 5 minutes after the reset time.
    /// Applies to every project - see <see cref="ViewModels.SessionItemViewModel.TryDetectUsageLimit"/>.</summary>
    public bool AutoResumeOnLimitEnabled { get; set; }

    /// <summary>When a Claude Code or Codex CLI session reaches its account limit, immediately
    /// continue with the other CLI using a shared transcript checkpoint instead of waiting for the
    /// limited account's reset time. Enabled by default so a newly installed launcher provides the
    /// seamless failover behavior without an additional setup step.</summary>
    public bool CrossAgentHandoffEnabled { get; set; } = true;

    /// <summary>Which branch of Claude Code's "Resume from summary?" chooser a resume launch takes on
    /// the user's behalf. Applies to both the scheduled auto-resume and the manual "再開" button, since
    /// neither can leave a blocking chooser on screen for an unattended relaunch to answer.</summary>
    public ResumeMode ResumeMode { get; set; }

    /// <summary>When true, the launcher has registered its <c>usage-statusline</c> bridge as Claude
    /// Code's status-line command in ~/.claude/settings.json, so the usage bars can show real
    /// <c>used_percentage</c> figures instead of a token estimate. Toggled from the 設定 tab; see
    /// <see cref="Services.ClaudeStatusLineInstaller"/>.</summary>
    public bool UsageStatusLineBridgeEnabled { get; set; }

    /// <summary>Raw JSON of the <c>statusLine</c> object that was already in ~/.claude/settings.json
    /// when the bridge was installed, stashed here so <see cref="Services.UsageStatusLineBridge"/> can
    /// keep running it (chained) and so uninstalling restores it exactly. Null when the user had no
    /// status line, which is the common case.</summary>
    public string? ChainedStatusLine { get; set; }
}
