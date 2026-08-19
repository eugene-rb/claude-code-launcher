using System.IO;
using System.Text;

namespace ClaudeLauncher.App.Services;

/// <summary>Tails a running session's Claude Code transcript (~/.claude/projects/&lt;hash of cwd&gt;/*.jsonl)
/// for a "rate_limit" event, one instance per running <see cref="ViewModels.SessionItemViewModel"/>.
/// Tracks its own read offset so a poll only scans lines appended since the previous call — a matched
/// line is therefore only ever seen once, and an unterminated trailing line is left for the next poll
/// rather than mis-parsed.</summary>
public sealed class TranscriptLimitWatcher(string? projectsRootOverride = null)
{
    private readonly string _projectsRoot = projectsRootOverride ?? ClaudeProjectPathResolver.GetProjectsRoot();
    private string? _watchedFilePath;
    private long _offset;
    private DateTimeOffset _processStartedAt;

    /// <summary>Call when the session (re)starts, before the first <see cref="Poll"/>.</summary>
    public void Reset(DateTimeOffset processStartedAt)
    {
        _watchedFilePath = null;
        _offset = 0;
        _processStartedAt = processStartedAt;
    }

    /// <summary>Returns the reset time of the most recent newly-seen "rate_limit" event, or null if
    /// none was found since the last call (including when the transcript directory/file can't be
    /// located yet — e.g. the CLI hasn't written anything for this run).</summary>
    public DateTimeOffset? Poll(string workingDirectory)
    {
        var projectDir = Path.Combine(_projectsRoot, ClaudeProjectPathResolver.ToProjectDirName(workingDirectory));
        var file = PickActiveTranscriptFile(projectDir, _processStartedAt);
        if (file is null)
        {
            return null;
        }

        if (file != _watchedFilePath)
        {
            _watchedFilePath = file;
            _offset = 0;
        }

        return ReadNewLines(file);
    }

    private DateTimeOffset? ReadNewLines(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length <= _offset)
        {
            return null;
        }

        fs.Seek(_offset, SeekOrigin.Begin);
        var buffer = new byte[fs.Length - _offset];
        var read = fs.Read(buffer, 0, buffer.Length);
        var text = Encoding.UTF8.GetString(buffer, 0, read);

        var lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            // No complete line yet; leave the offset where it was and retry next poll.
            return null;
        }

        var completeChunk = text[..lastNewline];
        _offset += Encoding.UTF8.GetByteCount(completeChunk) + 1;

        DateTimeOffset? found = null;
        foreach (var line in completeChunk.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parsed = UsageLimitEventParser.TryParseLine(line);
            if (parsed is not null)
            {
                found = parsed;
            }
        }

        return found;
    }

    /// <summary>Picks the most recently modified *.jsonl in the project directory that was touched at
    /// or after the session started — a best-effort match for "this run's transcript" that tolerates
    /// clock-granularity skew via a small tolerance.</summary>
    public static string? PickActiveTranscriptFile(string projectDir, DateTimeOffset notBefore)
    {
        if (!Directory.Exists(projectDir))
        {
            return null;
        }

        var cutoffUtc = notBefore.UtcDateTime.AddSeconds(-5);

        return Directory.EnumerateFiles(projectDir, "*.jsonl")
            .Select(p => new FileInfo(p))
            .Where(fi => fi.LastWriteTimeUtc >= cutoffUtc)
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }
}
