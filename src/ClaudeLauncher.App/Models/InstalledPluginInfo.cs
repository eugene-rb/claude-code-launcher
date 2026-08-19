namespace ClaudeLauncher.App.Models;

public sealed record InstalledPluginInfo(
    string Id,
    string Version,
    string Scope,
    bool Enabled,
    string InstallPath,
    string? InstalledAt);
