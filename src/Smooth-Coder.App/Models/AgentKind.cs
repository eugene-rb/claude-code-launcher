namespace SmoothCoder.App.Models;

/// <summary>Which CLI a project launches with. <see cref="ClaudeCode"/> is the enum's default (0) so
/// an existing <c>sessions.json</c> written before this field existed deserializes every profile as
/// Claude Code with no migration code needed.</summary>
public enum AgentKind
{
    ClaudeCode = 0,
    CodexCli = 1,
    KimiCodeCli = 2,
    AntigravityCli = 3,
}
