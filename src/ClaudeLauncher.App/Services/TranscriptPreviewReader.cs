using System.Text;
using System.Text.Json;

namespace ClaudeLauncher.App.Services;

/// <summary>Extracts a short, human-readable snippet of the most recent conversation turn from a
/// Claude Code transcript, for the dashboard's live preview panel. Shares
/// <see cref="TranscriptActivityClassifier"/>'s tail-read approach (only the last turn or two are ever
/// shown, so there's no need to parse the whole file) but keeps its own line-parsing logic since it
/// needs the message text itself rather than just an idle/responding classification.</summary>
public static class TranscriptPreviewReader
{
    private const int MaxSnippetLength = 160;

    /// <summary>Returns up to two lines - the last real user message and the last assistant activity
    /// (finished text, or a "running tool X" / "thinking" placeholder while a turn is still in
    /// progress) - or null if the tail window contains no recognizable turn yet.</summary>
    public static string? ReadPreview(string jsonlFilePath)
    {
        var text = TranscriptTailFile.ReadTail(jsonlFilePath);
        return text is null ? null : ExtractPreview(text);
    }

    /// <summary>Extracts a preview from an already-read tail of a transcript. Exposed internally so a
    /// caller that also needs <see cref="TranscriptActivityClassifier"/>'s output can read the file
    /// once and pass the same text to both, instead of reading it twice.</summary>
    internal static string? ExtractPreview(string text)
    {
        string? lastUser = null;
        string? lastAssistant = null;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // A leading line in the tail window may be truncated mid-record (we started reading from
            // an arbitrary byte offset); TryExtractLine returns null for it via the JsonException catch
            // below and it's simply skipped, same as any other unparseable line.
            var (role, snippet) = TryExtractLine(trimmed);
            if (snippet is null)
            {
                continue;
            }

            if (role == "user")
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
            lines.Add($"あなた: {Truncate(lastUser)}");
        }

        if (lastAssistant is not null)
        {
            lines.Add($"Claude: {Truncate(lastAssistant)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static (string? role, string? snippet) TryExtractLine(string jsonlLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonlLine);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            {
                return (null, null);
            }

            var type = typeEl.GetString();
            if (type is not ("user" or "assistant"))
            {
                return (null, null);
            }

            if (!root.TryGetProperty("message", out var messageEl) || messageEl.ValueKind != JsonValueKind.Object
                || !messageEl.TryGetProperty("role", out var roleEl) || roleEl.ValueKind != JsonValueKind.String
                || !messageEl.TryGetProperty("content", out var contentEl))
            {
                return (null, null);
            }

            var snippet = ExtractSnippet(contentEl);
            return snippet is null ? (null, null) : (type, snippet);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    /// <summary>A "tool_result" block (the user-role line that relays a tool's output back into the
    /// agent loop) carries no text worth previewing on its own, so it's skipped - leaving the
    /// caller's <c>lastUser</c> pointing at the human's actual most recent prompt instead.</summary>
    private static string? ExtractSnippet(JsonElement contentEl)
    {
        if (contentEl.ValueKind == JsonValueKind.String)
        {
            var s = contentEl.GetString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        if (contentEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var textBuilder = new StringBuilder();
        var sawToolUse = false;
        string? toolName = null;
        var sawThinking = false;

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var blockTypeEl) || blockTypeEl.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            switch (blockTypeEl.GetString())
            {
                case "text":
                    if (block.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                    {
                        textBuilder.Append(textEl.GetString());
                    }

                    break;
                case "tool_use":
                    sawToolUse = true;
                    if (block.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    {
                        toolName = nameEl.GetString();
                    }

                    break;
                case "thinking":
                    sawThinking = true;
                    break;
            }
        }

        if (textBuilder.Length > 0)
        {
            return textBuilder.ToString();
        }

        if (sawToolUse)
        {
            return toolName is null ? "🔧 ツールを実行中…" : $"🔧 {toolName} を実行中…";
        }

        return sawThinking ? "💭 考え中…" : null;
    }

    private static string Truncate(string value)
    {
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= MaxSnippetLength ? collapsed : string.Concat(collapsed.AsSpan(0, MaxSnippetLength), "…");
    }
}
