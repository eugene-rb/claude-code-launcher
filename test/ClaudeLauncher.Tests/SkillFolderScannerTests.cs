using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class SkillFolderScannerTests
{
    [Fact]
    public void ParseDescription_ExtractsFromFrontmatter()
    {
        const string content = """
            ---
            name: my-skill
            description: does a thing well
            ---
            # Body
            """;

        Assert.Equal("does a thing well", SkillFolderScanner.ParseDescription(content));
    }

    [Fact]
    public void ParseDescription_QuotedValue_StripsQuotes()
    {
        const string content = """
            ---
            description: "quoted description"
            ---
            """;

        Assert.Equal("quoted description", SkillFolderScanner.ParseDescription(content));
    }

    [Fact]
    public void ParseDescription_NoFrontmatter_ReturnsNull()
    {
        Assert.Null(SkillFolderScanner.ParseDescription("# Just a heading\nno frontmatter here"));
    }

    [Fact]
    public void ParseDescription_MissingDescriptionKey_ReturnsNull()
    {
        const string content = """
            ---
            name: my-skill
            ---
            """;

        Assert.Null(SkillFolderScanner.ParseDescription(content));
    }

    [Fact]
    public void Scan_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));

        Assert.Empty(SkillFolderScanner.Scan(missing));
    }

    [Fact]
    public void Scan_ListsFoldersContainingSkillMd_SkipsFoldersWithout()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var withSkill = Path.Combine(root, "with-skill");
            Directory.CreateDirectory(withSkill);
            File.WriteAllText(Path.Combine(withSkill, "SKILL.md"), "---\ndescription: has a skill file\n---\n");

            var withoutSkill = Path.Combine(root, "without-skill");
            Directory.CreateDirectory(withoutSkill);

            var results = SkillFolderScanner.Scan(root);

            Assert.Single(results);
            Assert.Equal("with-skill", results[0].Name);
            Assert.Equal("has a skill file", results[0].Description);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
