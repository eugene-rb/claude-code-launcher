using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class ExistingProjectScannerTests
{
    private static string CwdLine(string cwd) => $$$"""{"type":"user","cwd":"{{{cwd.Replace("\\", "\\\\")}}}","message":{}}""";

    [Fact]
    public void Scan_MissingRoot_ReturnsEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));

        Assert.Empty(ExistingProjectScanner.Scan(root));
    }

    [Fact]
    public void Scan_DirectoryWithNoJsonlFiles_IsSkipped()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "empty-project"));
        try
        {
            Assert.Empty(ExistingProjectScanner.Scan(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_CwdOnFirstLine_IsFound()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, "D--Dev-Sample");
        Directory.CreateDirectory(projectDir);
        try
        {
            File.WriteAllText(Path.Combine(projectDir, "a.jsonl"), CwdLine(@"D:\Dev\Sample") + "\n");

            var results = ExistingProjectScanner.Scan(root);

            Assert.Single(results);
            Assert.Equal(@"D:\Dev\Sample", results[0].WorkingDirectory);
            Assert.Equal("Sample", results[0].SuggestedName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_CwdOnLaterLine_IsStillFound()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, "D--Dev-Sample");
        Directory.CreateDirectory(projectDir);
        try
        {
            var content = string.Join('\n',
                """{"type":"summary","summary":"no cwd on this line"}""",
                """{"type":"system","message":"still nothing"}""",
                CwdLine(@"D:\Dev\Sample")) + "\n";
            File.WriteAllText(Path.Combine(projectDir, "a.jsonl"), content);

            var results = ExistingProjectScanner.Scan(root);

            Assert.Single(results);
            Assert.Equal(@"D:\Dev\Sample", results[0].WorkingDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_MultipleFiles_OnlyOneHasCwd_IsFound()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, "D--Dev-Sample");
        Directory.CreateDirectory(projectDir);
        try
        {
            File.WriteAllText(Path.Combine(projectDir, "no-cwd.jsonl"), """{"type":"summary"}""" + "\n");
            File.WriteAllText(Path.Combine(projectDir, "has-cwd.jsonl"), CwdLine(@"D:\Dev\Sample") + "\n");

            var results = ExistingProjectScanner.Scan(root);

            Assert.Single(results);
            Assert.Equal(@"D:\Dev\Sample", results[0].WorkingDirectory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_NoFileHasCwd_ProjectIsExcluded()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, "D--Dev-Sample");
        Directory.CreateDirectory(projectDir);
        try
        {
            File.WriteAllText(Path.Combine(projectDir, "a.jsonl"), """{"type":"summary"}""" + "\n");

            Assert.Empty(ExistingProjectScanner.Scan(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_NestedWorkingDirectory_SuggestedNameIsLastSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, "D--Dev-Sample-tests-Sample-Tests");
        Directory.CreateDirectory(projectDir);
        try
        {
            File.WriteAllText(Path.Combine(projectDir, "a.jsonl"), CwdLine(@"D:\Dev\Sample\tests\Sample.Tests") + "\n");

            var results = ExistingProjectScanner.Scan(root);

            Assert.Single(results);
            Assert.Equal("Sample.Tests", results[0].SuggestedName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Scan_LastActivity_IsMaxAcrossFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(root, "D--Dev-Sample");
        Directory.CreateDirectory(projectDir);
        try
        {
            var older = Path.Combine(projectDir, "older.jsonl");
            var newer = Path.Combine(projectDir, "newer.jsonl");
            File.WriteAllText(older, CwdLine(@"D:\Dev\Sample") + "\n");
            File.WriteAllText(newer, CwdLine(@"D:\Dev\Sample") + "\n");

            var newerTime = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(older, newerTime.AddDays(-1));
            File.SetLastWriteTimeUtc(newer, newerTime);

            var results = ExistingProjectScanner.Scan(root);

            Assert.Single(results);
            Assert.True(Math.Abs((results[0].LastActivityAt.UtcDateTime - newerTime).TotalSeconds) < 2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
