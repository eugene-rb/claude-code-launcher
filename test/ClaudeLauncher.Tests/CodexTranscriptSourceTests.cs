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
}
