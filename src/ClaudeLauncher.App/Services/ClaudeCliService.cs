using System.Diagnostics;
using System.Text;

namespace ClaudeLauncher.App.Services;

/// <summary>Result of a `claude` CLI invocation. Exit codes for `claude plugin` subcommands are
/// undocumented, so callers must not infer success/failure beyond what they show the user
/// verbatim. <see cref="TimedOut"/> means the process was killed before it finished - treat this
/// as "unknown state", never as "failed", since the command may have partially applied.</summary>
public sealed record CliResult(bool TimedOut, int? ExitCode, string StdOut, string StdErr);

/// <summary>Runs `claude` CLI commands hidden (no visible window), captures output, and enforces a
/// timeout. Distinct from <see cref="ProcessLauncherService"/>, which launches a visible,
/// long-running interactive session window instead.</summary>
public sealed class ClaudeCliService
{
    public async Task<CliResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "claude",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new CliResult(TimedOut: false, ExitCode: null, StdOut: string.Empty, StdErr: ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            return new CliResult(TimedOut: true, ExitCode: null, stdOut.ToString(), stdErr.ToString());
        }

        return new CliResult(TimedOut: false, process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout firing and the kill attempt.
        }
    }
}
