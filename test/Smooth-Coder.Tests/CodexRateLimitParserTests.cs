using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class CodexRateLimitParserTests
{
    [Fact]
    public void TryParseSnapshot_RealRolloutShape_ReadsBothWindows()
    {
        const string line = """{"timestamp":"2026-09-06T03:00:00Z","type":"event_msg","payload":{"type":"token_count","info":null,"rate_limits":{"primary":{"used_percent":23.0,"window_minutes":300,"resets_at":1788646938},"secondary":{"used_percent":4.0,"window_minutes":10080,"resets_at":1789233738}}}}""";

        var result = CodexRateLimitParser.TryParseSnapshot(line);

        Assert.NotNull(result);
        Assert.Equal(23, result.Primary!.UsedPercentage);
        Assert.Equal(300, result.Primary.WindowMinutes);
        Assert.Equal(4, result.Secondary!.UsedPercentage);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789233738), result.Secondary.ResetsAt);
    }

    [Fact]
    public void TryParseReachedReset_WhenSecondaryReached_ReturnsItsReset()
    {
        const string line = """{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":40,"window_minutes":300,"resets_at":1788646938},"secondary":{"used_percent":100,"window_minutes":10080,"resets_at":1789233738}}}}""";

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789233738),
            CodexRateLimitParser.TryParseReachedReset(line));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}")]
    public void TryParseSnapshot_UnrelatedOrMalformed_ReturnsNull(string line)
    {
        Assert.Null(CodexRateLimitParser.TryParseSnapshot(line));
    }
}
