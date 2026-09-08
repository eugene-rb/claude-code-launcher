using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

/// <summary>The launcher is the only thing that speaks now that the Claude Code hook's own sounds are
/// retired, and the dashboard re-reads the marker directory every two seconds against markers that
/// live for ten minutes. So "announce each event exactly once" is the whole contract here: get it
/// wrong in one direction and the announcement repeats 300 times, in the other and it never comes.</summary>
public class AgentEventNotifierTests
{
    private static StatusMarker Marker(string sessionId, string reason, DateTimeOffset updatedAt) =>
        new(sessionId, @"D:\Dev\Sample", reason, updatedAt);

    private static (AgentEventNotifier Notifier, List<VoiceCue> Spoken) Create()
    {
        List<VoiceCue> spoken = [];
        return (new AgentEventNotifier(spoken.Add), spoken);
    }

    [Fact]
    public void FirstObserve_OnlySeeds()
    {
        var (notifier, spoken) = Create();

        notifier.Observe([Marker("s1", StatusMarker.TurnCompleteReason, DateTimeOffset.Now)]);

        // A launcher started mid-session would otherwise open by announcing a backlog of events the
        // user has already dealt with.
        Assert.Empty(spoken);
    }

    [Fact]
    public void NewMarkerAfterSeeding_IsAnnouncedOnce()
    {
        var (notifier, spoken) = Create();
        var now = DateTimeOffset.Now;
        notifier.Observe([]);

        var marker = Marker("s1", StatusMarker.TurnCompleteReason, now);
        notifier.Observe([marker]);
        notifier.Observe([marker]);
        notifier.Observe([marker]);

        Assert.Equal([VoiceCue.TurnComplete], spoken);
    }

    [Fact]
    public void SameSessionWithANewerTimestamp_IsANewEvent()
    {
        var (notifier, spoken) = Create();
        var now = DateTimeOffset.Now;
        notifier.Observe([]);

        notifier.Observe([Marker("s1", StatusMarker.TurnCompleteReason, now)]);
        notifier.Observe([Marker("s1", StatusMarker.TurnCompleteReason, now.AddSeconds(30))]);

        Assert.Equal([VoiceCue.TurnComplete, VoiceCue.TurnComplete], spoken);
    }

    [Fact]
    public void SameSessionChangingReason_IsANewEvent()
    {
        var (notifier, spoken) = Create();
        var now = DateTimeOffset.Now;
        notifier.Observe([]);

        notifier.Observe([Marker("s1", StatusMarker.PermissionPromptReason, now)]);
        notifier.Observe([Marker("s1", StatusMarker.TurnCompleteReason, now.AddSeconds(5))]);

        Assert.Equal([VoiceCue.AwaitingApproval, VoiceCue.TurnComplete], spoken);
    }

    [Fact]
    public void MarkerClearedThenWrittenAgain_IsAnnouncedAgain()
    {
        var (notifier, spoken) = Create();
        var now = DateTimeOffset.Now;
        notifier.Observe([]);

        notifier.Observe([Marker("s1", StatusMarker.PermissionPromptReason, now)]);
        // PostToolUse clears the marker between prompts; the next prompt writes a fresh one.
        notifier.Observe([]);
        notifier.Observe([Marker("s1", StatusMarker.PermissionPromptReason, now.AddSeconds(20))]);

        Assert.Equal([VoiceCue.AwaitingApproval, VoiceCue.AwaitingApproval], spoken);
    }

    [Fact]
    public void EachSessionIsTrackedSeparately()
    {
        var (notifier, spoken) = Create();
        var now = DateTimeOffset.Now;
        notifier.Observe([]);

        notifier.Observe([Marker("s1", StatusMarker.TurnCompleteReason, now)]);
        notifier.Observe([Marker("s1", StatusMarker.TurnCompleteReason, now), Marker("s2", StatusMarker.TurnCompleteReason, now)]);

        Assert.Equal([VoiceCue.TurnComplete, VoiceCue.TurnComplete], spoken);
    }

    [Theory]
    [InlineData(StatusMarker.PermissionPromptReason, VoiceCue.AwaitingApproval)]
    [InlineData(StatusMarker.AskOrPlanReason, VoiceCue.AwaitingApproval)]
    [InlineData(StatusMarker.TurnCompleteReason, VoiceCue.TurnComplete)]
    public void CueFor_MapsEveryReasonTheHookWrites(string reason, VoiceCue expected)
    {
        Assert.Equal(expected, AgentEventNotifier.CueFor(reason));
    }

    [Fact]
    public void CueFor_UnknownReason_IsSilentRatherThanGuessed()
    {
        Assert.Null(AgentEventNotifier.CueFor("some_future_hook_reason"));
    }

    [Fact]
    public void UnknownReason_DoesNotSuppressTheNextRealEvent()
    {
        var (notifier, spoken) = Create();
        var now = DateTimeOffset.Now;
        notifier.Observe([]);

        notifier.Observe([Marker("s1", "some_future_hook_reason", now)]);
        notifier.Observe([Marker("s1", StatusMarker.TurnCompleteReason, now.AddSeconds(1))]);

        Assert.Equal([VoiceCue.TurnComplete], spoken);
    }
}
