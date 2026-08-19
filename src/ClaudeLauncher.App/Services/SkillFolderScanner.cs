using System.IO;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Lists skill folders (each a directory containing SKILL.md) and extracts the
/// `description:` line from the YAML frontmatter for display. Deliberately not a full YAML
/// parser - skills only need enough to show a one-line summary in a list.</summary>
public static class SkillFolderScanner
{
    public static string GetUserSkillsDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "skills");

    public static IReadOnlyList<SkillInfo> Scan(string skillsRoot)
    {
        if (!Directory.Exists(skillsRoot))
        {
            return [];
        }

        var result = new List<SkillInfo>();
        foreach (var dir in Directory.GetDirectories(skillsRoot))
        {
            var skillMdPath = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillMdPath))
            {
                continue;
            }

            result.Add(new SkillInfo(Path.GetFileName(dir), dir, ParseDescription(File.ReadAllText(skillMdPath))));
        }

        return result;
    }

    public static string? ParseDescription(string skillMdContent)
    {
        var lines = skillMdContent.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != "---")
        {
            return null;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                break;
            }

            var separatorIndex = lines[i].IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = lines[i][..separatorIndex].Trim();
            if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
            {
                return lines[i][(separatorIndex + 1)..].Trim().Trim('"');
            }
        }

        return null;
    }
}
