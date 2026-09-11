using System.IO;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Persists <see cref="UsageSnapshot"/> as JSON under %APPDATA%\SmoothCoder, mirroring
/// <see cref="UsageCalibrationStore"/>'s load/save shape. Both the status-line bridge process (writer)
/// and the running app (reader) go through this type, so the file format lives in one place. Every
/// read is defensive: a missing or malformed file yields an empty <see cref="UsageSnapshot"/>, never
/// an exception - a broken snapshot must never take the usage bars down.</summary>
public sealed class UsageSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    public UsageSnapshotStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smooth-Coder", "usage-snapshot.json"))
    {
    }

    public UsageSnapshotStore(string filePath)
    {
        _filePath = filePath;
    }

    public string FilePath => _filePath;

    /// <summary>Null (not an empty object) when the file is absent or unreadable, so callers can tell
    /// "the bridge has never run" from "the bridge ran but reported no rate-limit windows".</summary>
    public UsageSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<UsageSnapshot>(json, JsonOptions);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(UsageSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
