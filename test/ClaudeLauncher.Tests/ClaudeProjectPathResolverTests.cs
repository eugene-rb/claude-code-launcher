using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class ClaudeProjectPathResolverTests
{
    // Verified against this machine's real ~/.claude/projects folder names.
    [Theory]
    [InlineData(@"D:\Dev\Cludecode-resumer", "D--Dev-Cludecode-resumer")]
    [InlineData(@"D:\Dev\Cludecode-resumer\tests\ClaudeResumer.Tests", "D--Dev-Cludecode-resumer-tests-ClaudeResumer-Tests")]
    [InlineData(@"C:\Users\kind4", "C--Users-kind4")]
    public void ToProjectDirName_MatchesRealClaudeCodeNaming(string workingDirectory, string expected)
    {
        Assert.Equal(expected, ClaudeProjectPathResolver.ToProjectDirName(workingDirectory));
    }
}
