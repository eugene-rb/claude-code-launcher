using SmoothCoder.App.Models;
using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

/// <summary>Exercises the best-effort classify/preview heuristics shared by the non-Claude
/// <see cref="IAgentTranscriptSource"/> implementations, through <see cref="CodexTranscriptSource"/>'s
/// public <see cref="IAgentTranscriptSource.Classify"/>/<see cref="IAgentTranscriptSource.ExtractPreview"/>
/// (any of the three non-Claude sources would do - they all delegate to the same internal helper).</summary>
public class GenericChatJsonlHeuristicsTests
{
    private static readonly IAgentTranscriptSource Source = new CodexTranscriptSource();

    [Fact]
    public void Classify_LastLineIsAssistantTextOnly_IsIdle()
    {
        var text = """{"role":"user","content":"hi"}""" + "\n"
            + """{"role":"assistant","content":[{"type":"text","text":"hello"}]}""";

        Assert.Equal(ProjectActivityState.Idle, Source.Classify(text));
    }

    [Fact]
    public void Classify_LastLineIsUserMessage_IsResponding()
    {
        var text = """{"role":"assistant","content":[{"type":"text","text":"hello"}]}""" + "\n"
            + """{"role":"user","content":"do something"}""";

        Assert.Equal(ProjectActivityState.Responding, Source.Classify(text));
    }

    [Fact]
    public void Classify_LastLineIsToolCall_IsResponding()
    {
        var text = """{"role":"assistant","content":[{"type":"text","text":"hello"}]}""" + "\n"
            + """{"type":"function_call","name":"run_shell"}""";

        Assert.Equal(ProjectActivityState.Responding, Source.Classify(text));
    }

    [Fact]
    public void Classify_UnparseableLines_AreSkippedNotThrown()
    {
        var text = "not json at all\n{also not json";

        Assert.Null(Source.Classify(text));
    }

    [Fact]
    public void Classify_EmptyText_ReturnsNull()
    {
        Assert.Null(Source.Classify(""));
    }

    [Fact]
    public void ExtractPreview_ReturnsLastUserAndAssistantTurns()
    {
        var text = """{"role":"user","content":"what's the plan"}""" + "\n"
            + """{"role":"assistant","content":[{"type":"text","text":"here's the plan"}]}""";

        var preview = Source.ExtractPreview(text);

        Assert.Contains("what's the plan", preview);
        Assert.Contains("here's the plan", preview);
    }

    [Fact]
    public void ExtractPreview_NoRecognizableTurns_ReturnsNull()
    {
        Assert.Null(Source.ExtractPreview("garbage\nmore garbage"));
    }

    [Fact]
    public void CodexResponseItemPayload_IsClassifiedAndShownInPreview()
    {
        var text = """{"type":"response_item","payload":{"type":"message","role":"user","content":[{"type":"input_text","text":"continue the task"}]}}""" + "\n"
            + """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"done"}]}}""";

        Assert.Equal(ProjectActivityState.Idle, Source.Classify(text));
        var preview = Source.ExtractPreview(text);
        Assert.Contains("continue the task", preview);
        Assert.Contains("done", preview);
    }
}
