using System.IO;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Remembers, per AI CLI, when its account-wide rate limit frees up again. Persisted under
/// %APPDATA%\SmoothCoder like <see cref="UsageSnapshotStore"/> and <see cref="UsageCalibrationStore"/>.
///
/// <para>Account-wide, not per project: a usage limit belongs to the account, so a limit hit while
/// working on one project also blocks every other project. Keeping this on
/// <see cref="Models.SessionProfile"/> would let two projects each conclude the other agent was free
/// and hand work back and forth between them.</para>
///
/// <para>Persisted rather than held in memory for the same reason: <see cref="HandoffPlanner"/>'s
/// whole job is to not send a task to an exhausted account, and an app restart that forgot which
/// accounts were exhausted would resume exactly the handoff loop the planner exists to prevent.</para>
///
/// <para>Every read is defensive - a missing or corrupt file reads as "nothing is limited", which
/// degrades to the pre-ledger behavior rather than taking the launcher down.</para></summary>
public sealed class AgentCooldownStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;

    /// <summary>Guards <see cref="_cooldowns"/> and the file writes behind it. Today every caller is
    /// on the WPF dispatcher (<see cref="ViewModels.MainViewModel"/>'s schedule tick and the session
    /// view models it drives), so this is uncontended - but one instance is shared by every project,
    /// which makes it exactly the sort of thing a later background caller gets added to. Locking now
    /// costs nothing at this call frequency and removes the question.</summary>
    private readonly Lock _gate = new();

    private Dictionary<AgentKind, DateTimeOffset> _cooldowns;

    public AgentCooldownStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Smooth-Coder", "agent-cooldowns.json"))
    {
    }

    public AgentCooldownStore(string filePath)
    {
        _filePath = filePath;
        _cooldowns = Load();
    }

    public string FilePath => _filePath;

    /// <summary>When <paramref name="kind"/>'s account frees up, or null if it isn't known to be
    /// limited. An entry whose time has already passed reads as null.</summary>
    public DateTimeOffset? Get(AgentKind kind, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _cooldowns.TryGetValue(kind, out var resetAt) && resetAt > now ? resetAt : null;
        }
    }

    public bool IsCoolingDown(AgentKind kind, DateTimeOffset now) => Get(kind, now) is not null;

    /// <summary>Records that <paramref name="kind"/> is limited until <paramref name="resetAt"/>.
    /// Later reset times win: two limits can be in force at once (a 5-hour window and a weekly one),
    /// and the account is only usable again once the longer of them has passed. A reset time already
    /// in the past is ignored, so re-reading an old transcript line can't resurrect a stale cooldown.
    /// Returns true if the stored state changed.</summary>
    public bool Set(AgentKind kind, DateTimeOffset resetAt, DateTimeOffset now)
    {
        if (resetAt <= now)
        {
            return false;
        }

        lock (_gate)
        {
            if (_cooldowns.TryGetValue(kind, out var existing) && existing >= resetAt)
            {
                return false;
            }

            _cooldowns[kind] = resetAt;
            Save();
            return true;
        }
    }

    /// <summary>Drops entries whose reset time has passed. Returns true if anything was removed.</summary>
    public bool PruneExpired(DateTimeOffset now)
    {
        lock (_gate)
        {
            var expired = _cooldowns.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList();
            if (expired.Count == 0)
            {
                return false;
            }

            foreach (var kind in expired)
            {
                _cooldowns.Remove(kind);
            }

            Save();
            return true;
        }
    }

    private Dictionary<AgentKind, DateTimeOffset> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<Dictionary<AgentKind, DateTimeOffset>>(json, JsonOptions) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or NotSupportedException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Callers already hold _gate, so the dictionary can't be mutated mid-serialize.
            var json = JsonSerializer.Serialize(_cooldowns, JsonOptions);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // In-memory state stays correct for this run; a failed write must never stop a failover.
        }
    }

    /// <summary>Re-reads the file, discarding in-memory state. Only needed by tests that write the
    /// file behind this instance's back.</summary>
    public void Reload()
    {
        var loaded = Load();
        lock (_gate)
        {
            _cooldowns = loaded;
        }
    }
}
