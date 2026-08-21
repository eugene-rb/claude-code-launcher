using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeLauncher.App.ViewModels;

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
        StartWithWindowsEnabled = _startupRegistration.IsEnabled();
        _loading = false;
    }

    partial void OnDefaultExecutableChanged(string value) => Persist();

    partial void OnDefaultArgumentsChanged(string value) => Persist();

    partial void OnAutoResumeOnLimitEnabledChanged(bool value) => Persist();

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
        });
    }
}
