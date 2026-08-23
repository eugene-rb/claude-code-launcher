using System.Text;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class ProcessLauncherServiceTests
{
    [Fact]
    public void BuildScript_NoArguments_InvokesExecutableAlone()
    {
        var script = ProcessLauncherService.BuildScript("My Session", "claude", []);

        Assert.Equal("$Host.UI.RawUI.WindowTitle = 'My Session'; & 'claude'", script);
    }

    [Fact]
    public void BuildScript_WithArguments_QuotesEachTokenSeparately()
    {
        var script = ProcessLauncherService.BuildScript("Session", "claude", ["--resume", "--model", "sonnet"]);

        Assert.Equal("$Host.UI.RawUI.WindowTitle = 'Session'; & 'claude' '--resume' '--model' 'sonnet'", script);
    }

    [Fact]
    public void BuildScript_EmbeddedSingleQuote_IsDoubledToEscapeSafely()
    {
        var script = ProcessLauncherService.BuildScript("It's Mine", "claude", ["--path", "C:\\It's a path"]);

        Assert.Equal(
            "$Host.UI.RawUI.WindowTitle = 'It''s Mine'; & 'claude' '--path' 'C:\\It''s a path'",
            script);
    }

    [Fact]
    public void BuildLaunchArguments_NotResuming_ReturnsTokenizedArgumentsUnchanged()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.ClaudeCode, "--model sonnet", resume: false);

        Assert.Equal(["--model", "sonnet"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_AppendsContinueFlag()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.ClaudeCode, "--model sonnet", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_WithNoOtherArguments_IsJustContinueFlag()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.ClaudeCode, "", resume: true);

        Assert.Equal(["-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_StripsExistingBareResumeFlag()
    {
        // A bare trailing --resume (no session ID) opens an interactive picker and would hang an
        // unattended auto-resume launch, so it must not survive alongside the appended -c.
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.ClaudeCode, "--resume --model sonnet", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_StripsExistingResumeFlagWithSessionIdValue()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.ClaudeCode, "-r abc123 --model sonnet", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_DoesNotDuplicateExistingContinueFlag()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.ClaudeCode, "-c --model sonnet", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_TrailingResumeWithNoValue_IsRemovedCleanly()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.ClaudeCode, "--model sonnet --resume", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_ResumingWithCompactFirst_AppendsCompactAsThePositionalPrompt()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(
            AgentCatalog.ClaudeCode, "--model sonnet", resume: true, ResumeMode.CompactFirst);

        // The prompt argument has to trail the flags: the CLI is `claude [options] [prompt]`.
        Assert.Equal(["--model", "sonnet", "-c", "/compact"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_ResumingWithFullSession_LeavesTheConversationUncompacted()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(
            AgentCatalog.ClaudeCode, "--model sonnet", resume: true, ResumeMode.FullSession);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_NotResuming_IgnoresResumeModeEntirely()
    {
        // A fresh session has no conversation to compact, so /compact must not leak into it.
        var arguments = ProcessLauncherService.BuildLaunchArguments(
            AgentCatalog.ClaudeCode, "--model sonnet", resume: false, ResumeMode.CompactFirst);

        Assert.Equal(["--model", "sonnet"], arguments);
    }

    [Fact]
    public void BuildResumeEnvironment_NotResuming_IsEmptySoAttendedLaunchesAreUntouched()
    {
        var environment = ProcessLauncherService.BuildResumeEnvironment(AgentCatalog.ClaudeCode, resume: false);

        Assert.Empty(environment);
    }

    [Fact]
    public void BuildResumeEnvironment_Resuming_RaisesBothResumeChooserThresholds()
    {
        // Claude Code's "Resume from summary?" chooser blocks on a keypress and would hang an
        // unattended auto-resume; it is skipped when the session is under both thresholds.
        var environment = ProcessLauncherService.BuildResumeEnvironment(AgentCatalog.ClaudeCode, resume: true);

        Assert.Equal("525600", environment["CLAUDE_CODE_RESUME_THRESHOLD_MINUTES"]);
        Assert.Equal("999999999", environment["CLAUDE_CODE_RESUME_TOKEN_THRESHOLD"]);
    }

    [Fact]
    public void BuildResumeEnvironment_Resuming_ThresholdsParseAsPlainIntegers()
    {
        // The CLI parses both variables with a plain integer parser and ignores anything that is not
        // finite, which would silently bring the chooser back.
        foreach (var value in ProcessLauncherService.BuildResumeEnvironment(AgentCatalog.ClaudeCode, resume: true).Values)
        {
            Assert.True(int.TryParse(value, out var parsed));
            Assert.True(parsed > 0);
        }
    }

    [Fact]
    public void BuildLaunchArguments_CodexResuming_PrefixesResumeLastSubcommand()
    {
        // codex resume is a distinct subcommand, not a flag appended after the profile's own
        // arguments - the template must prefix rather than append.
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.CodexCli, "--model o3", resume: true);

        Assert.Equal(["resume", "--last", "--model", "o3"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_CodexNotResuming_ReturnsTokenizedArgumentsUnchanged()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.CodexCli, "--model o3", resume: false);

        Assert.Equal(["--model", "o3"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_KimiResuming_AppendsContinueFlag()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.KimiCodeCli, "", resume: true);

        Assert.Equal(["--continue"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_KimiResuming_StripsExistingSessionFlagWithValue()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.KimiCodeCli, "--session abc123", resume: true);

        Assert.Equal(["--continue"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_AntigravityResuming_AppendsContinueFlag()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(AgentCatalog.AntigravityCli, "", resume: true);

        Assert.Equal(["--continue"], arguments);
    }

    [Fact]
    public void BuildResumeEnvironment_NonClaudeAgent_IsAlwaysEmpty()
    {
        // No other agent has a verified equivalent of Claude Code's resume-chooser thresholds, so
        // their definitions must never inject environment variables that could silently do nothing.
        var environment = ProcessLauncherService.BuildResumeEnvironment(AgentCatalog.CodexCli, resume: true);

        Assert.Empty(environment);
    }

    [Fact]
    public void EncodeCommand_RoundTrips_AsUtf16LeBase64()
    {
        const string script = "Write-Host 'こんにちは'";

        var encoded = ProcessLauncherService.EncodeCommand(script);
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));

        Assert.Equal(script, decoded);
    }
}
