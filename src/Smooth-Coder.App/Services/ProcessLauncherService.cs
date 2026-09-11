using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

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

    /// <summary>Tokenizes the profile's configured arguments and, when resuming, composes
    /// <paramref name="agent"/>'s <see cref="AgentDefinition.ResumeArgumentTemplate"/> around them (a
    /// trailing continuation flag for most CLIs, a leading subcommand for Codex's `resume --last`).
    /// Any flag in <see cref="AgentDefinition.ResumeFlagsToStrip"/> /
    /// <see cref="AgentDefinition.ResumeFlagsToStripWithOptionalValue"/> already in the profile's own
    /// arguments (e.g. a user-configured `--resume`) is stripped first so it can't collide with the
    /// template - a bare trailing `--resume` with no session ID would otherwise still open an
    /// interactive picker and hang an unattended resume.
    /// <para><see cref="ResumeMode.CompactFirst"/> additionally appends
    /// <see cref="AgentDefinition.CompactResumeExtraToken"/> (Claude Code's `/compact`) as the CLI's
    /// positional prompt argument, which the resumed session runs as its first input. Agents with no
    /// such token (everyone but Claude Code) ignore this mode entirely.</para></summary>
    public static IReadOnlyList<string> BuildLaunchArguments(
        AgentDefinition agent,
        string profileArguments,
        bool resume,
        ResumeMode resumeMode = ResumeMode.FullSession,
        string? initialPrompt = null)
    {
        var arguments = CommandLineTokenizer.Tokenize(profileArguments);
        if (!resume)
        {
            return string.IsNullOrWhiteSpace(initialPrompt) ? arguments : [.. arguments, initialPrompt];
        }

        var filtered = StripResumeFlags(arguments, agent);
        var composed = ComposeResumeTemplate(agent.ResumeArgumentTemplate, filtered);

        // Both of these occupy the CLI's single positional prompt slot, so only one can be appended.
        // `/compact` wins: it is the mode the user explicitly chose, and the resumed session is nudged
        // back into the task afterwards (see SessionItemViewModel.ScheduleResumeNudge). Appending both
        // would hand the CLI two positional arguments, where the second is either ignored or rejected.
        if (resumeMode == ResumeMode.CompactFirst && agent.CompactResumeExtraToken is { } compactToken)
        {
            return [.. composed, compactToken];
        }

        if (!string.IsNullOrWhiteSpace(initialPrompt))
        {
            composed = [.. composed, initialPrompt];
        }

        return composed;
    }

    private static List<string> StripResumeFlags(IReadOnlyList<string> arguments, AgentDefinition agent)
    {
        var filtered = new List<string>();
        for (var i = 0; i < arguments.Count; i++)
        {
            var token = arguments[i];

            if (agent.ResumeFlagsToStripWithOptionalValue.Contains(token))
            {
                // May optionally take a value (e.g. a session ID); drop it too so it isn't left
                // behind as a stray positional prompt argument.
                if (i + 1 < arguments.Count && !arguments[i + 1].StartsWith('-'))
                {
                    i++;
                }

                continue;
            }

            if (agent.ResumeFlagsToStrip.Contains(token))
            {
                continue;
            }

            filtered.Add(token);
        }

        return filtered;
    }

    /// <summary>Splices <paramref name="filteredArgs"/> into <paramref name="template"/> at its single
    /// <c>"{args}"</c> placeholder entry, leaving every other template token as a literal.</summary>
    private static List<string> ComposeResumeTemplate(IReadOnlyList<string> template, IReadOnlyList<string> filteredArgs)
    {
        var result = new List<string>();
        foreach (var token in template)
        {
            if (token == "{args}")
            {
                result.AddRange(filteredArgs);
            }
            else
            {
                result.Add(token);
            }
        }

        return result;
    }

    /// <summary>Returns <paramref name="agent"/>'s <see cref="AgentDefinition.ResumeEnvironmentVariables"/>
    /// when resuming, empty otherwise. Claude Code shows a blocking "This session is Xh Ym old and N
    /// tokens / Resume from summary?" chooser before it will continue an old, large conversation; that
    /// chooser waits for a keypress, so an unattended auto-resume just sits on it and never reaches the
    /// CLI. Claude Code skips the chooser entirely when the session is below both of its threshold env
    /// vars, so a resume launch runs with them raised out of reach - a year of wall-clock and a token
    /// count no transcript reaches - taking the "resume the full session as-is" branch, what plain `-c`
    /// did before the chooser existed. No other agent is known to have an equivalent chooser, so their
    /// definitions carry no variables here. Values are set only for the launched process (never
    /// machine-wide) and only when resuming, so a fresh launch is untouched.</summary>
    public static IReadOnlyDictionary<string, string> BuildResumeEnvironment(AgentDefinition agent, bool resume) =>
        resume ? new Dictionary<string, string>(agent.ResumeEnvironmentVariables) : new Dictionary<string, string>();

    /// <summary>Starts the session's PowerShell window. The caller owns the returned process (keep a
    /// reference alive and subscribe to <see cref="Process.Exited"/> as needed). <paramref name="executable"/>
    /// and <paramref name="argumentsText"/> are the per-agent default from <see cref="Models.AppSettings"/>
    /// unless the caller is launching with a one-off override; <paramref name="agentKind"/> selects
    /// which <see cref="AgentDefinition"/> (see <see cref="AgentCatalog"/>) governs resume syntax. Pass
    /// <paramref name="resume"/> to continue the most recent conversation instead of a fresh one, and
    /// <paramref name="resumeMode"/> to say whether that conversation carries on in full or from a
    /// summary (Claude Code only - every other agent ignores this mode).</summary>
    public Process Start(
        SessionProfile profile,
        AgentKind agentKind,
        string executable,
        string argumentsText,
        bool resume = false,
        ResumeMode resumeMode = ResumeMode.FullSession,
        string? initialPrompt = null)
    {
        var agent = AgentCatalog.Get(agentKind);
        var arguments = BuildLaunchArguments(agent, argumentsText, resume, resumeMode, initialPrompt);
        var script = BuildScript(profile.Name, executable, arguments);
        var encoded = EncodeCommand(script);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = profile.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        foreach (var (name, value) in BuildResumeEnvironment(agent, resume))
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
