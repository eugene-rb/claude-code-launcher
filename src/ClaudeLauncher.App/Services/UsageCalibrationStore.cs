using System.IO;
using System.Text.Json;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Persists <see cref="UsageCalibration"/> as JSON under %APPDATA%\ClaudeLauncher, mirroring
/// <see cref="AppSettingsStore"/>'s load/save shape. Kept as its own file rather than a field on
/// <see cref="AppSettings"/> because <see cref="ViewModels.SettingsViewModel.Persist"/> rebuilds a whole
/// new <see cref="AppSettings"/> object field-by-field from its own observable properties every time any
/// user-editable setting changes - a system-computed value folded into that object would get silently
/// reset to default the next time an unrelated setting changed.</summary>
public sealed class UsageCalibrationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public UsageCalibrationStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeLauncher", "usage-calibration.json"))
    {
    }

    public UsageCalibrationStore(string filePath)
    {
        _filePath = filePath;
    }

    public UsageCalibration Load()
    {
        if (!File.Exists(_filePath))
        {
            return new UsageCalibration();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<UsageCalibration>(json, JsonOptions) ?? new UsageCalibration();
    }

    public void Save(UsageCalibration calibration)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(calibration, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
