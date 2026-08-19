using System.Text;
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
    public void EncodeCommand_RoundTrips_AsUtf16LeBase64()
    {
        const string script = "Write-Host 'こんにちは'";

        var encoded = ProcessLauncherService.EncodeCommand(script);
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));

        Assert.Equal(script, decoded);
    }
}
