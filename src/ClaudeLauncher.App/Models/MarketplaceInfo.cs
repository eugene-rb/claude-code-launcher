namespace ClaudeLauncher.App.Models;

public sealed record MarketplaceInfo(
    string Name,
    string Source,
    string? Repo,
    string? Url,
    string InstallLocation);
