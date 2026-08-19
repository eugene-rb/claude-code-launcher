using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class ClaudeCliArgsTests
{
    [Fact]
    public void List_ReturnsExpectedArgs() =>
        Assert.Equal(["plugin", "list", "--json", "--available"], ClaudeCliArgs.List());

    [Fact]
    public void MarketplaceList_ReturnsExpectedArgs() =>
        Assert.Equal(["plugin", "marketplace", "list", "--json"], ClaudeCliArgs.MarketplaceList());

    [Fact]
    public void Install_IncludesScopeAndYesFlag() =>
        Assert.Equal(["plugin", "install", "foo@bar", "-s", "user", "-y"], ClaudeCliArgs.Install("foo@bar", "user"));

    [Fact]
    public void Uninstall_IncludesScopeAndYesFlag() =>
        Assert.Equal(["plugin", "uninstall", "foo@bar", "-s", "project", "-y"], ClaudeCliArgs.Uninstall("foo@bar", "project"));

    [Fact]
    public void Enable_HasNoYesFlag() =>
        Assert.Equal(["plugin", "enable", "foo@bar", "-s", "user"], ClaudeCliArgs.Enable("foo@bar", "user"));

    [Fact]
    public void Disable_HasNoYesFlag() =>
        Assert.Equal(["plugin", "disable", "foo@bar", "-s", "user"], ClaudeCliArgs.Disable("foo@bar", "user"));

    [Fact]
    public void MarketplaceAdd_HasNoYesFlag() =>
        Assert.Equal(["plugin", "marketplace", "add", "https://github.com/foo/bar"], ClaudeCliArgs.MarketplaceAdd("https://github.com/foo/bar"));

    [Fact]
    public void MarketplaceRemove_HasNoYesFlag() =>
        Assert.Equal(["plugin", "marketplace", "remove", "foo"], ClaudeCliArgs.MarketplaceRemove("foo"));

    [Fact]
    public void Details_ReturnsExpectedArgs() =>
        Assert.Equal(["plugin", "details", "foo@bar"], ClaudeCliArgs.Details("foo@bar"));

    [Fact]
    public void Validate_ReturnsExpectedArgs() =>
        Assert.Equal(["plugin", "validate", "C:\\some\\path"], ClaudeCliArgs.Validate("C:\\some\\path"));

    [Fact]
    public void InitSkill_WithoutDescription_OmitsFlag() =>
        Assert.Equal(["plugin", "init", "my-skill"], ClaudeCliArgs.InitSkill("my-skill", null));

    [Fact]
    public void InitSkill_WithDescription_IncludesFlag() =>
        Assert.Equal(["plugin", "init", "my-skill", "--description", "does a thing"], ClaudeCliArgs.InitSkill("my-skill", "does a thing"));
}
