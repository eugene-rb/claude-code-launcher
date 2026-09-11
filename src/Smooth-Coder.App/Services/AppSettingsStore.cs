using System.IO;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Persists <see cref="AppSettings"/> as JSON under %APPDATA%\SmoothCoder, mirroring
/// <see cref="SessionProfileStore"/>'s load/save shape.</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppSettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smooth-Coder", "settings.json"))
    {
    }

    public AppSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return MigrateAgentSettings(new AppSettings());
        }

        var json = File.ReadAllText(_filePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        return MigrateAgentSettings(settings);
    }

    /// <summary>Fills <see cref="AppSettings.AgentSettings"/> from <see cref="AgentCatalog"/>'s
    /// defaults the first time it's loaded after this field was introduced (empty dictionary), seeding
    /// Claude Code's entry from the legacy <see cref="AppSettings.DefaultExecutable"/>/
    /// <see cref="AppSettings.DefaultArguments"/> instead of the catalog default so a user's existing
    /// customization survives the upgrade unchanged. A no-op once <see cref="AppSettings.AgentSettings"/>
    /// has been populated and saved once.</summary>
    private static AppSettings MigrateAgentSettings(AppSettings settings)
    {
        if (settings.AgentSettings.Count > 0)
        {
            return settings;
        }

        foreach (var agent in AgentCatalog.All)
        {
            settings.AgentSettings[agent.Kind] = agent.Kind == AgentKind.ClaudeCode
                ? new AgentExecutionSettings { Executable = settings.DefaultExecutable, Arguments = settings.DefaultArguments }
                : new AgentExecutionSettings { Executable = agent.DefaultExecutable, Arguments = agent.DefaultArguments };
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
