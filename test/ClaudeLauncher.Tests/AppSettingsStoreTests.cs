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
        Assert.True(store.Load().CrossAgentHandoffEnabled);
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
    public void SaveThenLoad_RoundTripsDisabledCrossAgentHandoff()
    {
        var path = CreateTempSettingsPath();
        try
        {
            var store = new AppSettingsStore(path);
            store.Save(new AppSettings { CrossAgentHandoffEnabled = false });

            Assert.False(new AppSettingsStore(path).Load().CrossAgentHandoffEnabled);
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

    [Fact]
    public void Load_SettingsWrittenBeforeAgentSettingsExisted_MigratesClaudeCodeFromLegacyFields()
    {
        var path = CreateTempSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, """
                {
                  "DefaultExecutable": "claude",
                  "DefaultArguments": "--model sonnet"
                }
                """);

            var settings = new AppSettingsStore(path).Load();

            Assert.Equal(4, settings.AgentSettings.Count);
            Assert.Equal("claude", settings.AgentSettings[AgentKind.ClaudeCode].Executable);
            Assert.Equal("--model sonnet", settings.AgentSettings[AgentKind.ClaudeCode].Arguments);
            Assert.Equal("codex", settings.AgentSettings[AgentKind.CodexCli].Executable);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Load_AgentSettingsAlreadyPopulated_IsNotOverwrittenByMigration()
    {
        var path = CreateTempSettingsPath();
        try
        {
            var store = new AppSettingsStore(path);
            var settings = new AppSettings();
            settings.AgentSettings[AgentKind.ClaudeCode] = new AgentExecutionSettings { Executable = "custom-claude", Arguments = "--foo" };
            store.Save(settings);

            var reloaded = new AppSettingsStore(path).Load();

            Assert.Equal("custom-claude", reloaded.AgentSettings[AgentKind.ClaudeCode].Executable);
            Assert.Single(reloaded.AgentSettings);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
