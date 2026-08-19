using ClaudeLauncher.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeLauncher.App.ViewModels;

public partial class InstalledPluginItemViewModel : ObservableObject
{
    public InstalledPluginInfo Info { get; private set; }

    [ObservableProperty]
    private bool enabled;

    public InstalledPluginItemViewModel(InstalledPluginInfo info)
    {
        Info = info;
        enabled = info.Enabled;
    }
}
