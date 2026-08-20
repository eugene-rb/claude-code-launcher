using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeLauncher.App.ViewModels;

/// <summary>Backs the "設定" tab. Every property change is persisted immediately (no explicit save
/// button) since these are simple defaults, not a form with validation to complete first.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsStore _store;
    private bool _loading;

    [ObservableProperty]
    private string defaultExecutable = "claude";

    [ObservableProperty]
    private string defaultArguments = string.Empty;

    [ObservableProperty]
    private bool autoResumeOnLimitEnabled;

    public SettingsViewModel()
        : this(new AppSettingsStore())
    {
    }

    public SettingsViewModel(AppSettingsStore store)
    {
        _store = store;

        var settings = _store.Load();
        _loading = true;
        DefaultExecutable = settings.DefaultExecutable;
        DefaultArguments = settings.DefaultArguments;
        AutoResumeOnLimitEnabled = settings.AutoResumeOnLimitEnabled;
        _loading = false;
    }

    partial void OnDefaultExecutableChanged(string value) => Persist();

    partial void OnDefaultArgumentsChanged(string value) => Persist();

    partial void OnAutoResumeOnLimitEnabledChanged(bool value) => Persist();

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
