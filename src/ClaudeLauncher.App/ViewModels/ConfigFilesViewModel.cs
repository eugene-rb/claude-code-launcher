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

        // CLAUDE.md is the canonical project instructions file in this launcher; mirroring it into
        // AGENTS.md one-way keeps Codex CLI/Kimi Code CLI/Antigravity CLI (which all read AGENTS.md)
        // in sync without a separate sync step. AGENTS.md itself stays independently editable/savable
        // for AI-specific customization - that edit simply gets overwritten the next time CLAUDE.md is
        // saved, which is called out next to the CLAUDE.md editor in the view.
        if (SelectedEntry.Definition.Key == ConfigFileService.ClaudeMdKey)
        {
            MirrorToAgentsMd(Content);
            StatusMessage = "保存しました。AGENTS.md(他のAI用)にもコピーしました。";
        }
        else
        {
            StatusMessage = "保存しました。";
        }
    }

    /// <summary>Writes CLAUDE.md's just-saved content to the same project's AGENTS.md path and
    /// refreshes that entry's <see cref="ConfigFileEntryViewModel.Exists"/> flag. Only called while
    /// <see cref="SelectedEntry"/> is the CLAUDE.md entry, so AGENTS.md is never the one currently open
    /// in the editor at this point - no need to refresh <see cref="Content"/>.</summary>
    private void MirrorToAgentsMd(string content)
    {
        var agentsEntry = ProjectEntries.FirstOrDefault(e => e.Definition.Key == ConfigFileService.AgentsMdKey);
        if (agentsEntry is null)
        {
            return;
        }

        ConfigFileService.Save(agentsEntry.ResolvedPath, content);
        agentsEntry.RefreshExists();
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
