using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class AntigravityTranscriptSourceTests
{
    [Fact]
    public void FindMostRecentTranscriptFile_NoConversationsRoot_ReturnsNullWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        var source = new AntigravityTranscriptSource(root);

        Assert.Null(source.FindMostRecentTranscriptFile(@"D:\Fake\Project"));
    }

    [Fact]
    public void FindMostRecentTranscriptFile_NoCwdFieldInAnyFile_DegradesToNullRatherThanGuessing()
    {
        // Antigravity's conversation<->cwd mapping is unconfirmed; a file with no recognizable cwd
        // field must never be guessed at from recency alone (that would misattribute someone else's
        // project's activity to this one).
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "conv-1.jsonl"), """{"role":"user","content":"hi"}""" + "\n");

            var source = new AntigravityTranscriptSource(root);

            Assert.Null(source.FindMostRecentTranscriptFile(@"D:\Fake\Project"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindMostRecentTranscriptFile_MatchingCwdField_IsFound()
    {
        var root = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var workingDirectory = Path.Combine(root, "project");
            var file = Path.Combine(root, "conv-1.jsonl");
            File.WriteAllText(file, $$"""{"cwd":"{{workingDirectory.Replace("\\", "\\\\")}}"}""" + "\n");

            var source = new AntigravityTranscriptSource(root);

            Assert.Equal(file, source.FindMostRecentTranscriptFile(workingDirectory));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SupportsUsageLimitAutoResume_IsFalse()
    {
        Assert.False(new AntigravityTranscriptSource().SupportsUsageLimitAutoResume);
    }
}
