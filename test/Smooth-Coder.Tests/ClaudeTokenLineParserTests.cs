using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class ClaudeTokenLineParserTests
{
    [Fact]
    public void TryParseLine_RealCapturedShape_SumsAllFourTokenFields()
    {
        const string line = """
            {"type":"assistant","timestamp":"2026-08-19T13:46:29.888Z","cwd":"D:\\Dev\\Project","sessionId":"abc","isSidechain":false,"message":{"role":"assistant","usage":{"input_tokens":10,"cache_creation_input_tokens":52915,"cache_read_input_tokens":100,"output_tokens":1711,"output_tokens_details":{"thinking_tokens":887},"service_tier":"standard"}}}
            """;

        var result = ClaudeTokenLineParser.TryParseLine(line);

        Assert.NotNull(result);
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 13, 46, 29, 888, TimeSpan.Zero), result!.Value.Timestamp);
        Assert.Equal(10 + 52915 + 100 + 1711, result.Value.Tokens);
    }

    [Fact]
    public void TryParseLine_SidechainMessage_IsStillCounted()
    {
        const string line = """
            {"type":"assistant","timestamp":"2026-08-19T13:46:29.888Z","isSidechain":true,"message":{"usage":{"input_tokens":5,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":20}}}
            """;

        var result = ClaudeTokenLineParser.TryParseLine(line);

        Assert.NotNull(result);
        Assert.Equal(25, result!.Value.Tokens);
    }

    [Fact]
    public void TryParseLine_NonAssistantType_ReturnsNull()
    {
        const string line = """
            {"type":"user","timestamp":"2026-08-19T13:46:29.888Z","message":{"content":"hi"}}
            """;

        Assert.Null(ClaudeTokenLineParser.TryParseLine(line));
    }

    [Fact]
    public void TryParseLine_NoUsageField_ReturnsNull()
    {
        const string line = """
            {"type":"assistant","timestamp":"2026-08-19T13:46:29.888Z","message":{"content":[{"type":"text","text":"hi"}]}}
            """;

        Assert.Null(ClaudeTokenLineParser.TryParseLine(line));
    }

    [Fact]
    public void TryParseLine_MissingTokenField_TreatsItAsZero()
    {
        const string line = """
            {"type":"assistant","timestamp":"2026-08-19T13:46:29.888Z","message":{"usage":{"input_tokens":3,"output_tokens":7}}}
            """;

        var result = ClaudeTokenLineParser.TryParseLine(line);

        Assert.NotNull(result);
        Assert.Equal(10, result!.Value.Tokens);
    }

    [Fact]
    public void TryParseLine_MalformedJson_ReturnsNullInsteadOfThrowing()
    {
        Assert.Null(ClaudeTokenLineParser.TryParseLine("{\"type\":\"assistant\",\"usage\": this is not valid json"));
    }
}
