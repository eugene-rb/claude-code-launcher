using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeLauncher.App.ViewModels;

/// <summary>Backs the "拡張機能" tab: install/manage Claude Code plugins, marketplaces, and skills
/// by shelling out to `claude plugin ...` (see ClaudeCliArgs/ClaudeCliService). Exit codes for
/// these subcommands are undocumented, so this VM never claims success/failure - it appends the
/// raw command and output to <see cref="OperationLog"/> and lets the user judge.</summary>
public partial class ExtensionsViewModel : ObservableObject
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(45);

    private readonly ClaudeCliService _cli;

    public ObservableCollection<InstalledPluginItemViewModel> InstalledPlugins { get; } = [];

    public ObservableCollection<AvailablePluginInfo> AvailablePlugins { get; } = [];

    public ObservableCollection<MarketplaceInfo> Marketplaces { get; } = [];

    public ObservableCollection<SkillInfo> Skills { get; } = [];

    public ICollectionView AvailablePluginsView { get; }

    public bool HasLoadedOnce { get; private set; }

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string operationLog = string.Empty;

    [ObservableProperty]
    private string newMarketplaceSource = string.Empty;

    [ObservableProperty]
    private string newSkillName = string.Empty;

    [ObservableProperty]
    private string newSkillDescription = string.Empty;

    public ExtensionsViewModel()
        : this(new ClaudeCliService())
    {
    }

    public ExtensionsViewModel(ClaudeCliService cli)
    {
        _cli = cli;
        AvailablePluginsView = CollectionViewSource.GetDefaultView(AvailablePlugins);
        AvailablePluginsView.Filter = FilterAvailablePlugin;
    }

    partial void OnSearchTextChanged(string value) => AvailablePluginsView.Refresh();

    private bool FilterAvailablePlugin(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return obj is AvailablePluginInfo plugin
            && (plugin.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || plugin.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Called by the host when the tab is first selected, so the app doesn't shell out to
    /// `claude plugin list` (which hits the network for marketplace data) on every startup.</summary>
    public void EnsureLoaded()
    {
        if (HasLoadedOnce)
        {
            return;
        }

        HasLoadedOnce = true;
        RefreshCommand.Execute(null);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var listResult = await _cli.RunAsync(ClaudeCliArgs.List(), LongTimeout);
            AppendLog(ClaudeCliArgs.List(), listResult);
            if (!listResult.TimedOut && listResult.ExitCode == 0)
            {
                var (installed, available) = PluginCatalogParser.ParsePluginList(listResult.StdOut);
                InstalledPlugins.Clear();
                foreach (var plugin in installed)
                {
                    InstalledPlugins.Add(new InstalledPluginItemViewModel(plugin));
                }

                AvailablePlugins.Clear();
                foreach (var plugin in available)
                {
                    AvailablePlugins.Add(plugin);
                }
            }

            var marketResult = await _cli.RunAsync(ClaudeCliArgs.MarketplaceList(), ShortTimeout);
            AppendLog(ClaudeCliArgs.MarketplaceList(), marketResult);
            if (!marketResult.TimedOut && marketResult.ExitCode == 0)
            {
                Marketplaces.Clear();
                foreach (var market in PluginCatalogParser.ParseMarketplaces(marketResult.StdOut))
                {
                    Marketplaces.Add(market);
                }
            }

            RefreshSkills();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshSkills()
    {
        Skills.Clear();
        foreach (var skill in SkillFolderScanner.Scan(SkillFolderScanner.GetUserSkillsDirectory()))
        {
            Skills.Add(skill);
        }
    }

    [RelayCommand]
    private Task InstallPluginAsync(AvailablePluginInfo? plugin) =>
        plugin is null ? Task.CompletedTask : RunAndLogAsync(ClaudeCliArgs.Install(plugin.PluginId, "user"), LongTimeout, refreshAfter: true);

    [RelayCommand]
    private Task UninstallPluginAsync(InstalledPluginItemViewModel? item) =>
        item is null ? Task.CompletedTask : RunAndLogAsync(ClaudeCliArgs.Uninstall(item.Info.Id, item.Info.Scope), ShortTimeout, refreshAfter: true);

    [RelayCommand]
    private Task ToggleEnabledAsync(InstalledPluginItemViewModel? item) => item is null
        ? Task.CompletedTask
        : RunAndLogAsync(
            item.Enabled ? ClaudeCliArgs.Disable(item.Info.Id, item.Info.Scope) : ClaudeCliArgs.Enable(item.Info.Id, item.Info.Scope),
            ShortTimeout,
            refreshAfter: true);

    [RelayCommand]
    private async Task AddMarketplaceAsync()
    {
        if (string.IsNullOrWhiteSpace(NewMarketplaceSource))
        {
            return;
        }

        await RunAndLogAsync(ClaudeCliArgs.MarketplaceAdd(NewMarketplaceSource.Trim()), LongTimeout, refreshAfter: true);
        NewMarketplaceSource = string.Empty;
    }

    [RelayCommand]
    private Task RemoveMarketplaceAsync(MarketplaceInfo? marketplace) =>
        marketplace is null ? Task.CompletedTask : RunAndLogAsync(ClaudeCliArgs.MarketplaceRemove(marketplace.Name), ShortTimeout, refreshAfter: true);

    [RelayCommand]
    private async Task CreateSkillAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSkillName))
        {
            return;
        }

        var description = string.IsNullOrWhiteSpace(NewSkillDescription) ? null : NewSkillDescription.Trim();
        await RunAndLogAsync(ClaudeCliArgs.InitSkill(NewSkillName.Trim(), description), ShortTimeout, refreshAfter: true);
        NewSkillName = string.Empty;
        NewSkillDescription = string.Empty;
    }

    [RelayCommand]
    private Task ShowDetailsAsync(AvailablePluginInfo? plugin) =>
        plugin is null ? Task.CompletedTask : RunAndLogAsync(ClaudeCliArgs.Details(plugin.PluginId), ShortTimeout, refreshAfter: false);

    [RelayCommand]
    private Task ValidateSkillAsync(SkillInfo? skill) =>
        skill is null ? Task.CompletedTask : RunAndLogAsync(ClaudeCliArgs.Validate(skill.FolderPath), ShortTimeout, refreshAfter: false);

    [RelayCommand]
    private void OpenSkillFolder(SkillInfo? skill)
    {
        if (skill is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{skill.FolderPath}\"", UseShellExecute = true });
    }

    private async Task RunAndLogAsync(string[] args, TimeSpan timeout, bool refreshAfter)
    {
        IsBusy = true;
        try
        {
            var result = await _cli.RunAsync(args, timeout);
            AppendLog(args, result);
            if (refreshAfter && !result.TimedOut)
            {
                await RefreshAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLog(IReadOnlyList<string> args, CliResult result)
    {
        var timestamp = DateTimeOffset.Now.ToString("HH:mm:ss");
        var status = result.TimedOut
            ? "タイムアウト(状態不明。ターミナルで手動確認するか、少し待ってから再読み込みしてください)"
            : result.ExitCode is null
                ? "起動失敗"
                : $"終了コード {result.ExitCode}";

        OperationLog += $"[{timestamp}] $ claude {string.Join(' ', args)} -> {status}{Environment.NewLine}{result.StdOut}{result.StdErr}{Environment.NewLine}";
    }
}
