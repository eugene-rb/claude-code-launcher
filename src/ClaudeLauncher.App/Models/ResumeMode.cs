namespace ClaudeLauncher.App.Models;

/// <summary>How a resume launch (`-c`) should treat the conversation it picks back up. Either value
/// suppresses Claude Code's own "Resume from summary?" chooser - see
/// <see cref="Services.ProcessLauncherService.BuildResumeEnvironment"/> for why that chooser cannot be
/// left to a human on an unattended relaunch - so this setting is what decides, on the user's behalf,
/// which of the chooser's two branches the launcher takes.</summary>
public enum ResumeMode
{
    /// <summary>Continue the conversation in full, exactly as it was left. This is what plain `-c` did
    /// before the chooser existed, and it re-reads the whole transcript on the first turn back.</summary>
    FullSession = 0,

    /// <summary>Continue, then immediately run `/compact` so the conversation carries on from a summary
    /// rather than its full history - the same thing the chooser's recommended branch does, and the
    /// cheaper option when the relaunch was triggered by hitting a usage limit in the first place.</summary>
    CompactFirst = 1,
}
