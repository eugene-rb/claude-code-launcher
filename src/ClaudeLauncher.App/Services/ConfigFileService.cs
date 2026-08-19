using System.IO;
using System.Text;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

public static class ConfigFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static IReadOnlyList<ConfigFileDefinition> UserDefinitions { get; } =
    [
        new("user-claude-md", "CLAUDE.md", "ユーザー全体のグローバル指示", "CLAUDE.md", ConfigFileScope.User),
        new("user-settings-json", "settings.json", "ユーザー全体の設定(権限・フック等)", "settings.json", ConfigFileScope.User),
    ];

    public static IReadOnlyList<ConfigFileDefinition> ProjectDefinitions { get; } =
    [
        new("project-claude-md", "CLAUDE.md", "プロジェクト共有の指示(Git管理対象)", "CLAUDE.md", ConfigFileScope.Project),
        new("project-settings-json", ".claude/settings.json", "プロジェクト共有の設定(Git管理対象)", Path.Combine(".claude", "settings.json"), ConfigFileScope.Project),
        new("project-settings-local-json", ".claude/settings.local.json", "このマシン専用のローカル設定(Git非管理)", Path.Combine(".claude", "settings.local.json"), ConfigFileScope.Project),
    ];

    public static string ResolveUserPath(ConfigFileDefinition definition) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", definition.RelativePath);

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
