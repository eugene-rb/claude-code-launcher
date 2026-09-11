namespace SmoothCoder.App.Models;

/// <summary>Everything the launcher needs to know about one AI CLI, gathered in a single record so a
/// wrong fact about a specific CLI's flags is a one-line edit in <see cref="Services.AgentCatalog"/>
/// rather than a code change scattered across the app. <see cref="Services.ProcessLauncherService"/>
/// treats every field here as data, never hardcoding a particular CLI's syntax.</summary>
public sealed record AgentDefinition(
    AgentKind Kind,
    string DisplayName,
    string DefaultExecutable,
    string DefaultArguments,
    /// <summary>Tokens appended/prefixed when resuming, with a single <c>"{args}"</c> entry marking
    /// where the (already-filtered) profile argument tokens are spliced in. E.g. Claude Code's
    /// <c>["{args}", "-c"]</c> appends a continuation flag after the profile's own arguments; Codex
    /// CLI's <c>["resume", "--last", "{args}"]</c> instead prefixes a subcommand, since `codex resume`
    /// is a distinct subcommand rather than a flag on `codex` itself.</summary>
    IReadOnlyList<string> ResumeArgumentTemplate,
    /// <summary>Flag tokens stripped from the profile's own arguments before resuming, because they'd
    /// otherwise collide with <see cref="ResumeArgumentTemplate"/> (e.g. a user-configured `--resume`
    /// alongside an appended `-c`). These never consume a following token.</summary>
    IReadOnlyList<string> ResumeFlagsToStrip,
    /// <summary>Like <see cref="ResumeFlagsToStrip"/>, but each of these may optionally be followed by
    /// a value token (e.g. Claude's `-r &lt;session-id&gt;`) that must be dropped along with the flag
    /// itself, unless the next token looks like another flag.</summary>
    IReadOnlyList<string> ResumeFlagsToStripWithOptionalValue,
    /// <summary>Extra token appended only when resuming with <see cref="ResumeMode.CompactFirst"/> -
    /// Claude Code's `/compact` positional prompt. Null for every other agent; none of them are known
    /// to have an equivalent "resume, then summarize" mode.</summary>
    string? CompactResumeExtraToken,
    /// <summary>Environment variables set only for a resume launch. Only Claude Code has known values
    /// here (suppressing its "Resume from summary?" chooser) - left empty for the others rather than
    /// guessing variable names that could silently do nothing or hang an unattended launch.</summary>
    IReadOnlyDictionary<string, string> ResumeEnvironmentVariables,
    /// <summary>Project-level instruction file this agent reads for context, e.g. `CLAUDE.md` or
    /// `AGENTS.md`.</summary>
    string ProjectInstructionFileName,
    /// <summary>This agent's per-user config/data directory, relative to the user profile folder.</summary>
    string UserConfigDirectoryRelativeToHome,
    /// <summary>Whether usage-limit detection is allowed to stop and relaunch a running session for
    /// this agent. Claude Code and Codex expose verified limit information in their transcripts; other
    /// a false positive here kills a live process, unlike the read-only activity badge/preview.</summary>
    bool SupportsUsageLimitAutoResume);
