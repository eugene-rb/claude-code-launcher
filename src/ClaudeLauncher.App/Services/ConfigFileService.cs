using System.IO;
using System.Text;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

public static class ConfigFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static IReadOnlyList<ConfigFileDefinition> UserDefinitions { get; } =
    [
        new("user-claude-md", "Claude: CLAUDE.md", "Claude Code のユーザー全体グローバル指示", "CLAUDE.md", ConfigFileScope.User),
        new("user-settings-json", "Claude: settings.json", "Claude Code のユーザー設定(権限・フック等)", "settings.json", ConfigFileScope.User),
        new("user-codex-agents-md", "Codex: AGENTS.md", "Codex のユーザー全体グローバル指示", "AGENTS.md", ConfigFileScope.User),
        new("user-codex-config-toml", "Codex: config.toml", "Codex のユーザー設定(モデル・権限等)", "config.toml", ConfigFileScope.User),
    ];

    /// <summary>Key of the canonical project instructions entry - saving this one also mirrors its
    /// content into <see cref="AgentsMdKey"/> (see <see cref="ViewModels.ConfigFilesViewModel.Save"/>),
    /// since Claude Code is this launcher's original and most-used AI.</summary>
    public const string ClaudeMdKey = "project-claude-md";

    /// <summary>Key of the shared instructions file Codex CLI/Kimi Code CLI/Antigravity CLI read
    /// (`AGENTS.md`). Independently editable like every other entry - see
    /// <see cref="ViewModels.ConfigFilesViewModel.Save"/> for the one-way CLAUDE.md → AGENTS.md
    /// mirroring that happens only when <see cref="ClaudeMdKey"/> itself is saved.</summary>
    public const string AgentsMdKey = "project-agents-md";

    public static IReadOnlyList<ConfigFileDefinition> ProjectDefinitions { get; } =
    [
        new(ClaudeMdKey, "CLAUDE.md", "プロジェクト共有の指示(Git管理対象)。保存するとAGENTS.mdにも自動コピーされます。", "CLAUDE.md", ConfigFileScope.Project),
        new(AgentsMdKey, "AGENTS.md", "他のAI(Codex CLI・Kimi Code CLI・Antigravity CLI)用の共有指示(Git管理対象)", "AGENTS.md", ConfigFileScope.Project),
        new("project-settings-json", ".claude/settings.json", "プロジェクト共有の設定(Git管理対象)", Path.Combine(".claude", "settings.json"), ConfigFileScope.Project),
        new("project-settings-local-json", ".claude/settings.local.json", "このマシン専用のローカル設定(Git非管理)", Path.Combine(".claude", "settings.local.json"), ConfigFileScope.Project),
    ];

    public static string ResolveUserPath(ConfigFileDefinition definition) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            definition.Key.StartsWith("user-codex-", StringComparison.Ordinal) ? ".codex" : ".claude",
            definition.RelativePath);

    public static string ResolveProjectPath(ConfigFileDefinition definition, string projectDirectory) =>
        Path.Combine(projectDirectory, definition.RelativePath);

    public static string Load(string path) => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;

    public static void Save(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Utf8NoBom);
    }
}
