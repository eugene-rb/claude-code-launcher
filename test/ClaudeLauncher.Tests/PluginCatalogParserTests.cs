using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class PluginCatalogParserTests
{
    // Fixtures below are trimmed excerpts of the ACTUAL output of
    // `claude plugin list --json --available` and `claude plugin marketplace list --json`,
    // captured against a real install during development - not hand-guessed schema.
    private const string PluginListJson = """
        {
          "installed": [
            {
              "id": "chrome-devtools-mcp@chrome-devtools-plugins",
              "version": "1.6.0",
              "scope": "user",
              "enabled": true,
              "installPath": "C:\\Users\\kind4\\.claude\\plugins\\cache\\chrome-devtools-plugins\\chrome-devtools-mcp\\1.6.0",
              "installedAt": "2026-08-03T05:49:39.372Z",
              "lastUpdated": "2026-08-03T05:49:39.372Z",
              "mcpServers": { "chrome-devtools": { "command": "npx", "args": ["chrome-devtools-mcp@1.6.0"] } }
            },
            {
              "id": "frontend-design@claude-plugins-official",
              "version": "unknown",
              "scope": "user",
              "enabled": false,
              "installPath": "C:\\Users\\kind4\\.claude\\plugins\\cache\\claude-plugins-official\\frontend-design\\unknown",
              "installedAt": "2026-07-20T12:38:38.092Z",
              "lastUpdated": "2026-08-19T13:48:39.541Z"
            }
          ],
          "available": [
            {
              "pluginId": "42crunch-api-security-testing@claude-plugins-official",
              "name": "42crunch-api-security-testing",
              "description": "Automate API security directly in Claude Code with 42Crunch.",
              "marketplaceName": "claude-plugins-official",
              "source": { "source": "git-subdir", "url": "https://github.com/42Crunch-AI/claude-plugins.git", "path": "plugins/api-security-testing", "ref": "v1.5.5", "sha": "30287f5e3f122a646d1ac5ca3ab96e130c52a3ad" },
              "installCount": 2575
            }
          ]
        }
        """;

    private const string MarketplaceListJson = """
        [
          { "name": "anthropic-agent-skills", "source": "github", "repo": "anthropics/skills", "installLocation": "C:\\Users\\kind4\\.claude\\plugins\\marketplaces\\anthropic-agent-skills" },
          { "name": "ecc", "source": "git", "url": "https://github.com/affaan-m/ECC.git", "installLocation": "C:\\Users\\kind4\\.claude\\plugins\\marketplaces\\ecc" }
        ]
        """;

    [Fact]
    public void ParsePluginList_ParsesInstalledPlugins()
    {
        var (installed, _) = PluginCatalogParser.ParsePluginList(PluginListJson);

        Assert.Equal(2, installed.Count);
        Assert.Equal("chrome-devtools-mcp@chrome-devtools-plugins", installed[0].Id);
        Assert.Equal("1.6.0", installed[0].Version);
        Assert.Equal("user", installed[0].Scope);
        Assert.True(installed[0].Enabled);
        Assert.False(installed[1].Enabled);
        Assert.Equal("unknown", installed[1].Version);
    }

    [Fact]
    public void ParsePluginList_ParsesAvailablePlugins()
    {
        var (_, available) = PluginCatalogParser.ParsePluginList(PluginListJson);

        Assert.Single(available);
        Assert.Equal("42crunch-api-security-testing@claude-plugins-official", available[0].PluginId);
        Assert.Equal("claude-plugins-official", available[0].MarketplaceName);
        Assert.Equal(2575, available[0].InstallCount);
    }

    [Fact]
    public void ParsePluginList_MissingSections_ReturnsEmptyLists()
    {
        var (installed, available) = PluginCatalogParser.ParsePluginList("{}");

        Assert.Empty(installed);
        Assert.Empty(available);
    }

    [Fact]
    public void ParseMarketplaces_ParsesGithubAndGitSources()
    {
        var marketplaces = PluginCatalogParser.ParseMarketplaces(MarketplaceListJson);

        Assert.Equal(2, marketplaces.Count);
        Assert.Equal("github", marketplaces[0].Source);
        Assert.Equal("anthropics/skills", marketplaces[0].Repo);
        Assert.Null(marketplaces[0].Url);
        Assert.Equal("git", marketplaces[1].Source);
        Assert.Equal("https://github.com/affaan-m/ECC.git", marketplaces[1].Url);
        Assert.Null(marketplaces[1].Repo);
    }
}
