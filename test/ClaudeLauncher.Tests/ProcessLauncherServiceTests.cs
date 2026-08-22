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
        var arguments = ProcessLauncherService.BuildLaunchArguments("--model sonnet", resume: false);

        Assert.Equal(["--model", "sonnet"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_AppendsContinueFlag()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments("--model sonnet", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_WithNoOtherArguments_IsJustContinueFlag()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments("", resume: true);

        Assert.Equal(["-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_StripsExistingBareResumeFlag()
    {
        // A bare trailing --resume (no session ID) opens an interactive picker and would hang an
        // unattended auto-resume launch, so it must not survive alongside the appended -c.
        var arguments = ProcessLauncherService.BuildLaunchArguments("--resume --model sonnet", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_StripsExistingResumeFlagWithSessionIdValue()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments("-r abc123 --model sonnet", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_DoesNotDuplicateExistingContinueFlag()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments("-c --model sonnet", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_Resuming_TrailingResumeWithNoValue_IsRemovedCleanly()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments("--model sonnet --resume", resume: true);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_ResumingWithCompactFirst_AppendsCompactAsThePositionalPrompt()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(
            "--model sonnet", resume: true, ResumeMode.CompactFirst);

        // The prompt argument has to trail the flags: the CLI is `claude [options] [prompt]`.
        Assert.Equal(["--model", "sonnet", "-c", "/compact"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_ResumingWithFullSession_LeavesTheConversationUncompacted()
    {
        var arguments = ProcessLauncherService.BuildLaunchArguments(
            "--model sonnet", resume: true, ResumeMode.FullSession);

        Assert.Equal(["--model", "sonnet", "-c"], arguments);
    }

    [Fact]
    public void BuildLaunchArguments_NotResuming_IgnoresResumeModeEntirely()
    {
        // A fresh session has no conversation to compact, so /compact must not leak into it.
        var arguments = ProcessLauncherService.BuildLaunchArguments(
            "--model sonnet", resume: false, ResumeMode.CompactFirst);

        Assert.Equal(["--model", "sonnet"], arguments);
    }

    [Fact]
    public void BuildResumeEnvironment_NotResuming_IsEmptySoAttendedLaunchesAreUntouched()
    {
        var environment = ProcessLauncherService.BuildResumeEnvironment(resume: false);

        Assert.Empty(environment);
    }

    [Fact]
    public void BuildResumeEnvironment_Resuming_RaisesBothResumeChooserThresholds()
    {
        // Claude Code's "Resume from summary?" chooser blocks on a keypress and would hang an
        // unattended auto-resume; it is skipped when the session is under both thresholds.
        var environment = ProcessLauncherService.BuildResumeEnvironment(resume: true);

        Assert.Equal("525600", environment["CLAUDE_CODE_RESUME_THRESHOLD_MINUTES"]);
        Assert.Equal("999999999", environment["CLAUDE_CODE_RESUME_TOKEN_THRESHOLD"]);
    }

    [Fact]
    public void BuildResumeEnvironment_Resuming_ThresholdsParseAsPlainIntegers()
    {
        // The CLI parses both variables with a plain integer parser and ignores anything that is not
        // finite, which would silently bring the chooser back.
        foreach (var value in ProcessLauncherService.BuildResumeEnvironment(resume: true).Values)
        {
            Assert.True(int.TryParse(value, out var parsed));
            Assert.True(parsed > 0);
        }
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
