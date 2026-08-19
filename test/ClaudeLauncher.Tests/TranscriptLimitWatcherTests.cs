using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class TranscriptLimitWatcherTests
{
    private const string WorkingDirectory = @"D:\Fake\Project";

    private static string RateLimitLine(string timestamp, string text) =>
        $$"""{"type":"assistant","timestamp":"{{timestamp}}","message":{"content":[{"type":"text","text":"{{text}}"}]},"error":"rate_limit","isApiErrorMessage":true,"apiErrorStatus":429}""";

    [Fact]
    public void Poll_NoProjectDirectory_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var watcher = new TranscriptLimitWatcher(root);
        watcher.Reset(DateTimeOffset.Now);

        Assert.Null(watcher.Poll(WorkingDirectory));
    }

    [Fact]
    public void Poll_NewRateLimitLine_ReturnsParsedResetTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, ClaudeProjectPathResolver.ToProjectDirName(WorkingDirectory));
        Directory.CreateDirectory(projectDir);
        try
        {
            var transcript = Path.Combine(projectDir, "session.jsonl");
            var startedAt = DateTimeOffset.Now.AddMinutes(-1);
            File.WriteAllText(transcript, RateLimitLine("2026-08-19T15:42:19.927Z", "You've hit your session limit \\u00b7 resets 3:30am (Asia/Tokyo)") + "\n");

            var watcher = new TranscriptLimitWatcher(root);
            watcher.Reset(startedAt);

            var result = watcher.Poll(WorkingDirectory);

            Assert.Equal(new DateTimeOffset(2026, 8, 20, 3, 30, 0, TimeSpan.FromHours(9)), result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Poll_SameLineNotReprocessed_ReturnsNullOnSecondPoll()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, ClaudeProjectPathResolver.ToProjectDirName(WorkingDirectory));
        Directory.CreateDirectory(projectDir);
        try
        {
            var transcript = Path.Combine(projectDir, "session.jsonl");
            var startedAt = DateTimeOffset.Now.AddMinutes(-1);
            File.WriteAllText(transcript, RateLimitLine("2026-08-19T15:42:19.927Z", "You've hit your session limit \\u00b7 resets 3:30am (Asia/Tokyo)") + "\n");

            var watcher = new TranscriptLimitWatcher(root);
            watcher.Reset(startedAt);

            Assert.NotNull(watcher.Poll(WorkingDirectory));
            Assert.Null(watcher.Poll(WorkingDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Poll_TrailingUnterminatedLine_IsLeftForNextPoll()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, ClaudeProjectPathResolver.ToProjectDirName(WorkingDirectory));
        Directory.CreateDirectory(projectDir);
        try
        {
            var transcript = Path.Combine(projectDir, "session.jsonl");
            var startedAt = DateTimeOffset.Now.AddMinutes(-1);
            var line = RateLimitLine("2026-08-19T15:42:19.927Z", "You've hit your session limit \\u00b7 resets 3:30am (Asia/Tokyo)");

            // No trailing newline yet - the line is "still being written".
            File.WriteAllText(transcript, line);

            var watcher = new TranscriptLimitWatcher(root);
            watcher.Reset(startedAt);

            Assert.Null(watcher.Poll(WorkingDirectory));

            // The writer finishes the line.
            File.AppendAllText(transcript, "\n");

            Assert.NotNull(watcher.Poll(WorkingDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PickActiveTranscriptFile_PicksMostRecentFileAtOrAfterStart()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var older = Path.Combine(root, "old.jsonl");
            var newer = Path.Combine(root, "new.jsonl");
            File.WriteAllText(older, "{}");
            File.WriteAllText(newer, "{}");

            var startedAt = DateTimeOffset.UtcNow;
            File.SetLastWriteTimeUtc(older, startedAt.UtcDateTime.AddMinutes(-10));
            File.SetLastWriteTimeUtc(newer, startedAt.UtcDateTime.AddSeconds(1));

            var result = TranscriptLimitWatcher.PickActiveTranscriptFile(root, startedAt);

            Assert.Equal(newer, result);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
