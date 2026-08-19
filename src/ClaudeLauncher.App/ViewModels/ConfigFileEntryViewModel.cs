using System.IO;
using ClaudeLauncher.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeLauncher.App.ViewModels;

public partial class ConfigFileEntryViewModel : ObservableObject
{
    public ConfigFileDefinition Definition { get; }

    public string ResolvedPath { get; }

    [ObservableProperty]
    private bool _exists;

    public ConfigFileEntryViewModel(ConfigFileDefinition definition, string resolvedPath)
    {
        Definition = definition;
        ResolvedPath = resolvedPath;
        _exists = File.Exists(resolvedPath);
    }

    public void RefreshExists() => Exists = File.Exists(ResolvedPath);
}
