using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class TranscriptPreviewReaderTests
{
    private const string AssistantTextLine = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"done"}]}}""";
    private const string AssistantToolUseLine = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","name":"Bash"}]}}""";
    private const string AssistantThinkingLine = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"thinking","thinking":"..."}]}}""";
    private const string UserTextLine = """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"hello there"}]}}""";
    private const string UserToolResultLine = """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"1"}]}}""";
    private const string BookkeepingLine = """{"type":"attachment"}""";

    private static string WriteTempFile(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
        return path;
    }

    [Fact]
    public void ReadPreview_UserThenAssistantText_ShowsBoth()
    {
        var file = WriteTempFile(UserTextLine, AssistantTextLine);
        try
        {
            var preview = TranscriptPreviewReader.ReadPreview(file);

            Assert.Equal("あなた: hello there" + Environment.NewLine + "Claude: done", preview);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReadPreview_AssistantToolUse_ShowsToolName()
    {
        var file = WriteTempFile(UserTextLine, AssistantToolUseLine);
        try
        {
            var preview = TranscriptPreviewReader.ReadPreview(file);

            Assert.Contains("Bash", preview);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReadPreview_AssistantThinkingOnly_ShowsThinkingPlaceholder()
    {
        var file = WriteTempFile(AssistantThinkingLine);
        try
        {
            var preview = TranscriptPreviewReader.ReadPreview(file);

            Assert.Contains("考え中", preview);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReadPreview_ToolResultLine_DoesNotOverwriteLastRealUserMessage()
    {
        var file = WriteTempFile(UserTextLine, AssistantToolUseLine, UserToolResultLine);
        try
        {
            var preview = TranscriptPreviewReader.ReadPreview(file);

            Assert.Contains("hello there", preview);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReadPreview_TrailingBookkeepingLines_AreIgnored()
    {
        var file = WriteTempFile(AssistantTextLine, BookkeepingLine, BookkeepingLine);
        try
        {
            var preview = TranscriptPreviewReader.ReadPreview(file);

            Assert.Equal("Claude: done", preview);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReadPreview_EmptyFile_ReturnsNull()
    {
        var file = WriteTempFile();
        File.WriteAllText(file, string.Empty);
        try
        {
            Assert.Null(TranscriptPreviewReader.ReadPreview(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ReadPreview_MissingFile_ReturnsNull()
    {
        var file = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_missing_" + Guid.NewGuid().ToString("N") + ".jsonl");

        Assert.Null(TranscriptPreviewReader.ReadPreview(file));
    }

    [Fact]
    public void ReadPreview_LongText_IsTruncated()
    {
        var longText = new string('a', 300);
        var line = $$$"""{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"{{{longText}}}"}]}}""";
        var file = WriteTempFile(line);
        try
        {
            var preview = TranscriptPreviewReader.ReadPreview(file);

            Assert.NotNull(preview);
            Assert.True(preview.Length < longText.Length);
            Assert.EndsWith("…", preview);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
