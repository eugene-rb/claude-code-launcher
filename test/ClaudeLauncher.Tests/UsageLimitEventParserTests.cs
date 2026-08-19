using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class UsageLimitEventParserTests
{
    // Real message wording collected from this machine's own ~/.claude/projects transcripts
    // (35 genuine rate_limit events, CLI versions 2.1.207-2.1.235).

    [Fact]
    public void TryParseResetTime_SessionLimit_SameDay_ResolvesTime()
    {
        var eventTimestamp = new DateTimeOffset(2026, 8, 19, 15, 42, 19, TimeSpan.Zero); // 2026-08-20 00:42 JST
        var result = UsageLimitEventParser.TryParseResetTime("You've hit your session limit · resets 3:30am (Asia/Tokyo)", eventTimestamp);

        Assert.Equal(new DateTimeOffset(2026, 8, 20, 3, 30, 0, TimeSpan.FromHours(9)), result);
    }

    [Fact]
    public void TryParseResetTime_WeeklyLimit_TimeAlreadyPassedToday_RollsToTomorrow()
    {
        // 2026-08-06T10:53:43Z is 19:53 JST; "resets 12pm" (noon) must mean the next day.
        var eventTimestamp = new DateTimeOffset(2026, 8, 6, 10, 53, 43, TimeSpan.Zero);
        var result = UsageLimitEventParser.TryParseResetTime("You've hit your weekly limit · resets 12pm (Asia/Tokyo)", eventTimestamp);

        Assert.Equal(new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.FromHours(9)), result);
    }

    [Fact]
    public void TryParseResetTime_ExplicitDate_UsesGivenMonthAndDay()
    {
        var eventTimestamp = new DateTimeOffset(2026, 7, 21, 13, 43, 38, TimeSpan.Zero); // 2026-07-21 22:43 JST
        var result = UsageLimitEventParser.TryParseResetTime("You've hit your weekly limit · resets Jul 24, 12pm (Asia/Tokyo)", eventTimestamp);

        Assert.Equal(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(9)), result);
    }

    [Fact]
    public void TryParseResetTime_ExplicitDate_YearBoundary_RollsToNextYear()
    {
        // Event in late December; explicit date "Jan 3" must mean next year, not the same year
        // (which would be before the event).
        var eventTimestamp = new DateTimeOffset(2026, 12, 30, 3, 0, 0, TimeSpan.Zero); // 2026-12-30 12:00 JST
        var result = UsageLimitEventParser.TryParseResetTime("You've hit your weekly limit · resets Jan 3, 9am (Asia/Tokyo)", eventTimestamp);

        Assert.Equal(new DateTimeOffset(2027, 1, 3, 9, 0, 0, TimeSpan.FromHours(9)), result);
    }

    [Fact]
    public void TryParseResetTime_DifferentTimeZone_ResolvesViaIana()
    {
        var eventTimestamp = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero); // 08:00 America/New_York (EDT, UTC-4)
        var result = UsageLimitEventParser.TryParseResetTime("You've hit your session limit · resets 2pm (America/New_York)", eventTimestamp);

        Assert.Equal(new DateTimeOffset(2026, 8, 19, 14, 0, 0, TimeSpan.FromHours(-4)), result);
    }

    [Fact]
    public void TryParseResetTime_NoTimeZone_FallsBackToLocal()
    {
        var eventTimestamp = DateTimeOffset.UtcNow;
        var result = UsageLimitEventParser.TryParseResetTime("You've hit your session limit · resets 3pm", eventTimestamp);

        Assert.NotNull(result);
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(result!.Value), result.Value.Offset);
        Assert.Equal(15, result.Value.Hour);
    }

    [Fact]
    public void TryParseResetTime_UnknownTimeZone_FallsBackToLocalWithoutThrowing()
    {
        var eventTimestamp = DateTimeOffset.UtcNow;
        var result = UsageLimitEventParser.TryParseResetTime("You've hit your session limit · resets 5am (Not/AZone)", eventTimestamp);

        Assert.NotNull(result);
        Assert.Equal(5, result!.Value.Hour);
    }

    [Fact]
    public void TryParseResetTime_UnrelatedText_ReturnsNull()
    {
        var result = UsageLimitEventParser.TryParseResetTime("I'll fix that bug for you now.", DateTimeOffset.UtcNow);

        Assert.Null(result);
    }

    [Fact]
    public void TryParseLine_RealCapturedShape_ExtractsResetTime()
    {
        const string line = """
            {"parentUuid":"ca4a521b-5e1a-412a-b8f4-016d47aa2684","isSidechain":false,"type":"assistant","uuid":"8b441d7b-93f6-43c1-8cab-cadd658dcf78","timestamp":"2026-08-04T15:53:49.229Z","message":{"id":"7dec0fb2-1988-4eb4-8923-2728ca20b47a","role":"assistant","content":[{"type":"text","text":"You've hit your session limit \u00b7 resets 1:50am (Asia/Tokyo)"}]},"requestId":"req_011CdhyhjJBXqstNrqmXk3Wj","error":"rate_limit","isApiErrorMessage":true,"apiErrorStatus":429,"cwd":"C:\\Users\\kind4","sessionId":"c3223a9c-af12-4a08-a4c5-fb2ed317ae1e","version":"2.1.221"}
            """;

        var result = UsageLimitEventParser.TryParseLine(line);

        Assert.Equal(new DateTimeOffset(2026, 8, 5, 1, 50, 0, TimeSpan.FromHours(9)), result);
    }

    [Fact]
    public void TryParseLine_NoRateLimitError_ReturnsNull()
    {
        const string line = """
            {"type":"assistant","timestamp":"2026-08-04T15:53:49.229Z","message":{"content":[{"type":"text","text":"Here is the answer."}]}}
            """;

        Assert.Null(UsageLimitEventParser.TryParseLine(line));
    }

    [Fact]
    public void TryParseLine_MalformedJson_ReturnsNullInsteadOfThrowing()
    {
        Assert.Null(UsageLimitEventParser.TryParseLine("{\"error\":\"rate_limit\", this is not valid json"));
    }

    [Fact]
    public void TryParseLine_RateLimitButUnparsableText_ReturnsNull()
    {
        const string line = """
            {"type":"assistant","timestamp":"2026-08-04T15:53:49.229Z","message":{"content":[{"type":"text","text":"Something went wrong."}]},"error":"rate_limit"}
            """;

        Assert.Null(UsageLimitEventParser.TryParseLine(line));
    }
}
