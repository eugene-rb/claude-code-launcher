using System.Collections.ObjectModel;
using SmoothCoder.App.Models;
using SmoothCoder.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmoothCoder.App.ViewModels;

/// <summary>One selectable entry of the 「再開時の会話」 picker. <see cref="Label"/> is the combo box row;
/// <see cref="Detail"/> is the consequence shown underneath once that row is selected, so the trade-off
/// between the two modes is visible at the moment of choosing rather than buried in a tooltip.</summary>
public sealed record ResumeModeOption(ResumeMode Value, string Label, string Detail);

/// <summary>Backs the "設定" tab. Every property change is persisted immediately (no explicit save
/// button) since these are simple defaults, not a form with validation to complete first.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;
    private readonly StartupRegistrationService _startupRegistration;
    private readonly ClaudeStatusLineInstaller _statusLineInstaller;
    private string? _chainedStatusLine;
    private bool _loading;

    /// <summary>One editable row per <see cref="AgentCatalog"/> entry - the per-agent executable and
    /// default launch arguments used unless a project/launch overrides them. Populated in the
    /// constructor from <see cref="AppSettings.AgentSettings"/> (already migrated by
    /// <see cref="AppSettingsStore.Load"/> if this is the first load after upgrading).</summary>
    public ObservableCollection<AgentSettingsRowViewModel> AgentRows { get; } = [];

    [ObservableProperty]
    private bool autoResumeOnLimitEnabled;

    [ObservableProperty]
    private bool crossAgentHandoffEnabled = true;

    /// <summary>Spoken announcements at each unattended milestone. The service is owned here (rather
    /// than by <see cref="MainViewModel"/>) so the toggle and the volume slider drive the same instance
    /// every session card announces through - see <see cref="Voice"/>.</summary>
    [ObservableProperty]
    private bool voiceNotificationEnabled = true;

    /// <summary>Whether a finished turn is announced as well. Separate from
    /// <see cref="VoiceNotificationEnabled"/> because it is the one cue that fires during ordinary
    /// attended work - see <see cref="AppSettings.VoiceTurnCompleteEnabled"/>.</summary>
    [ObservableProperty]
    private bool voiceTurnCompleteEnabled = true;

    /// <summary>Volume as the slider shows it, 0-100. Stored as 0.0-1.0 in
    /// <see cref="AppSettings.VoiceNotificationVolume"/>; kept as a percentage here because a WPF
    /// Slider bound to a 0-1 range with a whole-number TickFrequency is awkward to configure.</summary>
    [ObservableProperty]
    private double voiceNotificationVolumePercent = 70;

    [ObservableProperty]
    private ResumeMode resumeMode;

    /// <summary>When on, the launcher registers its <c>usage-statusline</c> bridge as Claude Code's
    /// status-line command so the usage bars can show real figures. Writing to
    /// ~/.claude/settings.json happens the moment this flips - see
    /// <see cref="OnUsageStatusLineBridgeEnabledChanged"/>.</summary>
    [ObservableProperty]
    private bool usageStatusLineBridgeEnabled;

    /// <summary>Non-null after a failed install/uninstall attempt; shown under the toggle. Cleared on
    /// the next successful toggle.</summary>
    [ObservableProperty]
    private string? usageStatusLineBridgeError;

    /// <summary>Fixed picker contents for <see cref="ResumeMode"/>. Both modes suppress Claude Code's
    /// own blocking chooser; there is deliberately no "ask me" entry, because an unattended relaunch
    /// has nobody there to answer it and would simply sit on the dialog forever.</summary>
    public IReadOnlyList<ResumeModeOption> ResumeModeOptions { get; } =
    [
        new(ResumeMode.FullSession, "そのまま全部を再開する",
            "会話をそっくりそのまま引き継ぎます。中断前のやり取りを全部読み直すため、再開直後の1ターンで消費する量がいちばん大きくなります。"),
        new(ResumeMode.CompactFirst, "要約にまとめてから再開する",
            "再開した直後に /compact を実行し、会話を要約に置き換えてから続けます。利用上限に当たった直後の自動再開では、こちらのほうが消費を抑えられます。ただし会話が大きいほど要約の完了まで数分かかり、その間は入力できません。"),
    ];

    /// <summary>The <see cref="ResumeModeOption.Detail"/> of whichever mode is selected, so the view can
    /// bind one line of explanatory text instead of showing both modes' consequences at once.</summary>
    public string ResumeModeDetail =>
        ResumeModeOptions.First(option => option.Value == ResumeMode).Detail;

    /// <summary>Backed by the registry Run key, not <see cref="Models.AppSettings"/> - see
    /// <see cref="StartupRegistrationService"/> for why it's the sole source of truth.</summary>
    [ObservableProperty]
    private bool startWithWindowsEnabled;

    /// <summary>Same instance as <see cref="MainViewModel.Update"/> - shared so the version/"今すぐ確認"
    /// card here and the update-ready banner on the main window reflect one piece of state.</summary>
    public UpdateViewModel Update { get; }

    /// <summary>The one player every session announces through, kept in step with the toggle and
    /// slider above. Handed to each <see cref="SessionItemViewModel"/> by <see cref="MainViewModel"/>
    /// so cues from different projects queue behind one another instead of talking over each other.</summary>
    public VoiceNotificationService Voice { get; } = new();

    public SettingsViewModel()
        : this(new AppSettingsStore(), new StartupRegistrationService(), new UpdateViewModel())
    {
    }

    public SettingsViewModel(AppSettingsStore store, StartupRegistrationService startupRegistration, UpdateViewModel update, ClaudeStatusLineInstaller? statusLineInstaller = null)
    {
        _store = store;
        _startupRegistration = startupRegistration;
        _statusLineInstaller = statusLineInstaller ?? new ClaudeStatusLineInstaller();
        Update = update;

        var settings = _store.Load();
        _loading = true;
        _chainedStatusLine = settings.ChainedStatusLine;
        foreach (var agent in AgentCatalog.All)
        {
            var current = settings.AgentSettings.TryGetValue(agent.Kind, out var existing)
                ? existing
                : new AgentExecutionSettings { Executable = agent.DefaultExecutable, Arguments = agent.DefaultArguments };
            AgentRows.Add(new AgentSettingsRowViewModel(agent, current, Persist));
        }

        AutoResumeOnLimitEnabled = settings.AutoResumeOnLimitEnabled;
        CrossAgentHandoffEnabled = settings.CrossAgentHandoffEnabled;
        VoiceNotificationEnabled = settings.VoiceNotificationEnabled;
        VoiceTurnCompleteEnabled = settings.VoiceTurnCompleteEnabled;
        VoiceNotificationVolumePercent = Math.Clamp(settings.VoiceNotificationVolume, 0, 1) * 100;
        ApplyVoiceSettings();
        ResumeMode = settings.ResumeMode;
        // The file on disk is the source of truth - it can drift from the saved flag if the user
        // edited settings.json by hand or another tool took the status-line slot.
        UsageStatusLineBridgeEnabled = settings.UsageStatusLineBridgeEnabled && _statusLineInstaller.IsBridgeInstalled();
        StartWithWindowsEnabled = _startupRegistration.IsEnabled();
        _loading = false;
    }

    /// <summary>Resolves the executable configured for <paramref name="kind"/> in <see cref="AgentRows"/>,
    /// falling back to <see cref="AgentCatalog"/>'s default if the row is somehow missing or blank.</summary>
    public string GetExecutable(AgentKind kind) =>
        AgentRows.FirstOrDefault(r => r.Kind == kind)?.Executable is { Length: > 0 } exe
            ? exe
            : AgentCatalog.Get(kind).DefaultExecutable;

    /// <summary>Resolves the default launch arguments configured for <paramref name="kind"/> in
    /// <see cref="AgentRows"/>, falling back to <see cref="AgentCatalog"/>'s default if the row is
    /// somehow missing.</summary>
    public string GetArguments(AgentKind kind) =>
        AgentRows.FirstOrDefault(r => r.Kind == kind)?.Arguments ?? AgentCatalog.Get(kind).DefaultArguments;

    partial void OnAutoResumeOnLimitEnabledChanged(bool value) => Persist();

    partial void OnCrossAgentHandoffEnabledChanged(bool value) => Persist();

    partial void OnVoiceNotificationEnabledChanged(bool value)
    {
        ApplyVoiceSettings();
        Persist();
    }

    partial void OnVoiceTurnCompleteEnabledChanged(bool value)
    {
        ApplyVoiceSettings();
        Persist();
    }

    partial void OnVoiceNotificationVolumePercentChanged(double value)
    {
        ApplyVoiceSettings();
        Persist();
    }

    private void ApplyVoiceSettings()
    {
        Voice.Enabled = VoiceNotificationEnabled;
        Voice.TurnCompleteEnabled = VoiceTurnCompleteEnabled;
        Voice.Volume = Math.Clamp(VoiceNotificationVolumePercent / 100.0, 0, 1);
    }

    /// <summary>Plays one announcement at the current volume. The slider is otherwise unusable - its
    /// effect is inaudible until something happens to trigger a cue, which unattended is exactly when
    /// nobody is watching. Deliberately bypasses <see cref="VoiceNotificationEnabled"/>: pressing the
    /// button is an explicit request to hear it.</summary>
    [RelayCommand]
    private void TestVoice()
    {
        ApplyVoiceSettings();
        Voice.PlayTest();
    }

    partial void OnUsageStatusLineBridgeEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
            if (value)
            {
                _chainedStatusLine = _statusLineInstaller.Install(exePath);
            }
            else
            {
                _statusLineInstaller.Uninstall(_chainedStatusLine);
                _chainedStatusLine = null;
            }

            UsageStatusLineBridgeError = null;
            Persist();
        }
        catch (Exception ex)
        {
            UsageStatusLineBridgeError = $"設定の書き込みに失敗しました: {ex.Message}";
            _loading = true;
            UsageStatusLineBridgeEnabled = !value;
            _loading = false;
        }
    }

    partial void OnResumeModeChanged(ResumeMode value)
    {
        OnPropertyChanged(nameof(ResumeModeDetail));
        Persist();
    }

    partial void OnStartWithWindowsEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _startupRegistration.SetEnabled(value);
    }

    private void Persist()
    {
        if (_loading)
        {
            return;
        }

        var agentSettings = new Dictionary<AgentKind, AgentExecutionSettings>();
        foreach (var row in AgentRows)
        {
            agentSettings[row.Kind] = new AgentExecutionSettings
            {
                Executable = string.IsNullOrWhiteSpace(row.Executable) ? AgentCatalog.Get(row.Kind).DefaultExecutable : row.Executable.Trim(),
                Arguments = row.Arguments.Trim(),
            };
        }

        // Legacy fields are no longer read by this app, but keeping them mirrored to the Claude Code
        // row costs nothing and avoids stranding a downgrade on an old default.
        var claudeSettings = agentSettings.GetValueOrDefault(AgentKind.ClaudeCode);

        _store.Save(new()
        {
            DefaultExecutable = claudeSettings?.Executable ?? "claude",
            DefaultArguments = claudeSettings?.Arguments ?? string.Empty,
            AgentSettings = agentSettings,
            AutoResumeOnLimitEnabled = AutoResumeOnLimitEnabled,
            CrossAgentHandoffEnabled = CrossAgentHandoffEnabled,
            VoiceNotificationEnabled = VoiceNotificationEnabled,
            VoiceTurnCompleteEnabled = VoiceTurnCompleteEnabled,
            VoiceNotificationVolume = Math.Clamp(VoiceNotificationVolumePercent / 100.0, 0, 1),
            ResumeMode = ResumeMode,
            UsageStatusLineBridgeEnabled = UsageStatusLineBridgeEnabled,
            ChainedStatusLine = _chainedStatusLine,
        });
    }
}
