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

    /// <summary>Starts the session's PowerShell window. The caller owns the returned process (keep a
    /// reference alive and subscribe to <see cref="Process.Exited"/> as needed).</summary>
    public Process Start(SessionProfile profile)
    {
        var arguments = CommandLineTokenizer.Tokenize(profile.Arguments);
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

    /// <summary>Kills the session's process tree. Returns false (without throwing) if the process had
    /// already exited on its own before this call.</summary>
    public bool Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
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
