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
    /// Applies to every project - see <see cref="ViewModels.SessionItemViewModel.TryDetectUsageLimit"/>.
    /// <para>Defaults to true because <see cref="CrossAgentHandoffEnabled"/> also defaults to true and
    /// detection is what feeds it: with this off, <c>TryDetectUsageLimit</c> returns before it ever
    /// looks at the transcript, so the entire unattended failover chain is inert on a fresh install.
    /// Existing installs are unaffected - their saved value is always written out by
    /// <see cref="ViewModels.SettingsViewModel"/> and so survives the upgrade.</para></summary>
    public bool AutoResumeOnLimitEnabled { get; set; } = true;

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

    /// <summary>When true, the launcher speaks an announcement at each milestone: a limit was detected,
    /// the task moved to the other CLI, it was picked back up, both accounts ran out, an automatic
    /// resume failed, a turn finished, a session is waiting on the user. Common to Claude Code and
    /// Codex, with one honest exception: the "awaiting approval" cue comes from a Claude Code-only hook
    /// (see <see cref="Services.StatusMarkerStore"/>) and has no Codex equivalent.
    /// <para>This is the launcher's only voice. The Claude Code hook that used to play its own sounds
    /// was retired in favour of it, so nothing announces anything while the launcher isn't running.</para></summary>
    public bool VoiceNotificationEnabled { get; set; } = true;

    /// <summary>When true (and <see cref="VoiceNotificationEnabled"/> is on), a finished turn is
    /// announced too. Split out from the rest because it is the only cue that fires during ordinary
    /// attended work, many times an hour; the others each mark an unattended run changing state.
    /// Defaults on, since it is what the retired Claude Code hook did and turning it off silently would
    /// read as the notifications having broken.</summary>
    public bool VoiceTurnCompleteEnabled { get; set; } = true;

    /// <summary>Announcement volume, 0.0-1.0. Applied by scaling the samples (see
    /// <see cref="Services.WavAudio.Scale"/>), so it is the launcher's own level and independent of
    /// the Windows mixer. Defaults audibly rather than to 0 so a fresh install isn't silent in a way
    /// that reads as broken; a settings file written before this field existed simply leaves the
    /// initializer's value in place, since the deserializer only assigns properties the JSON actually
    /// carries. Silence is <see cref="VoiceNotificationEnabled"/>'s job, so an explicit 0 here is kept
    /// as the user's choice rather than corrected.</summary>
    public double VoiceNotificationVolume { get; set; } = 0.7;

    /// <summary>Raw JSON of the <c>statusLine</c> object that was already in ~/.claude/settings.json
    /// when the bridge was installed, stashed here so <see cref="Services.UsageStatusLineBridge"/> can
    /// keep running it (chained) and so uninstalling restores it exactly. Null when the user had no
    /// status line, which is the common case.</summary>
    public string? ChainedStatusLine { get; set; }
}
