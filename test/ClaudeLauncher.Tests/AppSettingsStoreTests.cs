using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class AppSettingsStoreTests
{
    private static string CreateTempSettingsPath() =>
        Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests_" + Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public void Load_NoFileYet_DefaultsToResumingTheFullSession()
    {
        var store = new AppSettingsStore(CreateTempSettingsPath());

        Assert.Equal(ResumeMode.FullSession, store.Load().ResumeMode);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsResumeMode()
    {
        var path = CreateTempSettingsPath();
        try
        {
            var store = new AppSettingsStore(path);
            store.Save(new AppSettings { ResumeMode = ResumeMode.CompactFirst });

            Assert.Equal(ResumeMode.CompactFirst, new AppSettingsStore(path).Load().ResumeMode);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Load_SettingsWrittenBeforeResumeModeExisted_KeepsTheOtherValuesAndDefaultsTheNewOne()
    {
        var path = CreateTempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, """
                {
                  "DefaultExecutable": "claude",
                  "DefaultArguments": "--model sonnet",
                  "AutoResumeOnLimitEnabled": true
                }
                """);

            var settings = new AppSettingsStore(path).Load();

            Assert.Equal("--model sonnet", settings.DefaultArguments);
            Assert.True(settings.AutoResumeOnLimitEnabled);
            Assert.Equal(ResumeMode.FullSession, settings.ResumeMode);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
