using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class ClaudeAccountUsageTrackerTests
{
    // Plain string interpolation (not a raw-string $$"""...""") because the JSON's three trailing
    // closing braces would otherwise collide with the $$-literal's own "}}" interpolation terminator.
    private static string TokenLine(string timestamp, long tokens) =>
        $"{{\"type\":\"assistant\",\"timestamp\":\"{timestamp}\",\"message\":{{\"usage\":{{\"input_tokens\":{tokens},\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0,\"output_tokens\":0}}}}}}";

    private static string RateLimitLine(string timestamp, string text) =>
        $$"""{"type":"assistant","timestamp":"{{timestamp}}","message":{"content":[{"type":"text","text":"{{text}}"}]},"error":"rate_limit","isApiErrorMessage":true,"apiErrorStatus":429}""";

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        public string CalibrationFile { get; }

        public TempRoot()
        {
            Directory.CreateDirectory(Path);
            CalibrationFile = System.IO.Path.Combine(Path, "usage-calibration.json");
        }

        public string NewProjectDir()
        {
            var dir = System.IO.Path.Combine(Path, "project-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public ClaudeAccountUsageTracker NewTracker() =>
            new(new UsageCalibrationStore(CalibrationFile), Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void Poll_NoProjectsDirectory_DoesNotThrow()
    {
        var missingRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClaudeLauncherTests_missing_" + Guid.NewGuid().ToString("N"));
        var tracker = new ClaudeAccountUsageTracker(new UsageCalibrationStore(System.IO.Path.Combine(missingRoot, "calib.json")), missingRoot);

        tracker.Poll(DateTimeOffset.Now);

        Assert.Equal(0, tracker.GetWindowTotal(ClaudeAccountUsageTracker.SessionWindow, DateTimeOffset.Now));
    }

    [Fact]
    public void Poll_SingleFile_SumsTokensWithinWindow()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        File.WriteAllLines(System.IO.Path.Combine(projectDir, "a.jsonl"),
        [
            TokenLine(now.AddHours(-1).ToString("O"), 100),
            TokenLine(now.AddHours(-2).ToString("O"), 200),
        ]);

        var tracker = root.NewTracker();
        tracker.Poll(now);

        Assert.Equal(300, tracker.GetWindowTotal(ClaudeAccountUsageTracker.SessionWindow, now));
    }

    [Fact]
    public void Poll_MultipleProjectDirectories_SumsAcrossAll()
    {
        using var root = new TempRoot();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        File.WriteAllText(System.IO.Path.Combine(root.NewProjectDir(), "a.jsonl"), TokenLine(now.AddMinutes(-30).ToString("O"), 50) + "\n");
        File.WriteAllText(System.IO.Path.Combine(root.NewProjectDir(), "b.jsonl"), TokenLine(now.AddMinutes(-10).ToString("O"), 75) + "\n");

        var tracker = root.NewTracker();
        tracker.Poll(now);

        Assert.Equal(125, tracker.GetWindowTotal(ClaudeAccountUsageTracker.SessionWindow, now));
    }

    [Fact]
    public void GetWindowTotal_SampleOlderThanSessionWindowButWithinWeekly_CountsOnlyInWeekly()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        File.WriteAllText(System.IO.Path.Combine(projectDir, "a.jsonl"), TokenLine(now.AddHours(-8).ToString("O"), 999) + "\n");

        var tracker = root.NewTracker();
        tracker.Poll(now);

        Assert.Equal(0, tracker.GetWindowTotal(ClaudeAccountUsageTracker.SessionWindow, now));
        Assert.Equal(999, tracker.GetWindowTotal(ClaudeAccountUsageTracker.WeeklyWindow, now));
    }

    [Fact]
    public void Poll_SampleOlderThanWeeklyWindow_NeverEntersTotals()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var stale = now.AddDays(-10);

        var file = System.IO.Path.Combine(projectDir, "old.jsonl");
        File.WriteAllText(file, TokenLine(stale.ToString("O"), 999) + "\n");
        File.SetLastWriteTimeUtc(file, stale.UtcDateTime);

        var tracker = root.NewTracker();
        tracker.Poll(now);

        Assert.Equal(0, tracker.GetWindowTotal(ClaudeAccountUsageTracker.WeeklyWindow, now));
    }

    [Fact]
    public void Poll_AppendedLinesAfterFirstPoll_AreAddedIncrementallyNotDuplicated()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var file = System.IO.Path.Combine(projectDir, "a.jsonl");

        File.WriteAllText(file, TokenLine(now.AddMinutes(-30).ToString("O"), 100) + "\n");

        var tracker = root.NewTracker();
        tracker.Poll(now);
        Assert.Equal(100, tracker.GetWindowTotal(ClaudeAccountUsageTracker.SessionWindow, now));

        // Second poll with no new bytes appended must not recount the same line.
        tracker.Poll(now);
        Assert.Equal(100, tracker.GetWindowTotal(ClaudeAccountUsageTracker.SessionWindow, now));

        File.AppendAllText(file, TokenLine(now.AddMinutes(-10).ToString("O"), 50) + "\n");
        tracker.Poll(now);
        Assert.Equal(150, tracker.GetWindowTotal(ClaudeAccountUsageTracker.SessionWindow, now));
    }

    [Fact]
    public void Poll_NewRateLimitEvent_CalibratesAndPersists()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 15, 42, 19, TimeSpan.Zero);
        var file = System.IO.Path.Combine(projectDir, "a.jsonl");

        File.WriteAllLines(file,
        [
            TokenLine(now.AddHours(-1).ToString("O"), 100),
            TokenLine(now.AddMinutes(-1).ToString("O"), 50),
            RateLimitLine(now.ToString("O"), "You've hit your session limit \\u00b7 resets 3:30am (Asia/Tokyo)"),
        ]);

        var tracker = root.NewTracker();
        tracker.Poll(now);

        Assert.Equal(150, tracker.SessionWindowBaselineTokens);
        Assert.Null(tracker.WeeklyWindowBaselineTokens);

        // Persisted, so a fresh tracker instance backed by the same file picks it up without polling again.
        var reloaded = new ClaudeAccountUsageTracker(new UsageCalibrationStore(root.CalibrationFile));
        Assert.Equal(150, reloaded.SessionWindowBaselineTokens);
    }

    [Fact]
    public void ComputePercentage_UnmeasuredWindow_IsNullUntilCalibrated()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        File.WriteAllText(System.IO.Path.Combine(projectDir, "a.jsonl"), TokenLine(now.AddMinutes(-5).ToString("O"), 42) + "\n");

        var tracker = root.NewTracker();
        tracker.Poll(now);

        var percentage = UsageWindowEvaluator.ComputePercentage(
            tracker.GetWindowTotal(ClaudeAccountUsageTracker.SessionWindow, now), tracker.SessionWindowBaselineTokens);

        Assert.Null(percentage);
    }
}
