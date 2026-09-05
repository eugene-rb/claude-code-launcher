using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Per-agent access to a project's conversation log, behind one interface so
/// <see cref="ViewModels.SessionItemViewModel"/> and <see cref="TranscriptLimitWatcher"/> don't need to
/// know which CLI wrote the file they're reading. Every implementation must be defensive: a transcript
/// file it can't find, open, or parse should degrade to <see langword="null"/> /
/// <see cref="ProjectActivityState.Unknown"/>, never throw - especially for the three non-Claude
/// sources, whose classification logic is a best-effort reading of each CLI's public documentation
/// rather than something verified against real transcripts.</summary>
public interface IAgentTranscriptSource
{
    /// <summary>Finds the most recently touched transcript for <paramref name="workingDirectory"/>,
    /// with no lower bound on when it was last written. Used for projects this launcher didn't itself
    /// start (no known process-start time to anchor "this run's transcript" against); the caller's own
    /// freshness check on the file's last-write time is what keeps a long-stale project from being
    /// misread as active.</summary>
    string? FindMostRecentTranscriptFile(string workingDirectory);

    /// <summary>Finds the transcript for <paramref name="workingDirectory"/> that was touched at or
    /// after <paramref name="notBefore"/> - a best-effort match for "this run's transcript" when the
    /// caller does know when its own launch started.</summary>
    string? FindActiveTranscriptFile(string workingDirectory, DateTimeOffset notBefore);

    /// <summary>Classifies an already-read tail of a transcript as idle or responding, or null if the
    /// tail contains no recognizable turn.</summary>
    ProjectActivityState? Classify(string tailText);

    /// <summary>Extracts a short human-readable preview of the most recent turn(s), or null if none
    /// could be found.</summary>
    string? ExtractPreview(string tailText);

    /// <summary>Parses one transcript line for a usage-limit event, returning its reset time. Always
    /// null for agents with <see cref="SupportsUsageLimitAutoResume"/> false.</summary>
    DateTimeOffset? TryParseUsageLimitEvent(string jsonlLine);

    /// <summary>Whether usage-limit auto-resume (which stops and relaunches a running process) is safe
    /// to enable for this agent. False for every agent but Claude Code, since a false positive here is
    /// destructive - unlike the read-only activity badge/preview, which degrade harmlessly. Claude
    /// Code and Codex currently expose verified formats.</summary>
    bool SupportsUsageLimitAutoResume { get; }
}
