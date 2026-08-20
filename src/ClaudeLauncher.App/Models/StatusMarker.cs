namespace ClaudeLauncher.App.Models;

/// <summary>A "this Claude Code session is waiting on you" signal written by the
/// write-status-marker.py hook (see Services/StatusMarkerStore) for a single session/turn.</summary>
public sealed record StatusMarker(string Cwd, string Reason, DateTimeOffset UpdatedAt);
