using System.Collections.ObjectModel;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeLauncher.App.ViewModels;

public partial class ConfigFilesViewModel : ObservableObject
{
    private string _loadedContent = string.Empty;

    public ObservableCollection<SessionItemViewModel> AvailableSessions { get; }

    public ObservableCollection<ConfigFileEntryViewModel> GlobalEntries { get; } = [];

    public ObservableCollection<ConfigFileEntryViewModel> ProjectEntries { get; } = [];

    [ObservableProperty]
    private string _projectDirectory = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    private ConfigFileEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public bool IsDirty => Content != _loadedContent;

    public ConfigFilesViewModel(ObservableCollection<SessionItemViewModel> sessions)
    {
        AvailableSessions = sessions;

        foreach (var definition in ConfigFileService.UserDefinitions)
        {
            GlobalEntries.Add(new ConfigFileEntryViewModel(definition, ConfigFileService.ResolveUserPath(definition)));
        }

        RebuildProjectEntries();
    }

    partial void OnProjectDirectoryChanged(string value)
    {
        if (SelectedEntry is { Definition.Scope: ConfigFileScope.Project })
        {
            SelectedEntry = null;
            Content = string.Empty;
            _loadedContent = string.Empty;
        }

        RebuildProjectEntries();
    }

    partial void OnContentChanged(string value) => OnPropertyChanged(nameof(IsDirty));

    private void RebuildProjectEntries()
    {
        ProjectEntries.Clear();

        if (string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            return;
        }

        foreach (var definition in ConfigFileService.ProjectDefinitions)
        {
            var path = ConfigFileService.ResolveProjectPath(definition, ProjectDirectory);
            ProjectEntries.Add(new ConfigFileEntryViewModel(definition, path));
        }
    }

    public void SelectEntry(ConfigFileEntryViewModel entry)
    {
        SelectedEntry = entry;
        _loadedContent = ConfigFileService.Load(entry.ResolvedPath);
        Content = _loadedContent;
        StatusMessage = entry.Exists ? null : "ファイルが存在しません。保存すると新規作成されます。";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        ConfigFileService.Save(SelectedEntry.ResolvedPath, Content);
        SelectedEntry.RefreshExists();
        _loadedContent = Content;
        OnPropertyChanged(nameof(IsDirty));
        StatusMessage = "保存しました。";
    }

    private bool CanSave() => SelectedEntry is not null;

    [RelayCommand(CanExecute = nameof(CanReload))]
    private void Reload()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        _loadedContent = ConfigFileService.Load(SelectedEntry.ResolvedPath);
        Content = _loadedContent;
        StatusMessage = SelectedEntry.Exists ? null : "ファイルが存在しません。保存すると新規作成されます。";
    }

    private bool CanReload() => SelectedEntry is not null;
}
