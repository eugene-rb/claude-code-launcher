using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class CodexTranscriptSourceTests
{
    [Fact]
    public void FindMostRecentTranscriptFile_NoSessionsRoot_ReturnsNullWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var source = new CodexTranscriptSource(root);

        Assert.Null(source.FindMostRecentTranscriptFile(@"D:\Fake\Project"));
    }

    [Fact]
    public void FindMostRecentTranscriptFile_MatchingCwdInRolloutFile_IsFound()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var dayDir = Path.Combine(root, "2026", "08", "23");
        Directory.CreateDirectory(dayDir);
        try
        {
            var workingDirectory = Path.Combine(root, "project");
            var file = Path.Combine(dayDir, "rollout-2026-08-23T00-00-00-abc.jsonl");
            var cwdJson = workingDirectory.Replace("\\", "\\\\");
            File.WriteAllText(file, "{\"type\":\"session_meta\",\"payload\":{\"cwd\":\"" + cwdJson + "\"}}\n");

            var source = new CodexTranscriptSource(root);

            Assert.Equal(file, source.FindMostRecentTranscriptFile(workingDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindMostRecentTranscriptFile_NoCwdMatch_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var dayDir = Path.Combine(root, "2026", "08", "23");
        Directory.CreateDirectory(dayDir);
        try
        {
            File.WriteAllText(Path.Combine(dayDir, "rollout-1.jsonl"), """{"cwd":"D:\\Other\\Project"}""" + "\n");

            var source = new CodexTranscriptSource(root);

            Assert.Null(source.FindMostRecentTranscriptFile(@"D:\Fake\Project"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SupportsUsageLimitAutoResume_IsTrue()
    {
        Assert.True(new CodexTranscriptSource().SupportsUsageLimitAutoResume);
    }

    [Fact]
    public void TryParseUsageLimitEvent_AtOneHundredPercent_ReturnsResetTime()
    {
        const string line = """{"timestamp":"2026-09-06T03:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":100.0,"window_minutes":300,"resets_at":1788646938},"secondary":null}}}""";

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1788646938),
            new CodexTranscriptSource().TryParseUsageLimitEvent(line));
    }

    [Fact]
    public void TryParseUsageLimitEvent_BelowLimit_ReturnsNull()
    {
        const string line = """{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":99.0,"window_minutes":300,"resets_at":1788646938}}}}""";

        Assert.Null(new CodexTranscriptSource().TryParseUsageLimitEvent(line));
    }

    /// <summary>Shapes taken from a real rollout file (~/.codex/sessions/2026/09/08/rollout-*.jsonl):
    /// one task_complete event_msg per turn, right after the final assistant message. This is Codex's
    /// answer to Claude Code's Stop hook and the reason the end-of-turn announcement covers both CLIs.</summary>
    private const string TaskCompleteLine =
        """{"timestamp":"2026-09-08T11:29:16.371Z","ordinal":452,"type":"event_msg","payload":{"type":"task_complete","turn_id":"01a080bb-f275-7191-b29b-b1be874e1224","last_agent_message":"done"}}""";

    private const string EarlierTaskCompleteLine =
        """{"timestamp":"2026-09-08T10:02:03.000Z","ordinal":120,"type":"event_msg","payload":{"type":"task_complete","turn_id":"01a08000-0000-0000-0000-000000000000","last_agent_message":"earlier"}}""";

    private const string TokenCountLine =
        """{"timestamp":"2026-09-08T11:29:16.351Z","ordinal":451,"type":"event_msg","payload":{"type":"token_count","info":{}}}""";

    [Fact]
    public void TryDetectTurnComplete_ReturnsTheRecordsOwnTimestamp()
    {
        var tail = string.Join(Environment.NewLine, TokenCountLine, TaskCompleteLine);

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-08T11:29:16.371Z", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            new CodexTranscriptSource().TryDetectTurnComplete(tail));
    }

    [Fact]
    public void TryDetectTurnComplete_MultipleTurnsInTheTail_ReturnsTheNewest()
    {
        // Order matters: the caller announces on a forward move, so returning an older turn from a
        // tail that also contains a newer one would go silent for the rest of the session.
        var tail = string.Join(Environment.NewLine, TaskCompleteLine, EarlierTaskCompleteLine);

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-08T11:29:16.371Z", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            new CodexTranscriptSource().TryDetectTurnComplete(tail));
    }

    [Fact]
    public void TryDetectTurnComplete_TailWithoutOne_ReturnsNull()
    {
        Assert.Null(new CodexTranscriptSource().TryDetectTurnComplete(TokenCountLine));
    }

    [Fact]
    public void TryDetectTurnComplete_TruncatedFirstLine_IsSkippedNotThrown()
    {
        // A tail read starts at an arbitrary byte offset, so its first line is routinely half a record
        // - and half a task_complete record still contains the string being pre-filtered on.
        var tail = string.Join(Environment.NewLine, """sg","payload":{"type":"task_complete","turn_id":"x"}}""", TaskCompleteLine);

        Assert.Equal(
            DateTimeOffset.Parse("2026-09-08T11:29:16.371Z", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind),
            new CodexTranscriptSource().TryDetectTurnComplete(tail));
    }
}
