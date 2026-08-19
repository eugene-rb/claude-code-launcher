namespace ClaudeLauncher.App.Services;

/// <summary>Builds argument arrays for `claude plugin ...` subcommands. Pure/testable - no process
/// I/O. Flags verified against `claude plugin &lt;subcommand&gt; --help` output, not documentation
/// guesses: `enable`/`disable`/`marketplace add`/`marketplace remove` have no -y/--yes flag.</summary>
public static class ClaudeCliArgs
{
    public static string[] List() => ["plugin", "list", "--json", "--available"];

    public static string[] MarketplaceList() => ["plugin", "marketplace", "list", "--json"];

    public static string[] Install(string pluginId, string scope) =>
        ["plugin", "install", pluginId, "-s", scope, "-y"];

    public static string[] Uninstall(string pluginId, string scope) =>
        ["plugin", "uninstall", pluginId, "-s", scope, "-y"];

    public static string[] Enable(string pluginId, string scope) =>
        ["plugin", "enable", pluginId, "-s", scope];

    public static string[] Disable(string pluginId, string scope) =>
        ["plugin", "disable", pluginId, "-s", scope];

    public static string[] MarketplaceAdd(string source) =>
        ["plugin", "marketplace", "add", source];

    public static string[] MarketplaceRemove(string name) =>
        ["plugin", "marketplace", "remove", name];

    public static string[] Details(string pluginId) => ["plugin", "details", pluginId];

    public static string[] Validate(string path) => ["plugin", "validate", path];

    public static string[] InitSkill(string name, string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? ["plugin", "init", name]
            : ["plugin", "init", name, "--description", description];
}
