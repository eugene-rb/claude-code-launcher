using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class StatusMarkerStoreTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MarkerJson(string cwd, string reason, DateTimeOffset updatedAt) =>
        $$"""{"cwd":"{{cwd.Replace("\\", "\\\\")}}","reason":"{{reason}}","updatedAt":"{{updatedAt:O}}"}""";

    [Fact]
    public void ReadFresh_MissingDirectory_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"));

        Assert.Empty(StatusMarkerStore.ReadFresh(dir, TimeSpan.FromMinutes(10), DateTimeOffset.Now));
    }

    [Fact]
    public void ReadFresh_FreshMarker_IsReturned()
    {
        var dir = CreateTempDir();
        try
        {
            var now = DateTimeOffset.Now;
            File.WriteAllText(Path.Combine(dir, "session1.json"), MarkerJson(@"D:\Dev\Sample", "permission_prompt", now));

            var results = StatusMarkerStore.ReadFresh(dir, TimeSpan.FromMinutes(10), now);

            var marker = Assert.Single(results);
            Assert.Equal(@"D:\Dev\Sample", marker.Cwd);
            Assert.Equal("permission_prompt", marker.Reason);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadFresh_StaleMarker_IsExcluded()
    {
        var dir = CreateTempDir();
        try
        {
            var now = DateTimeOffset.Now;
            File.WriteAllText(Path.Combine(dir, "session1.json"), MarkerJson(@"D:\Dev\Sample", "permission_prompt", now.AddMinutes(-30)));

            Assert.Empty(StatusMarkerStore.ReadFresh(dir, TimeSpan.FromMinutes(10), now));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadFresh_MalformedJson_IsSkippedNotThrown()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not valid json");

            Assert.Empty(StatusMarkerStore.ReadFresh(dir, TimeSpan.FromMinutes(10), DateTimeOffset.Now));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadFresh_MultipleFreshMarkers_AllReturned()
    {
        var dir = CreateTempDir();
        try
        {
            var now = DateTimeOffset.Now;
            File.WriteAllText(Path.Combine(dir, "s1.json"), MarkerJson(@"D:\Dev\A", "permission_prompt", now));
            File.WriteAllText(Path.Combine(dir, "s2.json"), MarkerJson(@"D:\Dev\B", "ask_or_plan", now));

            var results = StatusMarkerStore.ReadFresh(dir, TimeSpan.FromMinutes(10), now);

            Assert.Equal(2, results.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
