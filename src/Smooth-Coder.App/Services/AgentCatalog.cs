using System.IO;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>The single place every AI-CLI-specific fact lives (binary name, resume syntax, instruction
/// file, config directory). Everything outside this file treats a CLI's behavior as data resolved
/// through here, so correcting a wrong fact about codex-cli/kimi-code-cli/antigravity-cli is a one-line
/// edit rather than a change scattered across the launcher.
///
/// Facts below were gathered from each project's public documentation (no real transcripts to verify
/// against, unlike Claude Code's classifier - see <see cref="CodexTranscriptSource"/> etc. for the
/// caveats that follow from that). Executable name and resume arguments are also exposed as editable
/// text in the 設定 tab (see <see cref="Models.AgentExecutionSettings"/>), so a wrong guess here costs
/// the user a text-box edit, not a rebuild.</summary>
public static class AgentCatalog
{
    public static readonly AgentDefinition ClaudeCode = new(
        Kind: AgentKind.ClaudeCode,
        DisplayName: "Claude Code",
        DefaultExecutable: "claude",
        DefaultArguments: "",
        ResumeArgumentTemplate: ["{args}", "-c"],
        ResumeFlagsToStrip: ["-c", "--continue"],
        ResumeFlagsToStripWithOptionalValue: ["-r", "--resume"],
        CompactResumeExtraToken: "/compact",
        ResumeEnvironmentVariables: new Dictionary<string, string>
        {
            ["CLAUDE_CODE_RESUME_THRESHOLD_MINUTES"] = "525600",
            ["CLAUDE_CODE_RESUME_TOKEN_THRESHOLD"] = "999999999",
        },
        ProjectInstructionFileName: "CLAUDE.md",
        UserConfigDirectoryRelativeToHome: ".claude",
        SupportsUsageLimitAutoResume: true);

    /// <summary>codex resume is a distinct subcommand (`codex resume --last`), not a flag appended to
    /// `codex`'s normal arguments - hence the prefix-shaped template instead of a trailing flag.</summary>
    public static readonly AgentDefinition CodexCli = new(
        Kind: AgentKind.CodexCli,
        DisplayName: "Codex CLI",
        DefaultExecutable: "codex",
        DefaultArguments: "",
        ResumeArgumentTemplate: ["resume", "--last", "{args}"],
        ResumeFlagsToStrip: [],
        ResumeFlagsToStripWithOptionalValue: [],
        CompactResumeExtraToken: null,
        ResumeEnvironmentVariables: new Dictionary<string, string>(),
        ProjectInstructionFileName: "AGENTS.md",
        UserConfigDirectoryRelativeToHome: ".codex",
        SupportsUsageLimitAutoResume: true);

    /// <summary>`--continue`/`-c` continues the most recent session in the working directory;
    /// `--session`/`-S` (and its undocumented aliases `-r`/`--resume`) select a session by ID and are
    /// mutually exclusive with `--continue`, so they're stripped before appending it.</summary>
    public static readonly AgentDefinition KimiCodeCli = new(
        Kind: AgentKind.KimiCodeCli,
        DisplayName: "Kimi Code CLI",
        DefaultExecutable: "kimi",
        DefaultArguments: "",
        ResumeArgumentTemplate: ["{args}", "--continue"],
        ResumeFlagsToStrip: ["-c", "--continue"],
        ResumeFlagsToStripWithOptionalValue: ["-S", "--session", "-r", "--resume"],
        CompactResumeExtraToken: null,
        ResumeEnvironmentVariables: new Dictionary<string, string>(),
        ProjectInstructionFileName: "AGENTS.md",
        UserConfigDirectoryRelativeToHome: ".kimi-code",
        SupportsUsageLimitAutoResume: false);

    /// <summary>`-c`/`--continue` resumes the most recent conversation; `--conversation &lt;id&gt;`
    /// resumes a specific one by ID and is stripped the same way.</summary>
    public static readonly AgentDefinition AntigravityCli = new(
        Kind: AgentKind.AntigravityCli,
        DisplayName: "Antigravity CLI",
        DefaultExecutable: "agy",
        DefaultArguments: "",
        ResumeArgumentTemplate: ["{args}", "--continue"],
        ResumeFlagsToStrip: ["-c", "--continue"],
        ResumeFlagsToStripWithOptionalValue: ["--conversation"],
        CompactResumeExtraToken: null,
        ResumeEnvironmentVariables: new Dictionary<string, string>(),
        ProjectInstructionFileName: "AGENTS.md",
        UserConfigDirectoryRelativeToHome: Path.Combine(".gemini", "antigravity-cli"),
        SupportsUsageLimitAutoResume: false);

    public static IReadOnlyList<AgentDefinition> All { get; } = [ClaudeCode, CodexCli, KimiCodeCli, AntigravityCli];

    public static AgentDefinition Get(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => ClaudeCode,
        AgentKind.CodexCli => CodexCli,
        AgentKind.KimiCodeCli => KimiCodeCli,
        AgentKind.AntigravityCli => AntigravityCli,
        _ => ClaudeCode,
    };
}
