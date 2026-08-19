using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class ConfigFileServiceTests
{
    [Fact]
    public void ResolveUserPath_ClaudeMd_PointsUnderDotClaudeInUserProfile()
    {
        var definition = ConfigFileService.UserDefinitions.Single(d => d.Key == "user-claude-md");
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "CLAUDE.md");

        Assert.Equal(expected, ConfigFileService.ResolveUserPath(definition));
    }

    [Fact]
    public void ResolveUserPath_SettingsJson_PointsUnderDotClaudeInUserProfile()
    {
        var definition = ConfigFileService.UserDefinitions.Single(d => d.Key == "user-settings-json");
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        Assert.Equal(expected, ConfigFileService.ResolveUserPath(definition));
    }

    [Theory]
    [InlineData("project-claude-md", "CLAUDE.md")]
    [InlineData("project-settings-json", "settings.json")]
    [InlineData("project-settings-local-json", "settings.local.json")]
    public void ResolveProjectPath_CombinesProjectDirectoryWithRelativePath(string key, string expectedFileName)
    {
        var definition = ConfigFileService.ProjectDefinitions.Single(d => d.Key == key);
        var projectDir = Path.Combine("C:\\", "repo");

        var resolved = ConfigFileService.ResolveProjectPath(definition, projectDir);

        Assert.Equal(expectedFileName, Path.GetFileName(resolved));
        Assert.StartsWith(projectDir, resolved);
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsContentAndCreatesMissingParentDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(tempRoot, ".claude", "settings.local.json");

        try
        {
            const string content = "{ \"hello\": \"世界\" }";

            ConfigFileService.Save(path, content);
            var loaded = ConfigFileService.Load(path);

            Assert.Equal(content, loaded);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyString()
    {
        var path = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"), "CLAUDE.md");

        Assert.Equal(string.Empty, ConfigFileService.Load(path));
    }
}
