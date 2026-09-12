using SmoothCoder.App.Models;
using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class SharedTaskContextServiceTests
{
    [Fact]
    public void Capture_NoMatchingTranscriptAndNoPriorCheckpoint_HasNoContext()
    {
        var checkpointRoot = Path.Combine(Path.GetTempPath(), "SmoothCoderTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new SharedTaskContextService(checkpointRoot);
            var workingDirectory = Path.Combine(Path.GetTempPath(), "SmoothCoderTests_NoSuchProject_" + Guid.NewGuid().ToString("N"));

            var result = service.Capture(workingDirectory, AgentKind.ClaudeCode);

            Assert.False(result.HasContext);
            Assert.Contains("元の依頼を特定できませんでした", File.ReadAllText(result.Path));
        }
        finally
        {
            Directory.Delete(checkpointRoot, recursive: true);
        }
    }

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

    [Fact]
    public void ExtractOriginalTask_ReadsTheSectionBackOutOfACheckpoint()
    {
        var checkpoint = $"""
            # Shared task checkpoint

            - Captured: 2026-09-08T12:00:00.0000000+09:00

            {SharedTaskContextService.OriginalTaskHeading}

            ランチャーに双方向の引き継ぎを実装する。
            音声通知も共通機能にする。

            ## Recent conversation

            ### User

            共有チェックポイントを読んで続行してください。
            """;

        Assert.Equal(
            "ランチャーに双方向の引き継ぎを実装する。\n音声通知も共通機能にする。",
            SharedTaskContextService.ExtractOriginalTask(checkpoint)?.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ExtractOriginalTask_CheckpointWithoutTheSection_ReturnsNull()
    {
        // Checkpoints written before the section existed, and anything else unparseable.
        Assert.Null(SharedTaskContextService.ExtractOriginalTask("# Shared task checkpoint\n\n## Recent conversation\n"));
        Assert.Null(SharedTaskContextService.ExtractOriginalTask(string.Empty));
    }

    [Fact]
    public void ExtractOriginalTask_EmptySection_ReturnsNull()
    {
        var checkpoint = $"{SharedTaskContextService.OriginalTaskHeading}\n\n\n## Recent conversation\n";

        Assert.Null(SharedTaskContextService.ExtractOriginalTask(checkpoint));
    }

    /// <summary>The circular-reference case this section exists for: after the first handoff, the
    /// transcript's earliest user message is the continuation prompt pointing back at the checkpoint,
    /// so re-deriving the task from the transcript loses the actual request. Carrying the section
    /// through keeps it intact across any number of handoffs.</summary>
    [Fact]
    public void ExtractOriginalTask_SurvivesRepeatedCaptures()
    {
        var task = "巨大なリファクタリングを最後まで完了させる。";
        var checkpoint = $"{SharedTaskContextService.OriginalTaskHeading}\n\n{task}\n\n## Recent conversation\n";

        for (var handoff = 0; handoff < 5; handoff++)
        {
            var carried = SharedTaskContextService.ExtractOriginalTask(checkpoint);
            Assert.Equal(task, carried);

            // What the next capture writes: the carried task, plus a conversation whose first user
            // message is only the "go read the checkpoint" prompt.
            checkpoint = $"""
                {SharedTaskContextService.OriginalTaskHeading}

                {carried}

                ## Recent conversation

                ### User

                {SharedTaskContextService.BuildContinuationPrompt("C:\\checkpoint.md", AgentKind.ClaudeCode)}
                """;
        }
    }
}
