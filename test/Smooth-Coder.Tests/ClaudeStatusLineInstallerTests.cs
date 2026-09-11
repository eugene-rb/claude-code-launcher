using System.Text.Json;
using System.Text.Json.Nodes;
using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class ClaudeStatusLineInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "SmoothCoderStatusLineTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _settingsPath;

    public ClaudeStatusLineInstallerTests()
    {
        Directory.CreateDirectory(_dir);
        _settingsPath = Path.Combine(_dir, "settings.json");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private JsonObject ReadSettings() => (JsonObject)JsonNode.Parse(File.ReadAllText(_settingsPath))!;

    [Fact]
    public void Install_IntoExistingSettings_AddsStatusLineAndKeepsUnrelatedKeys()
    {
        File.WriteAllText(_settingsPath, """{"model":"haiku","hooks":{"Stop":[]}}""");
        var installer = new ClaudeStatusLineInstaller(_settingsPath);

        var stashed = installer.Install(@"C:\apps\Smooth-Coder.App.exe");

        Assert.Null(stashed);
        var root = ReadSettings();
        Assert.Equal("haiku", (string?)root["model"]);
        Assert.NotNull(root["hooks"]);
        Assert.Contains("usage-statusline", (string?)root["statusLine"]!["command"]);
        Assert.True(installer.IsBridgeInstalled());
    }

    [Fact]
    public void Install_NoSettingsFile_CreatesOne()
    {
        var installer = new ClaudeStatusLineInstaller(_settingsPath);

        installer.Install(@"C:\apps\x.exe");

        Assert.True(File.Exists(_settingsPath));
        Assert.True(installer.IsBridgeInstalled());
        // No backup when there was nothing to back up.
        Assert.Empty(Directory.GetFiles(_dir, "*.bak-smoothcoder-*"));
    }

    [Fact]
    public void Install_WithExistingStatusLine_StashesItAndBacksUp()
    {
        File.WriteAllText(_settingsPath, """{"statusLine":{"type":"command","command":"my-prompt.sh"}}""");
        var installer = new ClaudeStatusLineInstaller(_settingsPath);

        var stashed = installer.Install(@"C:\apps\x.exe");

        Assert.NotNull(stashed);
        Assert.Contains("my-prompt.sh", stashed);
        Assert.Single(Directory.GetFiles(_dir, "*.bak-smoothcoder-*"));
    }

    [Fact]
    public void Uninstall_RestoresStashedStatusLine()
    {
        File.WriteAllText(_settingsPath, """{"statusLine":{"type":"command","command":"my-prompt.sh"}}""");
        var installer = new ClaudeStatusLineInstaller(_settingsPath);
        var stashed = installer.Install(@"C:\apps\x.exe");

        installer.Uninstall(stashed);

        Assert.Equal("my-prompt.sh", (string?)ReadSettings()["statusLine"]!["command"]);
        Assert.False(installer.IsBridgeInstalled());
    }

    [Fact]
    public void Uninstall_NoStash_RemovesStatusLineKeyEntirely()
    {
        File.WriteAllText(_settingsPath, """{"model":"haiku"}""");
        var installer = new ClaudeStatusLineInstaller(_settingsPath);
        installer.Install(@"C:\apps\x.exe");

        installer.Uninstall(null);

        var root = ReadSettings();
        Assert.False(root.ContainsKey("statusLine"));
        Assert.Equal("haiku", (string?)root["model"]);
    }

    [Fact]
    public void Uninstall_UserReplacedStatusLineByHand_LeavesItAlone()
    {
        var installer = new ClaudeStatusLineInstaller(_settingsPath);
        installer.Install(@"C:\apps\x.exe");
        File.WriteAllText(_settingsPath, """{"statusLine":{"type":"command","command":"user-own.sh"}}""");

        installer.Uninstall(null);

        Assert.Equal("user-own.sh", (string?)ReadSettings()["statusLine"]!["command"]);
    }

    [Fact]
    public void Install_Twice_DoesNotStashOwnEntry()
    {
        var installer = new ClaudeStatusLineInstaller(_settingsPath);
        installer.Install(@"C:\apps\x.exe");

        var stashed = installer.Install(@"C:\apps\y.exe");

        Assert.Null(stashed);
        Assert.Contains("y.exe", (string?)ReadSettings()["statusLine"]!["command"]);
    }

    [Fact]
    public void RefreshPathIfInstalled_RepointsCommandToCurrentExe()
    {
        var installer = new ClaudeStatusLineInstaller(_settingsPath);
        installer.Install(@"C:\old\x.exe");

        installer.RefreshPathIfInstalled(@"C:\new\x.exe");

        Assert.Contains(@"C:\new\x.exe", (string?)ReadSettings()["statusLine"]!["command"]);
    }
}
