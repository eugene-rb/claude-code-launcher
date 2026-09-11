using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class ClaudeAccountUsageTrackerTests
{
    // Plain string interpolation (not a raw-string $$"""...""") because the JSON's three trailing
    // closing braces would otherwise collide with the $$-literal's own "}}" interpolation terminator.
    private static string TokenLine(string timestamp, long tokens) =>
        $"{{\"type\":\"assistant\",\"timestamp\":\"{timestamp}\",\"message\":{{\"usage\":{{\"input_tokens\":{tokens},\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0,\"output_tokens\":0}}}}}}";

    private static string RateLimitLine(string timestamp, string text) =>
        $$"""{"type":"assistant","timestamp":"{{timestamp}}","message":{"content":[{"type":"text","text":"{{text}}"}]},"error":"rate_limit","isApiErrorMessage":true,"apiErrorStatus":429}""";

    private static string SnapshotJson(string capturedAt, string? fiveHour = null, string? sevenDay = null)
    {
        var windows = new List<string>();
        if (fiveHour is not null) windows.Add($"\"fiveHour\":{fiveHour}");
        if (sevenDay is not null) windows.Add($"\"sevenDay\":{sevenDay}");
        var body = windows.Count > 0 ? "," + string.Join(",", windows) : string.Empty;
        return $"{{\"capturedAt\":\"{capturedAt}\"{body}}}";
    }

    private static string Window(double usedPercentage, string resetsAt) =>
        $"{{\"usedPercentage\":{usedPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"resetsAt\":\"{resetsAt}\"}}";

    private sealed class TempRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SmoothCoderTests_" + Guid.NewGuid().ToString("N"));
        public string CalibrationFile { get; }
        public string SnapshotFile { get; }

        public TempRoot()
        {
            Directory.CreateDirectory(Path);
            CalibrationFile = System.IO.Path.Combine(Path, "usage-calibration.json");
            SnapshotFile = System.IO.Path.Combine(Path, "usage-snapshot.json");
        }

        public string NewProjectDir()
        {
            var dir = System.IO.Path.Combine(Path, "project-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        public void WriteSnapshot(string json) => File.WriteAllText(SnapshotFile, json);

        public ClaudeAccountUsageTracker NewTracker() =>
            new(new UsageCalibrationStore(CalibrationFile), Path, new UsageSnapshotStore(SnapshotFile));

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void Poll_NoProjectsDirectory_DoesNotThrow()
    {
        var missingRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SmoothCoderTests_missing_" + Guid.NewGuid().ToString("N"));
        var tracker = new ClaudeAccountUsageTracker(
            new UsageCalibrationStore(System.IO.Path.Combine(missingRoot, "calib.json")),
            missingRoot,
            new UsageSnapshotStore(System.IO.Path.Combine(missingRoot, "snapshot.json")));

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

    [Fact]
    public void Poll_FreshSnapshotReading_FitsBaselineToRealPercentAndRecordsWindow()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
        var resetsAt = now.AddHours(3); // block started 2h ago (5h window)

        File.WriteAllLines(System.IO.Path.Combine(projectDir, "a.jsonl"),
        [
            TokenLine(now.AddHours(-1).ToString("O"), 250),
            TokenLine(now.AddMinutes(-5).ToString("O"), 250),
        ]);
        root.WriteSnapshot(SnapshotJson(now.ToString("O"), fiveHour: Window(25.0, resetsAt.ToString("O"))));

        var tracker = root.NewTracker();
        tracker.Poll(now);

        // 500 tokens in the block == 25% -> baseline 2000.
        Assert.Equal(2000, tracker.SessionWindowBaselineTokens);
        Assert.Equal(25.0, tracker.SessionWindowLastRealPercent);
        Assert.Equal(resetsAt, tracker.SessionWindowResetsAt);
        Assert.Null(tracker.WeeklyWindowBaselineTokens);
    }

    [Fact]
    public void Poll_SnapshotWindowAlreadyReset_IsIgnoredAndNeverPersistsBaseline()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        File.WriteAllText(System.IO.Path.Combine(projectDir, "a.jsonl"), TokenLine(now.AddMinutes(-5).ToString("O"), 400) + "\n");
        root.WriteSnapshot(SnapshotJson(now.AddHours(-2).ToString("O"), fiveHour: Window(90.0, now.AddMinutes(-1).ToString("O"))));

        var tracker = root.NewTracker();
        tracker.Poll(now);

        Assert.Null(tracker.SessionWindowBaselineTokens);
        Assert.Null(tracker.SessionWindowResetsAt);
        Assert.Null(tracker.SessionWindowLastRealPercent);
        Assert.False(File.Exists(root.CalibrationFile));
    }

    [Fact]
    public void Poll_SnapshotPercentBelowFloor_RecordsReadingButDoesNotFitBaseline()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        File.WriteAllText(System.IO.Path.Combine(projectDir, "a.jsonl"), TokenLine(now.AddMinutes(-5).ToString("O"), 100) + "\n");
        root.WriteSnapshot(SnapshotJson(now.ToString("O"), fiveHour: Window(5.0, now.AddHours(4).ToString("O"))));

        var tracker = root.NewTracker();
        tracker.Poll(now);

        Assert.Null(tracker.SessionWindowBaselineTokens);
        Assert.Equal(5.0, tracker.SessionWindowLastRealPercent);
    }

    [Fact]
    public void GetWindowTotalSince_CutsAtBlockStart()
    {
        using var root = new TempRoot();
        var projectDir = root.NewProjectDir();
        var now = new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

        File.WriteAllLines(System.IO.Path.Combine(projectDir, "a.jsonl"),
        [
            TokenLine(now.AddHours(-4).ToString("O"), 111), // before block start
            TokenLine(now.AddHours(-1).ToString("O"), 222), // inside block
        ]);

        var tracker = root.NewTracker();
        tracker.Poll(now);

        Assert.Equal(222, tracker.GetWindowTotalSince(now.AddHours(-2)));
    }
}
