using SmoothCoder.App.Models;
using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class HandoffPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DueHandoff_TargetBecameLimited_ParksUntilFirstReset()
    {
        var plan = HandoffPlanner.Revalidate(AgentKind.ClaudeCode, AgentKind.CodexCli,
            Now.AddHours(2), Now.AddHours(1), null, Now);
        Assert.NotNull(plan);
        Assert.Equal(FailoverAction.WaitForReset, plan.Value.Action);
        Assert.Equal(AgentKind.CodexCli, plan.Value.TargetAgent);
        Assert.Equal(Now.AddHours(1) + HandoffPlanner.ResumeGrace, plan.Value.FireAt);
    }

    [Fact]
    public void DueHandoff_TargetBecameLimited_SourceFree_ResumesSource()
    {
        var plan = HandoffPlanner.Revalidate(AgentKind.ClaudeCode, AgentKind.CodexCli,
            null, Now.AddHours(1), null, Now);
        Assert.Equal(new FailoverPlan(FailoverAction.ResumeSameAgent, AgentKind.ClaudeCode, Now), plan);
    }

    [Fact]
    public void DueResume_LongerLimitDiscovered_PostponesResume()
    {
        var plan = HandoffPlanner.Revalidate(AgentKind.CodexCli, AgentKind.CodexCli,
            Now.AddDays(2), Now.AddDays(2), null, Now);
        Assert.Equal(Now.AddDays(2) + HandoffPlanner.ResumeGrace, plan!.Value.FireAt);
    }

    [Fact]
    public void DueHandoff_TargetStillFree_KeepsOriginalDeadline()
    {
        Assert.Null(HandoffPlanner.Revalidate(AgentKind.ClaudeCode, AgentKind.CodexCli,
            Now.AddHours(1), null, null, Now));
    }

    [Fact]
    public void NoCounterpart_ResumesSameAgentAfterItsOwnReset()
    {
        var resetAt = Now.AddHours(3);

        var plan = HandoffPlanner.Plan(
            AgentKind.ClaudeCode, resetAt,
            counterpart: null, counterpartCooldownUntil: null, lastHandoffAt: null, Now);

        Assert.Equal(FailoverAction.ResumeSameAgent, plan.Action);
        Assert.Equal(AgentKind.ClaudeCode, plan.TargetAgent);
        Assert.Equal(resetAt + HandoffPlanner.ResumeGrace, plan.FireAt);
    }

    [Fact]
    public void CounterpartFree_HandsOffImmediately()
    {
        var plan = HandoffPlanner.Plan(
            AgentKind.ClaudeCode, Now.AddHours(3),
            AgentKind.CodexCli, counterpartCooldownUntil: null, lastHandoffAt: null, Now);

        Assert.Equal(FailoverAction.HandoffToCounterpart, plan.Action);
        Assert.Equal(AgentKind.CodexCli, plan.TargetAgent);
        Assert.Equal(Now + HandoffPlanner.HandoffDelay, plan.FireAt);
    }

    [Fact]
    public void CounterpartCooldownAlreadyExpired_CountsAsFree()
    {
        var plan = HandoffPlanner.Plan(
            AgentKind.CodexCli, Now.AddHours(3),
            AgentKind.ClaudeCode, counterpartCooldownUntil: Now.AddMinutes(-1), lastHandoffAt: null, Now);

        Assert.Equal(FailoverAction.HandoffToCounterpart, plan.Action);
        Assert.Equal(AgentKind.ClaudeCode, plan.TargetAgent);
    }

    [Fact]
    public void BothLimited_WaitsForWhicheverResetsFirst_Counterpart()
    {
        var counterpartReset = Now.AddHours(1);

        var plan = HandoffPlanner.Plan(
            AgentKind.ClaudeCode, Now.AddHours(5),
            AgentKind.CodexCli, counterpartReset, lastHandoffAt: null, Now);

        Assert.Equal(FailoverAction.WaitForReset, plan.Action);
        Assert.Equal(AgentKind.CodexCli, plan.TargetAgent);
        Assert.Equal(counterpartReset + HandoffPlanner.ResumeGrace, plan.FireAt);
    }

    [Fact]
    public void BothLimited_WaitsForWhicheverResetsFirst_ActiveAgent()
    {
        var activeReset = Now.AddHours(1);

        var plan = HandoffPlanner.Plan(
            AgentKind.ClaudeCode, activeReset,
            AgentKind.CodexCli, counterpartCooldownUntil: Now.AddDays(3), lastHandoffAt: null, Now);

        Assert.Equal(FailoverAction.WaitForReset, plan.Action);
        Assert.Equal(AgentKind.ClaudeCode, plan.TargetAgent);
        Assert.Equal(activeReset + HandoffPlanner.ResumeGrace, plan.FireAt);
    }

    [Fact]
    public void HandedOffTooRecently_DoesNotHandOffAgainEvenIfCounterpartLooksFree()
    {
        // The insurance case: the counterpart's limit was never recorded (no usage bridge, no
        // rate_limit line), so the ledger says it is free when it is not.
        var lastHandoff = Now.AddMinutes(-1);

        var plan = HandoffPlanner.Plan(
            AgentKind.CodexCli, Now.AddHours(2),
            AgentKind.ClaudeCode, counterpartCooldownUntil: null, lastHandoffAt: lastHandoff, Now);

        Assert.NotEqual(FailoverAction.HandoffToCounterpart, plan.Action);
        Assert.True(plan.FireAt >= lastHandoff + HandoffPlanner.MinimumDwell);
    }

    [Fact]
    public void HandedOffLongAgo_HandsOffNormally()
    {
        var plan = HandoffPlanner.Plan(
            AgentKind.CodexCli, Now.AddHours(2),
            AgentKind.ClaudeCode, counterpartCooldownUntil: null,
            lastHandoffAt: Now - HandoffPlanner.MinimumDwell, Now);

        Assert.Equal(FailoverAction.HandoffToCounterpart, plan.Action);
    }

    [Fact]
    public void WaitPlan_NeverFiresBeforeTheDwellFloor()
    {
        var lastHandoff = Now.AddMinutes(-2);

        // Both resets are in the very near future, so without the floor the plan would fire back
        // into the counterpart well inside the dwell window.
        var plan = HandoffPlanner.Plan(
            AgentKind.ClaudeCode, Now.AddSeconds(30),
            AgentKind.CodexCli, counterpartCooldownUntil: Now.AddSeconds(10), lastHandoffAt: lastHandoff, Now);

        Assert.Equal(FailoverAction.WaitForReset, plan.Action);
        Assert.Equal(lastHandoff + HandoffPlanner.MinimumDwell, plan.FireAt);
    }

    /// <summary>The regression this whole type exists for. Before the cooldown ledger, a limit on one
    /// agent handed the task to the other unconditionally; if both accounts were exhausted the two
    /// CLIs traded it back and forth forever, relaunching processes without making progress. Walk the
    /// planner through that exact scenario and assert it parks instead.</summary>
    [Fact]
    public void BothAccountsExhausted_ConvergesToWaitingInsteadOfLooping()
    {
        var cooldowns = new Dictionary<AgentKind, DateTimeOffset>
        {
            [AgentKind.ClaudeCode] = Now.AddHours(4),
            [AgentKind.CodexCli] = Now.AddHours(2),
        };

        var active = AgentKind.ClaudeCode;
        var clock = Now;
        DateTimeOffset? lastHandoffAt = null;
        var handoffCount = 0;

        for (var round = 0; round < 10; round++)
        {
            var counterpart = active == AgentKind.ClaudeCode ? AgentKind.CodexCli : AgentKind.ClaudeCode;
            var plan = HandoffPlanner.Plan(
                active, cooldowns[active], counterpart, cooldowns[counterpart], lastHandoffAt, clock);

            if (plan.Action == FailoverAction.HandoffToCounterpart)
            {
                handoffCount++;
                lastHandoffAt = plan.FireAt;
                active = plan.TargetAgent;
                // The agent it moved to reports its own (already recorded) limit seconds later.
                clock = plan.FireAt.AddSeconds(10);
                continue;
            }

            Assert.Equal(FailoverAction.WaitForReset, plan.Action);
            // Parked on the sooner of the two resets, which is the point: keep using both accounts to
            // the limit, then come back the moment either one frees up.
            Assert.Equal(AgentKind.CodexCli, plan.TargetAgent);
            Assert.Equal(cooldowns[AgentKind.CodexCli] + HandoffPlanner.ResumeGrace, plan.FireAt);
            Assert.Equal(0, handoffCount);
            return;
        }

        Assert.Fail("Planner kept moving the task between exhausted accounts instead of parking it.");
    }
}
