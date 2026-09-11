using System.IO;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Persists session profiles as JSON under %APPDATA%\SmoothCoder.</summary>
public sealed class SessionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public SessionProfileStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Smooth-Coder", "sessions.json"))
    {
    }

    public SessionProfileStore(string filePath)
    {
        _filePath = filePath;
    }

    public List<SessionProfile> Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<List<SessionProfile>>(json, JsonOptions) ?? [];
    }

    public void Save(IEnumerable<SessionProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
