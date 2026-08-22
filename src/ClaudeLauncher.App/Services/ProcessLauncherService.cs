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
    /// and hang the unattended resume.
    /// <para><see cref="ResumeMode.CompactFirst"/> additionally appends `/compact` as the CLI's
    /// positional prompt argument, which the resumed session runs as its first input - the same
    /// compaction the chooser's recommended branch performs. A profile that already supplies its own
    /// positional prompt would end up with two, so the setting is documented as applying to the
    /// launcher's own resume rather than to arbitrary custom arguments.</para></summary>
    public static IReadOnlyList<string> BuildLaunchArguments(
        string profileArguments,
        bool resume,
        ResumeMode resumeMode = ResumeMode.FullSession)
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
        if (resumeMode == ResumeMode.CompactFirst)
        {
            filtered.Add("/compact");
        }

        return filtered;
    }

    /// <summary>Claude Code shows a blocking "This session is Xh Ym old and N tokens / Resume from
    /// summary?" chooser before it will continue an old, large conversation. That chooser waits for a
    /// keypress, so an unattended auto-resume just sits on it and never reaches the CLI. The CLI skips
    /// the chooser entirely when the session is below both of these thresholds, so a resume launch runs
    /// with them raised out of reach - a year of wall-clock and a token count no transcript reaches.
    /// The effect is that the auto-resume takes the "resume the full session as-is" branch, which is
    /// what plain `-c` did before the chooser existed. Values are set only for the launched process
    /// (never machine-wide) and only when resuming, so a fresh launch is untouched. Which branch of the
    /// chooser the launcher then takes on the user's behalf is <see cref="ResumeMode"/>'s job.
    /// Both variables are parsed as plain integers by the CLI, so keep them well inside int range.</summary>
    public static IReadOnlyDictionary<string, string> BuildResumeEnvironment(bool resume)
    {
        if (!resume)
        {
            return new Dictionary<string, string>();
        }

        return new Dictionary<string, string>
        {
            ["CLAUDE_CODE_RESUME_THRESHOLD_MINUTES"] = "525600",
            ["CLAUDE_CODE_RESUME_TOKEN_THRESHOLD"] = "999999999",
        };
    }

    /// <summary>Starts the session's PowerShell window. The caller owns the returned process (keep a
    /// reference alive and subscribe to <see cref="Process.Exited"/> as needed). <paramref name="executable"/>
    /// and <paramref name="argumentsText"/> are the app-wide default from <see cref="Models.AppSettings"/>
    /// unless the caller is launching with a one-off override. Pass <paramref name="resume"/> to
    /// continue the most recent conversation instead of a fresh one, and <paramref name="resumeMode"/>
    /// to say whether that conversation carries on in full or from a summary.</summary>
    public Process Start(
        SessionProfile profile,
        string executable,
        string argumentsText,
        bool resume = false,
        ResumeMode resumeMode = ResumeMode.FullSession)
    {
        var arguments = BuildLaunchArguments(argumentsText, resume, resumeMode);
        var script = BuildScript(profile.Name, executable, arguments);
        var encoded = EncodeCommand(script);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = profile.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        foreach (var (name, value) in BuildResumeEnvironment(resume))
        {
            startInfo.Environment[name] = value;
        }

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
