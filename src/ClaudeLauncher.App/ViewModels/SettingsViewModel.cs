using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeLauncher.App.ViewModels;

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
    private bool _loading;

    [ObservableProperty]
    private string defaultExecutable = "claude";

    [ObservableProperty]
    private string defaultArguments = string.Empty;

    [ObservableProperty]
    private bool autoResumeOnLimitEnabled;

    [ObservableProperty]
    private ResumeMode resumeMode;

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

    public SettingsViewModel()
        : this(new AppSettingsStore(), new StartupRegistrationService(), new UpdateViewModel())
    {
    }

    public SettingsViewModel(AppSettingsStore store, StartupRegistrationService startupRegistration, UpdateViewModel update)
    {
        _store = store;
        _startupRegistration = startupRegistration;
        Update = update;

        var settings = _store.Load();
        _loading = true;
        DefaultExecutable = settings.DefaultExecutable;
        DefaultArguments = settings.DefaultArguments;
        AutoResumeOnLimitEnabled = settings.AutoResumeOnLimitEnabled;
        ResumeMode = settings.ResumeMode;
        StartWithWindowsEnabled = _startupRegistration.IsEnabled();
        _loading = false;
    }

    partial void OnDefaultExecutableChanged(string value) => Persist();

    partial void OnDefaultArgumentsChanged(string value) => Persist();

    partial void OnAutoResumeOnLimitEnabledChanged(bool value) => Persist();

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

        _store.Save(new()
        {
            DefaultExecutable = string.IsNullOrWhiteSpace(DefaultExecutable) ? "claude" : DefaultExecutable.Trim(),
            DefaultArguments = DefaultArguments.Trim(),
            AutoResumeOnLimitEnabled = AutoResumeOnLimitEnabled,
            ResumeMode = ResumeMode,
        });
    }
}
