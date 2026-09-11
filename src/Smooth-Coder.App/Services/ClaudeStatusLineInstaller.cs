using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmoothCoder.App.Services;

/// <summary>Registers (and removes) the launcher's <c>usage-statusline</c> bridge as Claude Code's
/// status-line command in <c>~/.claude/settings.json</c>. The status-line stdin JSON is the only
/// channel Anthropic gives for the account's real <c>rate_limits</c> figures (see
/// <see cref="UsageStatusLineBridge"/>), so the launcher has to own that slot to read them.
///
/// <para>Edits are done through <see cref="JsonNode"/>, never a typed model: settings.json holds many
/// keys this app knows nothing about (plugins, hooks, permissions, ...) and every one must survive the
/// round-trip untouched. A one-time <c>.bak-smoothcoder-*</c> copy is taken before the first write,
/// and writes are tmp-then-move like the rest of this codebase's stores.</para></summary>
public sealed class ClaudeStatusLineInstaller
{
    /// <summary>Marker that identifies our command inside settings.json, so install is idempotent and
    /// we never stash our own entry as if it were the user's pre-existing status line.</summary>
    public const string BridgeCommandMarker = "usage-statusline";

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _settingsPath;

    public ClaudeStatusLineInstaller()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json"))
    {
    }

    public ClaudeStatusLineInstaller(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    /// <summary>Points <c>statusLine</c> at <c>"&lt;exe&gt;" usage-statusline</c>. Returns the raw JSON
    /// of whatever <c>statusLine</c> object was there before (for the caller to stash in
    /// <see cref="Models.AppSettings.ChainedStatusLine"/> and later restore), or null if there was
    /// none or it was already ours.</summary>
    public string? Install(string exePath)
    {
        var root = ReadRoot();

        string? previousStatusLine = null;
        if (root["statusLine"] is JsonObject existing)
        {
            var command = existing["command"]?.GetValue<string>();
            if (command is null || !command.Contains(BridgeCommandMarker, StringComparison.Ordinal))
            {
                previousStatusLine = existing.ToJsonString();
            }
        }

        BackupOnce();

        root["statusLine"] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = $"\"{exePath}\" {BridgeCommandMarker}",
            ["padding"] = 0,
        };

        Write(root);
        return previousStatusLine;
    }

    /// <summary>Restores <paramref name="chainedStatusLineJson"/> (the value <see cref="Install"/>
    /// returned) as <c>statusLine</c>, or removes the key entirely when there's nothing to restore.
    /// Only touches <c>statusLine</c> if it's currently ours - a status line the user set by hand
    /// afterwards is left alone.</summary>
    public void Uninstall(string? chainedStatusLineJson)
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        var root = ReadRoot();

        if (root["statusLine"] is JsonObject current
            && current["command"]?.GetValue<string>() is { } command
            && !command.Contains(BridgeCommandMarker, StringComparison.Ordinal))
        {
            // User replaced it with their own since we installed - don't clobber that.
            return;
        }

        if (!string.IsNullOrWhiteSpace(chainedStatusLineJson)
            && SafeParseObject(chainedStatusLineJson) is { } restored)
        {
            root["statusLine"] = restored;
        }
        else
        {
            root.Remove("statusLine");
        }

        Write(root);
    }

    public bool IsBridgeInstalled()
    {
        try
        {
            return ReadRoot()["statusLine"]?["command"]?.GetValue<string>() is { } command
                && command.Contains(BridgeCommandMarker, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Rewrites the registered command to <paramref name="exePath"/> if the bridge is
    /// installed - mirrors <see cref="StartupRegistrationService.RefreshPathIfEnabled"/>, since
    /// Velopack swaps the <c>current\</c> folder in place on every update and a path captured at
    /// install time would go stale.</summary>
    public void RefreshPathIfInstalled(string exePath)
    {
        try
        {
            var root = ReadRoot();
            if (root["statusLine"] is not JsonObject sl
                || sl["command"]?.GetValue<string>() is not { } command
                || !command.Contains(BridgeCommandMarker, StringComparison.Ordinal))
            {
                return;
            }

            var wanted = $"\"{exePath}\" {BridgeCommandMarker}";
            if (command == wanted)
            {
                return;
            }

            sl["command"] = wanted;
            Write(root);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Best effort - a stale path just means the bar falls back to the estimate.
        }
    }

    private JsonObject ReadRoot()
    {
        if (!File.Exists(_settingsPath))
        {
            return new JsonObject();
        }

        try
        {
            var text = File.ReadAllText(_settingsPath);
            return JsonNode.Parse(text, nodeOptions: null, ParseOptions) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static JsonObject? SafeParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void BackupOnce()
    {
        if (!File.Exists(_settingsPath))
        {
            return;
        }

        var backupPath = _settingsPath + $".bak-smoothcoder-{DateTimeOffset.Now:yyyyMMdd}";
        if (!File.Exists(backupPath))
        {
            File.Copy(_settingsPath, backupPath);
        }
    }

    private void Write(JsonObject root)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = root.ToJsonString(WriteOptions);
        var tempPath = _settingsPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _settingsPath, overwrite: true);
    }
}
