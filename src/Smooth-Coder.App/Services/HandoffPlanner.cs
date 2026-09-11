using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>What a session should do once the agent it is running on has hit its account limit.
/// <see cref="FailoverPlan.TargetAgent"/> is what decides resume-vs-handoff (equal to the agent that
/// was running means resume it, different means switch); this enum only records <em>why</em>, which
/// the dashboard badge and the voice cue need to tell "carrying on elsewhere" apart from "parked
/// because there is nowhere left to go".</summary>
public enum FailoverAction
{
    /// <summary>Wait out this agent's own reset and continue on it. Either cross-agent handoff is
    /// turned off, or there is no counterpart for this agent.</summary>
    ResumeSameAgent,

    /// <summary>The counterpart account still has capacity - move the task there now.</summary>
    HandoffToCounterpart,

    /// <summary>Both accounts are exhausted. Park until whichever frees up first, then continue on
    /// that one.</summary>
    WaitForReset,
}

/// <summary>Where a rate-limited task goes next. <see cref="TargetAgent"/> is always the agent to
/// launch at <see cref="FireAt"/>, whether that is the same one that was running or the other.</summary>
public readonly record struct FailoverPlan(FailoverAction Action, AgentKind TargetAgent, DateTimeOffset FireAt);

/// <summary>Chooses where a task continues after its agent hit an account limit. Pure and clock-free
/// (every time is a parameter), like <see cref="ScheduleEvaluator"/> and <see cref="UsageWindowEvaluator"/>,
/// because this runs unattended and getting it wrong does not merely produce a wrong number - it
/// relaunches processes in a loop.
///
/// <para>The failure this exists to prevent: <see cref="SharedTaskContextService.GetCounterpart"/>
/// returns the other CLI unconditionally, so before this type, a limit on Claude Code handed the task
/// to Codex even when the Codex account was <em>also</em> exhausted. Codex would report its own limit
/// within seconds, hand back, and the two would trade the task forever without making progress. So
/// the counterpart's own cooldown (see <see cref="AgentCooldownStore"/>) is an input here, and when
/// neither account can take the work the plan parks the task rather than moving it.</para></summary>
public static class HandoffPlanner
{
    /// <summary>Delay added after a reset time before relaunching, so a launch can't land fractionally
    /// before the account actually frees up (clock skew between the CLI's reported reset and this
    /// machine) and immediately re-hit the limit.</summary>
    public static readonly TimeSpan ResumeGrace = TimeSpan.FromMinutes(5);

    /// <summary>How long to wait before starting the counterpart. Short on purpose - the whole point
    /// of a handoff is that the work continues now instead of at the reset time - but not zero, so the
    /// limited CLI's process has a moment to be stopped first.</summary>
    public static readonly TimeSpan HandoffDelay = TimeSpan.FromSeconds(5);

    /// <summary>How long a task must stay on an agent before it may be handed away again. This is
    /// insurance for the case the cooldown ledger cannot cover: an account whose limit was never
    /// recorded in machine-readable form (Claude Code's usage bridge is off and no `rate_limit` line
    /// was captured for that run) looks available, so without a floor on handoff frequency the two
    /// CLIs could still trade the task back and forth. A task that genuinely burns through an account
    /// in under this window is not making progress anyway.</summary>
    public static readonly TimeSpan MinimumDwell = TimeSpan.FromMinutes(10);

    /// <param name="activeAgent">The agent that was running when the limit was hit.</param>
    /// <param name="activeResetAt">When <paramref name="activeAgent"/>'s account frees up, as reported
    /// by its own transcript.</param>
    /// <param name="counterpart">The agent to fail over to, or null when cross-agent handoff is turned
    /// off or <paramref name="activeAgent"/> has no counterpart.</param>
    /// <param name="counterpartCooldownUntil">When <paramref name="counterpart"/>'s account frees up,
    /// or null if it is not known to be limited.</param>
    /// <param name="lastHandoffAt">When this task last moved between agents, for
    /// <see cref="MinimumDwell"/>. Null if it never has.</param>
    public static FailoverPlan Plan(
        AgentKind activeAgent,
        DateTimeOffset activeResetAt,
        AgentKind? counterpart,
        DateTimeOffset? counterpartCooldownUntil,
        DateTimeOffset? lastHandoffAt,
        DateTimeOffset now)
    {
        if (counterpart is not { } target)
        {
            return new FailoverPlan(FailoverAction.ResumeSameAgent, activeAgent, activeResetAt + ResumeGrace);
        }

        var counterpartFree = counterpartCooldownUntil is not { } until || until <= now;
        var dwellSatisfied = lastHandoffAt is not { } last || now - last >= MinimumDwell;

        if (counterpartFree && dwellSatisfied)
        {
            return new FailoverPlan(FailoverAction.HandoffToCounterpart, target, now + HandoffDelay);
        }

        // Neither account can take the work right now (or the task has bounced too recently to trust
        // that the counterpart really is free). Park until the first one frees up, and come back on
        // whichever that is - the point of the wait is to use both accounts to their limit, not to
        // sit on one agent's reset while the other's has already passed.
        var activeFireAt = activeResetAt + ResumeGrace;
        var counterpartFireAt = (counterpartCooldownUntil ?? now) + ResumeGrace;

        var (winner, fireAt) = counterpartFireAt < activeFireAt
            ? (target, counterpartFireAt)
            : (activeAgent, activeFireAt);

        // Never come back before the dwell floor has elapsed, so a plan chosen while the counterpart
        // looked free-but-too-recent can't fire straight back into the thrash it was avoiding.
        if (lastHandoffAt is { } handedOffAt && fireAt < handedOffAt + MinimumDwell)
        {
            fireAt = handedOffAt + MinimumDwell;
        }

        return new FailoverPlan(FailoverAction.WaitForReset, winner, fireAt);
    }
}
