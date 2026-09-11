using System.Text;
using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Best-effort idle/responding classification and preview extraction shared by the
/// non-Claude <see cref="IAgentTranscriptSource"/> implementations (Codex CLI, Kimi Code CLI,
/// Antigravity CLI). Unlike <see cref="TranscriptActivityClassifier"/>/<see cref="TranscriptPreviewReader"/>
/// - which were verified against 35 real Claude Code transcripts - this reads a loose superset of the
/// message shapes each CLI's public documentation describes (role/content fields resembling the
/// Anthropic/OpenAI message schema, plus a handful of plausible tool-call markers). It has not been
/// checked against a real transcript from any of these three CLIs. Treat its output as a starting
/// point to refine once real sample logs are available, not a validated classifier - it is written to
/// degrade to "skip this line" on anything unrecognized rather than guess wrong.</summary>
internal static class GenericChatJsonlHeuristics
{
    private const int MaxSnippetLength = 160;

    private static readonly string[] UserRoles = ["user", "human"];
    private static readonly string[] AssistantRoles = ["assistant", "model", "agent"];
    private static readonly string[] ToolMarkerKeys =
        ["tool_use", "tool_call", "tool_calls", "function_call", "toolCall", "functionCall"];

    public static ProjectActivityState? ClassifyText(string text)
    {
        ProjectActivityState? result = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var classified = TryClassifyLine(line);
            if (classified is not null)
            {
                result = classified;
            }
        }

        return result;
    }

    public static string? ExtractPreview(string text, string userLabel, string assistantLabel)
    {
        string? lastUser = null;
        string? lastAssistant = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var (role, snippet) = TryExtractLine(line);
            if (snippet is null)
            {
                continue;
            }

            if (role == LineRole.User)
            {
                lastUser = snippet;
            }
            else
            {
                lastAssistant = snippet;
            }
        }

        if (lastUser is null && lastAssistant is null)
        {
            return null;
        }

        var lines = new List<string>();
        if (lastUser is not null)
        {
            lines.Add($"{userLabel}: {Truncate(lastUser)}");
        }

        if (lastAssistant is not null)
        {
            lines.Add($"{assistantLabel}: {Truncate(lastAssistant)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private enum LineRole
    {
        User,
        Assistant,
    }

    private static ProjectActivityState? TryClassifyLine(string jsonlLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonlLine);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (LooksLikeToolMarker(root))
            {
                return ProjectActivityState.Responding;
            }

            var (role, content) = LocateMessage(root);
            if (role is null || content is not { } contentEl)
            {
                return null;
            }

            if (UserRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return ProjectActivityState.Responding;
            }

            if (!AssistantRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }

            return ContentHasOnlyText(contentEl) ? ProjectActivityState.Idle : ProjectActivityState.Responding;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (LineRole? role, string? snippet) TryExtractLine(string jsonlLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonlLine);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            var (role, content) = LocateMessage(root);
            if (role is null || content is not { } contentEl)
            {
                if (LooksLikeToolMarker(root))
                {
                    return (LineRole.Assistant, "🔧 ツールを実行中…");
                }

                return (null, null);
            }

            var snippet = ExtractText(contentEl) ?? (LooksLikeToolMarker(root) ? "🔧 ツールを実行中…" : null);
            if (snippet is null)
            {
                return (null, null);
            }

            var lineRole = UserRoles.Contains(role, StringComparer.OrdinalIgnoreCase) ? LineRole.User : LineRole.Assistant;
            return (lineRole, snippet);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>Tries a handful of plausible shapes for "the role and message content of this line":
    /// role/content at the root, or nested under a "message" object - covering both a flat
    /// `{"role":...,"content":...}` line and Claude-style `{"type":"user","message":{"role":...}}`.</summary>
    private static (string? role, JsonElement? content) LocateMessage(JsonElement root)
    {
        if (TryGetRoleAndContent(root, out var role, out var content))
        {
            return (role, content);
        }

        if (root.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.Object
            && TryGetRoleAndContent(messageEl, out role, out content))
        {
            return (role, content);
        }

        // Codex rollout JSONL stores Responses items under a payload wrapper.
        if (root.TryGetProperty("payload", out var payloadEl) && payloadEl.ValueKind == JsonValueKind.Object
            && TryGetRoleAndContent(payloadEl, out role, out content))
        {
            return (role, content);
        }

        return (null, null);
    }

    private static bool TryGetRoleAndContent(JsonElement obj, out string? role, out JsonElement? content)
    {
        role = null;
        content = null;

        if (!obj.TryGetProperty("role", out var roleEl) || roleEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        if (!obj.TryGetProperty("content", out var contentEl))
        {
            return false;
        }

        role = roleEl.GetString();
        content = contentEl;
        return true;
    }

    private static bool LooksLikeToolMarker(JsonElement root)
    {
        if (root.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var type = typeEl.GetString() ?? string.Empty;
            if (ToolMarkerKeys.Any(k => type.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("type", out var payloadType) && payloadType.ValueKind == JsonValueKind.String)
        {
            var type = payloadType.GetString() ?? string.Empty;
            if (ToolMarkerKeys.Any(k => type.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return ToolMarkerKeys.Any(key => root.TryGetProperty(key, out _));
    }

    private static bool ContentHasOnlyText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return true;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        var sawAnyBlock = false;
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            sawAnyBlock = true;
            var type = typeEl.GetString();
            if (type != "text" && type != "output_text" && type != "input_text")
            {
                return false;
            }
        }

        return sawAnyBlock;
    }

    private static string? ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            var s = content.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.String)
            {
                builder.Append(block.GetString());
                continue;
            }

            if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var type = typeEl.GetString();
            if (type is "text" or "output_text" or "input_text"
                && block.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
            {
                builder.Append(textEl.GetString());
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static string Truncate(string value)
    {
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxSnippetLength ? collapsed : string.Concat(collapsed.AsSpan(0, MaxSnippetLength), "…");
    }
}
