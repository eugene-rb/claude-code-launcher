using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class TranscriptActivityClassifierTests
{
    private const string AssistantTextLine = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"done"}]}}""";
    private const string AssistantThinkingLine = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"thinking","thinking":"..."}]}}""";
    private const string AssistantToolUseLine = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","name":"Bash"}]}}""";
    private const string UserTextLine = """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"hello"}]}}""";
    private const string UserToolResultLine = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"1"}]}}""";
    private const string BookkeepingLine = """{"type":"attachment"}""";

    private static string WriteTempFile(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        return path;
    }

    [Fact]
    public void ClassifyLastTurn_AssistantTextOnly_IsIdle()
    {
        var file = WriteTempFile(AssistantTextLine);
        try
        {
            Assert.Equal(ProjectActivityState.Idle, TranscriptActivityClassifier.ClassifyLastTurn(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ClassifyLastTurn_AssistantToolUse_IsResponding()
    {
        var file = WriteTempFile(AssistantToolUseLine);
        try
        {
            Assert.Equal(ProjectActivityState.Responding, TranscriptActivityClassifier.ClassifyLastTurn(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ClassifyLastTurn_AssistantThinkingOnly_IsResponding()
    {
        // Generation hasn't reached its final text/tool_use yet - still mid-turn, not idle.
        var file = WriteTempFile(AssistantThinkingLine);
        try
        {
            Assert.Equal(ProjectActivityState.Responding, TranscriptActivityClassifier.ClassifyLastTurn(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ClassifyLastTurn_UserToolResult_IsResponding()
    {
        var file = WriteTempFile(UserToolResultLine);
        try
        {
            Assert.Equal(ProjectActivityState.Responding, TranscriptActivityClassifier.ClassifyLastTurn(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ClassifyLastTurn_UserText_IsResponding()
    {
        var file = WriteTempFile(UserTextLine);
        try
        {
            Assert.Equal(ProjectActivityState.Responding, TranscriptActivityClassifier.ClassifyLastTurn(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ClassifyLastTurn_TrailingBookkeepingLines_AreIgnored()
    {
        var file = WriteTempFile(AssistantTextLine, BookkeepingLine, BookkeepingLine);
        try
        {
            Assert.Equal(ProjectActivityState.Idle, TranscriptActivityClassifier.ClassifyLastTurn(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ClassifyLastTurn_EmptyFile_ReturnsNull()
    {
        var file = WriteTempFile();
        File.WriteAllText(file, string.Empty);
        try
        {
            Assert.Null(TranscriptActivityClassifier.ClassifyLastTurn(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ClassifyLastTurn_MissingFile_ReturnsNull()
    {
        var file = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_missing_" + Guid.NewGuid().ToString("N") + ".jsonl");

        Assert.Null(TranscriptActivityClassifier.ClassifyLastTurn(file));
    }
}
