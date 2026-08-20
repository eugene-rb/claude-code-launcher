using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class WorkingDirectoryComparerTests
{
    [Fact]
    public void AreSame_DifferentCase_IsTrue()
    {
        Assert.True(WorkingDirectoryComparer.AreSame(@"D:\Dev\Sample", @"d:\dev\sample"));
    }

    [Fact]
    public void AreSame_TrailingSeparator_IsTrue()
    {
        Assert.True(WorkingDirectoryComparer.AreSame(@"D:\Dev\Sample", @"D:\Dev\Sample\"));
    }

    [Fact]
    public void AreSame_DifferentPaths_IsFalse()
    {
        Assert.False(WorkingDirectoryComparer.AreSame(@"D:\Dev\Sample", @"D:\Dev\Other"));
    }
}
