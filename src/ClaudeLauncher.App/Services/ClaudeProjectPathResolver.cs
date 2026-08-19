using System.IO;
using System.Text.RegularExpressions;

namespace ClaudeLauncher.App.Services;

/// <summary>Resolves the on-disk location of a Claude Code project's conversation transcripts.
/// Claude Code stores every session's JSONL transcript under a per-working-directory folder whose
/// name is the working directory's absolute path with every non-alphanumeric character replaced by
/// '-'. Verified against this machine's real ~/.claude/projects folder names (not documented).</summary>
public static partial class ClaudeProjectPathResolver
{
    public static string GetProjectsRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public static string ToProjectDirName(string absoluteWorkingDirectory) =>
        NonAlphanumeric().Replace(absoluteWorkingDirectory, "-");

    [GeneratedRegex("[^A-Za-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
