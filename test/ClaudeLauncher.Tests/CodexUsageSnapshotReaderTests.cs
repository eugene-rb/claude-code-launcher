using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class CodexUsageSnapshotReaderTests
{
    [Fact]
    public void ReadLatest_ReturnsNewestSnapshotFromNewestRollout()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var day = Path.Combine(root, "2026", "09", "06");
        Directory.CreateDirectory(day);
        try
        {
            var file = Path.Combine(day, "rollout-test.jsonl");
            File.WriteAllText(file,
                """{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":10,"window_minutes":300,"resets_at":1788646938}}}}""" + "\n"
                + """{"type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":42,"window_minutes":300,"resets_at":1788646938}}}}""" + "\n");

            var result = new CodexUsageSnapshotReader(root).ReadLatest();

            Assert.NotNull(result);
            Assert.Equal(42, result.Primary!.UsedPercentage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
