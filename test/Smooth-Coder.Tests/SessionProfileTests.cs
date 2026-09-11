using SmoothCoder.App.Models;

namespace SmoothCoder.Tests;

public class SessionProfileTests
{
    [Fact]
    public void Clone_PreservesLastUsedAgentKindSeparatelyFromConfiguredDefault()
    {
        var profile = new SessionProfile
        {
            AgentKind = AgentKind.ClaudeCode,
            LastUsedAgentKind = AgentKind.CodexCli,
        };

        var clone = profile.Clone();

        Assert.Equal(AgentKind.ClaudeCode, clone.AgentKind);
        Assert.Equal(AgentKind.CodexCli, clone.LastUsedAgentKind);
    }

    [Fact]
    public void NewProfile_HasNoLastUsedAgentAndFallsBackToConfiguredDefault()
    {
        var profile = new SessionProfile { AgentKind = AgentKind.CodexCli };

        Assert.Null(profile.LastUsedAgentKind);
        Assert.Equal(AgentKind.CodexCli, profile.LastUsedAgentKind ?? profile.AgentKind);
    }
}
