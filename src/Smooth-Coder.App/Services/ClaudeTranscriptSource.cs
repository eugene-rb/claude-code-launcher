using System.IO;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Thin <see cref="IAgentTranscriptSource"/> adapter over the existing, already-verified
/// Claude Code transcript services - <see cref="ClaudeProjectPathResolver"/>,
/// <see cref="TranscriptActivityClassifier"/>, <see cref="TranscriptPreviewReader"/>, and
/// <see cref="UsageLimitEventParser"/>. None of those classes change; this only routes calls to them
/// so Claude Code participates in the same interface as the other agents.</summary>
public sealed class ClaudeTranscriptSource(string? projectsRootOverride = null) : IAgentTranscriptSource
{
    private readonly string _projectsRoot = projectsRootOverride ?? ClaudeProjectPathResolver.GetProjectsRoot();

    public bool SupportsUsageLimitAutoResume => true;

    public string? FindMostRecentTranscriptFile(string workingDirectory)
    {
        var projectDir = Path.Combine(_projectsRoot, ClaudeProjectPathResolver.ToProjectDirName(workingDirectory));
        return TranscriptLimitWatcher.PickMostRecentTranscriptFile(projectDir);
    }

    public string? FindActiveTranscriptFile(string workingDirectory, DateTimeOffset notBefore)
    {
        var projectDir = Path.Combine(_projectsRoot, ClaudeProjectPathResolver.ToProjectDirName(workingDirectory));
        return TranscriptLimitWatcher.PickActiveTranscriptFile(projectDir, notBefore);
    }

    public ProjectActivityState? Classify(string tailText) => TranscriptActivityClassifier.ClassifyText(tailText);

    public string? ExtractPreview(string tailText) => TranscriptPreviewReader.ExtractPreview(tailText);

    public DateTimeOffset? TryParseUsageLimitEvent(string jsonlLine) => UsageLimitEventParser.TryParseLine(jsonlLine);

    /// <summary>Always null: Claude Code's transcript has no end-of-turn record. The Stop hook writes a
    /// <see cref="Models.StatusMarker.TurnCompleteReason"/> marker instead, which
    /// <see cref="AgentEventNotifier"/> announces.</summary>
    public DateTimeOffset? TryDetectTurnComplete(string tailText) => null;
}
