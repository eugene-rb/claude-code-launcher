using System.Text.Json;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Parses the JSON emitted by `claude plugin list --json --available` and
/// `claude plugin marketplace list --json`. These are undocumented output formats (verified
/// empirically against a real install, not from official docs), so parsing is defensive:
/// missing/unexpected fields are skipped rather than throwing, since a future CLI version could
/// add or rename fields.</summary>
public static class PluginCatalogParser
{
    public static (IReadOnlyList<InstalledPluginInfo> Installed, IReadOnlyList<AvailablePluginInfo> Available) ParsePluginList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var installed = new List<InstalledPluginInfo>();
        if (root.TryGetProperty("installed", out var installedArray) && installedArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in installedArray.EnumerateArray())
            {
                var id = GetString(item, "id");
                if (id is null)
                {
                    continue;
                }

                installed.Add(new InstalledPluginInfo(
                    Id: id,
                    Version: GetString(item, "version") ?? "unknown",
                    Scope: GetString(item, "scope") ?? "user",
                    Enabled: item.TryGetProperty("enabled", out var enabledEl) && enabledEl.ValueKind == JsonValueKind.True,
                    InstallPath: GetString(item, "installPath") ?? string.Empty,
                    InstalledAt: GetString(item, "installedAt")));
            }
        }

        var available = new List<AvailablePluginInfo>();
        if (root.TryGetProperty("available", out var availableArray) && availableArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in availableArray.EnumerateArray())
            {
                var pluginId = GetString(item, "pluginId");
                if (pluginId is null)
                {
                    continue;
                }

                available.Add(new AvailablePluginInfo(
                    PluginId: pluginId,
                    Name: GetString(item, "name") ?? pluginId,
                    Description: GetString(item, "description") ?? string.Empty,
                    MarketplaceName: GetString(item, "marketplaceName") ?? string.Empty,
                    InstallCount: item.TryGetProperty("installCount", out var countEl) && countEl.ValueKind == JsonValueKind.Number
                        ? countEl.GetInt32()
                        : 0));
            }
        }

        return (installed, available);
    }

    public static IReadOnlyList<MarketplaceInfo> ParseMarketplaces(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<MarketplaceInfo>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var name = GetString(item, "name");
            if (name is null)
            {
                continue;
            }

            result.Add(new MarketplaceInfo(
                Name: name,
                Source: GetString(item, "source") ?? "unknown",
                Repo: GetString(item, "repo"),
                Url: GetString(item, "url"),
                InstallLocation: GetString(item, "installLocation") ?? string.Empty));
        }

        return result;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
