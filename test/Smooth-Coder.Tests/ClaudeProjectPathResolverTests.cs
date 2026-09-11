using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class ClaudeProjectPathResolverTests
{
    // Verified against this machine's real ~/.claude/projects folder names.
    [Theory]
    [InlineData(@"D:\Dev\Smooth-Coder", "D--Dev-Smooth-Coder")]
    [InlineData(@"D:\Dev\Smooth-Coder\tests\SmoothCoder.Tests", "D--Dev-Smooth-Coder-tests-SmoothCoder-Tests")]
    [InlineData(@"C:\Users\kind4", "C--Users-kind4")]
    public void ToProjectDirName_MatchesRealClaudeCodeNaming(string workingDirectory, string expected)
    {
        Assert.Equal(expected, ClaudeProjectPathResolver.ToProjectDirName(workingDirectory));
    }
}
