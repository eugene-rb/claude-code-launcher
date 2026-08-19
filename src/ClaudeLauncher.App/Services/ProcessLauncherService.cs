using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>
/// Launches a session profile in a new, independent PowerShell console window and manages
/// its lifetime. Script construction and encoding are pure functions (no process I/O) so
/// they can be unit tested directly.
/// </summary>
public sealed class ProcessLauncherService
{
    /// <summary>
    /// Builds the PowerShell script text that sets the console window title and invokes the
    /// configured executable with its arguments via the call operator. Every dynamic value is
    /// emitted as a single-quoted PowerShell string literal (embedded quotes doubled) so the
    /// script is safe to run regardless of the characters in the session name, executable, or
    /// arguments.
    /// </summary>
    public static string BuildScript(string sessionName, string executable, IReadOnlyList<string> arguments)
    {
        var sb = new StringBuilder();
        sb.Append("$Host.UI.RawUI.WindowTitle = ").Append(ToPowerShellLiteral(sessionName)).Append(';');
        sb.Append(" & ").Append(ToPowerShellLiteral(executable));

        foreach (var arg in arguments)
        {
            sb.Append(' ').Append(ToPowerShellLiteral(arg));
        }

        return sb.ToString();
    }

    /// <summary>Base64-encodes a script for `powershell.exe -EncodedCommand`, which expects UTF-16LE bytes.</summary>
    public static string EncodeCommand(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string ToPowerShellLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>Tokenizes the profile's configured arguments and, when resuming, appends `-c` to
    /// continue the most recent conversation in the working directory (no session ID needed, unlike
    /// `-r/--resume` which opens an interactive picker when given no value and would hang unattended).
    /// Any existing `-r`/`--resume`/`-c`/`--continue` already in the profile's own arguments (e.g. a
    /// user-configured `--resume`) is stripped first so the two can't collide on the same command
    /// line - a bare trailing `--resume` with no session ID would otherwise still open that picker
    /// and hang the unattended resume.</summary>
    public static IReadOnlyList<string> BuildLaunchArguments(string profileArguments, bool resume)
    {
        var arguments = CommandLineTokenizer.Tokenize(profileArguments);
        if (!resume)
        {
            return arguments;
        }

        var filtered = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];
            if (token is "-r" or "--resume" or "-c" or "--continue")
            {
                // -r/--resume optionally takes a session-ID value; drop it too so it isn't left
                // behind as a stray positional prompt argument.
                if (token is "-r" or "--resume" && i + 1 < arguments.Count && !arguments[i + 1].StartsWith('-'))
                {
                    i++;
                }

                continue;
            }

            filtered.Add(token);
        }

        filtered.Add("-c");
        return filtered;
    }

    /// <summary>Starts the session's PowerShell window. The caller owns the returned process (keep a
    /// reference alive and subscribe to <see cref="Process.Exited"/> as needed). Pass
    /// <paramref name="resume"/> to continue the most recent conversation instead of a fresh one.</summary>
    public Process Start(SessionProfile profile, bool resume = false)
    {
        var arguments = BuildLaunchArguments(profile.Arguments, resume);
        var script = BuildScript(profile.Name, profile.Executable, arguments);
        var encoded = EncodeCommand(script);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = profile.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        startInfo.ArgumentList.Add("-NoExit");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encoded);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        return process;
    }

    /// <summary>Kills the session's process tree and waits (bounded) for it to actually exit before
    /// returning, so callers that immediately relaunch (e.g. an auto-resume's `-c`) don't race a
    /// still-terminating process for the working directory's files. Returns false (without throwing)
    /// if the process had already exited on its own before this call.</summary>
    public bool Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
            return true;
        }
        catch (InvalidOperationException)
        {
            // Already exited.
            return false;
        }
        catch (Win32Exception)
        {
            // Exiting concurrently with the kill attempt; treat as already stopped.
            return false;
        }
    }
}
