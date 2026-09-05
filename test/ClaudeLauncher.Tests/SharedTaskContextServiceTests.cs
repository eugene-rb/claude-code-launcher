using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class SharedTaskContextServiceTests
{
    [Fact]
    public void ExtractMessages_UnderstandsClaudeAndCodexShapes()
    {
        const string text = """
            {"type":"user","message":{"role":"user","content":[{"type":"text","text":"fix the bug"}]}}
            {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"working on it"}]}}
            {"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"tests pass"}]}}
            """;

        var result = SharedTaskContextService.ExtractMessages(text);

        Assert.Equal(3, result.Count);
        Assert.Equal("User", result[0].Role);
        Assert.Equal("fix the bug", result[0].Text);
        Assert.Equal("tests pass", result[2].Text);
    }

    [Theory]
    [InlineData(AgentKind.ClaudeCode, AgentKind.CodexCli)]
    [InlineData(AgentKind.CodexCli, AgentKind.ClaudeCode)]
    public void GetCounterpart_MapsClaudeAndCodexBothWays(AgentKind source, AgentKind target)
    {
        Assert.Equal(target, SharedTaskContextService.GetCounterpart(source));
    }

    [Fact]
    public void ExtractMessages_LongConversation_PreservesOriginalRequestAndRecentTurns()
    {
        var lines = new List<string>
        {
            """{"role":"user","content":"original task"}""",
        };
        for (var i = 0; i < 24; i++)
        {
            lines.Add($$"""{"role":"assistant","content":"update {{i}}"}""");
        }

        var result = SharedTaskContextService.ExtractMessages(string.Join('\n', lines));

        Assert.Equal(16, result.Count);
        Assert.Equal("original task", result[0].Text);
        Assert.Equal("update 23", result[^1].Text);
    }
}
