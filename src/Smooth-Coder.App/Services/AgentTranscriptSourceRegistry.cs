using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Resolves the <see cref="IAgentTranscriptSource"/> for a given <see cref="AgentKind"/>.
/// Instances are cheap (no I/O in their constructors) and stateless aside from their root-path
/// overrides, so one shared instance per kind is reused rather than allocating per call.</summary>
public static class AgentTranscriptSourceRegistry
{
    private static readonly IAgentTranscriptSource Claude = new ClaudeTranscriptSource();
    private static readonly IAgentTranscriptSource Codex = new CodexTranscriptSource();
    private static readonly IAgentTranscriptSource Kimi = new KimiTranscriptSource();
    private static readonly IAgentTranscriptSource Antigravity = new AntigravityTranscriptSource();

    public static IAgentTranscriptSource Get(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => Claude,
        AgentKind.CodexCli => Codex,
        AgentKind.KimiCodeCli => Kimi,
        AgentKind.AntigravityCli => Antigravity,
        _ => Claude,
    };
}
