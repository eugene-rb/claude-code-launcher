using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Announces the per-session events the hook reports through <see cref="StatusMarkerStore"/> -
/// a turn ending, or a session blocking on the user - exactly once each.
///
/// <para>Not scoped to the dashboard's projects. Until this existed, ~/.claude/hooks/notify-sound.py
/// played those announcements itself and so covered every Claude Code session on the machine, whether
/// or not the launcher knew about its directory. Filtering to registered projects here would have read
/// as the notifications having broken, so every fresh marker is announced and the project matching in
/// <see cref="ViewModels.SessionItemViewModel.RefreshActivityState"/> stays a badge concern only.</para>
///
/// <para>The de-duplication is the whole job. A marker sits on disk for its entire freshness window
/// (<see cref="StatusMarkerStore.DefaultMaxAge"/>), while the dashboard re-reads the directory every
/// two seconds, so "a marker is present" says nothing about whether it has already been announced.
/// What identifies one event is (session, reason, timestamp): the hook rewrites the file - bumping
/// <see cref="StatusMarker.UpdatedAt"/> - for every state it reports.</para>
///
/// <para>Takes the announcement as a delegate rather than the <see cref="VoiceNotificationService"/>
/// itself: what this class decides is <em>which</em> events are new, and that is worth testing without
/// a WPF resource lookup and an audible playback per assertion.</para></summary>
public sealed class AgentEventNotifier(Action<VoiceCue> play)
{
    private readonly Dictionary<string, (string Reason, DateTimeOffset UpdatedAt)> _announced = new(StringComparer.Ordinal);
    private bool _seeded;

    /// <summary>Records <paramref name="freshMarkers"/> and announces whatever is new since the last
    /// call. The first call only records: markers stay fresh for ten minutes, so a launcher started
    /// (or restarted) in the middle of a session would otherwise open by announcing a backlog of
    /// events the user has already seen and dealt with.</summary>
    public void Observe(IReadOnlyList<StatusMarker> freshMarkers)
    {
        var seeding = !_seeded;
        _seeded = true;

        foreach (var marker in freshMarkers)
        {
            if (_announced.TryGetValue(marker.SessionId, out var last)
                && last.Reason == marker.Reason && last.UpdatedAt == marker.UpdatedAt)
            {
                continue;
            }

            _announced[marker.SessionId] = (marker.Reason, marker.UpdatedAt);

            if (!seeding && CueFor(marker.Reason) is { } cue)
            {
                play(cue);
            }
        }

        Forget(freshMarkers);
    }

    /// <summary>Drops sessions whose markers are gone (cleared by the hook, or aged out), so a machine
    /// left running for weeks doesn't accumulate an entry per session ever seen. Safe to forget: a
    /// marker that comes back carries a newer timestamp than the one that was forgotten, so it is
    /// announced as the new event it is.</summary>
    private void Forget(IReadOnlyList<StatusMarker> freshMarkers)
    {
        if (_announced.Count == freshMarkers.Count)
        {
            return;
        }

        var live = freshMarkers.Select(m => m.SessionId).ToHashSet(StringComparer.Ordinal);
        foreach (var sessionId in _announced.Keys.Where(id => !live.Contains(id)).ToList())
        {
            _announced.Remove(sessionId);
        }
    }

    /// <summary>Both "blocked on the user" reasons map to one announcement. The hook distinguishes a
    /// permission prompt from an AskUserQuestion/ExitPlanMode confirmation because the dashboard's
    /// marker file is also its badge signal, but spoken aloud the two say the same thing: the session
    /// has stopped and needs an answer. An unrecognized reason is silent rather than guessed at.</summary>
    public static VoiceCue? CueFor(string reason) => reason switch
    {
        StatusMarker.PermissionPromptReason or StatusMarker.AskOrPlanReason => VoiceCue.AwaitingApproval,
        StatusMarker.TurnCompleteReason => VoiceCue.TurnComplete,
        _ => null,
    };
}
