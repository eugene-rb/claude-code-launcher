namespace SmoothCoder.App.Models;

/// <summary>Per-agent executable and default launch arguments, editable in the 設定 tab. Kept as
/// user-editable text rather than hardcoded so a wrong guess about a CLI's binary name or flags (see
/// <see cref="AgentDefinition"/>) costs the user a text-box edit, not a rebuild.</summary>
public sealed class AgentExecutionSettings
{
    public string Executable { get; set; } = string.Empty;

    public string Arguments { get; set; } = string.Empty;
}
