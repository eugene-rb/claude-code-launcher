using SmoothCoder.App.Models;
using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class AgentCatalogTests
{
    [Theory]
    [InlineData(AgentKind.ClaudeCode, "claude", "CLAUDE.md")]
    [InlineData(AgentKind.CodexCli, "codex", "AGENTS.md")]
    [InlineData(AgentKind.KimiCodeCli, "kimi", "AGENTS.md")]
    [InlineData(AgentKind.AntigravityCli, "agy", "AGENTS.md")]
    public void Get_ReturnsResearchedFactsForEachAgent(AgentKind kind, string expectedExecutable, string expectedInstructionFile)
    {
        var definition = AgentCatalog.Get(kind);

        Assert.Equal(kind, definition.Kind);
        Assert.Equal(expectedExecutable, definition.DefaultExecutable);
        Assert.Equal(expectedInstructionFile, definition.ProjectInstructionFileName);
    }

    [Fact]
    public void Get_UnknownEnumValue_FallsBackToClaudeCode()
    {
        var definition = AgentCatalog.Get((AgentKind)999);

        Assert.Equal(AgentKind.ClaudeCode, definition.Kind);
    }

    [Fact]
    public void All_ContainsExactlyFourAgentsWithDistinctKinds()
    {
        Assert.Equal(4, AgentCatalog.All.Count);
        Assert.Equal(4, AgentCatalog.All.Select(a => a.Kind).Distinct().Count());
    }

    [Fact]
    public void ClaudeAndCodex_SupportUsageLimitAutoResume()
    {
        // A false positive here stops and relaunches a live process - unlike the read-only activity
        // badge/preview, this must stay opt-in per agent until a real usage-limit event format is
        // confirmed for the other CLIs.
        foreach (var agent in AgentCatalog.All)
        {
            Assert.Equal(agent.Kind is AgentKind.ClaudeCode or AgentKind.CodexCli, agent.SupportsUsageLimitAutoResume);
        }
    }

    [Fact]
    public void OnlyClaudeCode_HasResumeEnvironmentVariables()
    {
        foreach (var agent in AgentCatalog.All.Where(a => a.Kind != AgentKind.ClaudeCode))
        {
            Assert.Empty(agent.ResumeEnvironmentVariables);
        }

        Assert.NotEmpty(AgentCatalog.ClaudeCode.ResumeEnvironmentVariables);
    }
}
