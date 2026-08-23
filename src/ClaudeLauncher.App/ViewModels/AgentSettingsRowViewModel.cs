using ClaudeLauncher.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeLauncher.App.ViewModels;

/// <summary>One row of the 設定 tab's per-agent executable/起動オプション editor. Every change persists
/// immediately via <paramref name="onChanged"/> (see <see cref="SettingsViewModel"/>'s
/// "no save button, persist on change" convention) - kept editable rather than fixed to
/// <see cref="Services.AgentCatalog"/>'s defaults so a wrong guess about a CLI's binary name or flags
/// costs the user a text-box edit, not a rebuild.</summary>
public partial class AgentSettingsRowViewModel(AgentDefinition definition, AgentExecutionSettings current, Action onChanged)
    : ObservableObject
{
    private readonly Action _onChanged = onChanged;

    public AgentKind Kind { get; } = definition.Kind;

    public string DisplayName { get; } = definition.DisplayName;

    [ObservableProperty]
    private string executable = current.Executable;

    [ObservableProperty]
    private string arguments = current.Arguments;

    partial void OnExecutableChanged(string value) => _onChanged();

    partial void OnArgumentsChanged(string value) => _onChanged();
}
