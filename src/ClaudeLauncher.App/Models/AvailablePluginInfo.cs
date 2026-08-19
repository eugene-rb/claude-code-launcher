namespace ClaudeLauncher.App.Models;

public sealed record AvailablePluginInfo(
    string PluginId,
    string Name,
    string Description,
    string MarketplaceName,
    int InstallCount);
