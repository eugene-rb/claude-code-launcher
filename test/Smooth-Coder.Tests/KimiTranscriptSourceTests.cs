using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class KimiTranscriptSourceTests
{
    [Fact]
    public void FindMostRecentTranscriptFile_NoIndexFile_ReturnsNullWithoutThrowing()
    {
        var home = Path.Combine(Path.GetTempPath(), "SmoothCoderTests_" + Guid.NewGuid().ToString("N"));
        var source = new KimiTranscriptSource(home);

        Assert.Null(source.FindMostRecentTranscriptFile(@"D:\Fake\Project"));
    }

    [Fact]
    public void FindMostRecentTranscriptFile_MatchingWorkDirInIndex_ResolvesWireFile()
    {
        var home = Path.Combine(Path.GetTempPath(), "SmoothCoderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);
        try
        {
            var workingDirectory = Path.Combine(home, "project");
            var sessionDir = Path.Combine("wd_project_abc123", "session-1");
            var wireDir = Path.Combine(home, "sessions", sessionDir, "agents", "main");
            Directory.CreateDirectory(wireDir);
            File.WriteAllText(Path.Combine(wireDir, "wire.jsonl"), """{"role":"user","content":"hi"}""" + "\n");

            var indexLine = $$"""{"sessionId":"session-1","sessionDir":"{{sessionDir.Replace("\\", "\\\\")}}","workDir":"{{workingDirectory.Replace("\\", "\\\\")}}"}""";
            File.WriteAllText(Path.Combine(home, "session_index.jsonl"), indexLine + "\n");

            var source = new KimiTranscriptSource(home);

            var found = source.FindMostRecentTranscriptFile(workingDirectory);

            Assert.Equal(Path.Combine(wireDir, "wire.jsonl"), found);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void SupportsUsageLimitAutoResume_IsFalse()
    {
        Assert.False(new KimiTranscriptSource().SupportsUsageLimitAutoResume);
    }
}
