using System.Text.Json;
using ClaudeLauncher.App.Models;

namespace ClaudeLauncher.App.Services;

/// <summary>Classifies a Claude Code transcript's most recent real turn as idle or responding, purely
/// from JSONL structure (no timing heuristics - message content isn't streamed to disk incrementally,
/// so "hasn't grown in N seconds" is not a reliable signal). Verified against real transcripts: besides
/// "user"/"assistant" lines, the CLI interleaves bookkeeping lines (types like "attachment",
/// "last-prompt", "ai-title", "agent-name", "mode", "permission-mode", "bridge-session", "system") that
/// carry no "message.role" and must be skipped rather than treated as the latest turn. Within a real
/// line, each JSONL record has been observed to carry a single content-block type (a "thinking" line is
/// followed by a separate "tool_use" or "text" line, not one line with both) - a line whose only block
/// is "text" is a finished response; anything else (tool_use, thinking-only, etc.) means the agent loop
/// is still working and hasn't reached its final answer yet. This module distinguishes only Idle vs.
/// Responding; disambiguating "tool running" from "awaiting your approval" needs the hook-based
/// <see cref="StatusMarkerStore"/> signal, since a permission prompt is never written to the transcript.</summary>
public static class TranscriptActivityClassifier
{
    public static ProjectActivityState? ClassifyLastTurn(string jsonlFilePath)
    {
        var text = TranscriptTailFile.ReadTail(jsonlFilePath);
        return text is null ? null : ClassifyText(text);
    }

    /// <summary>Classifies an already-read tail of a transcript. Exposed internally so a caller that
    /// also needs <see cref="TranscriptPreviewReader"/>'s output can read the file once and pass the
    /// same text to both, instead of reading it twice.</summary>
    internal static ProjectActivityState? ClassifyText(string text)
    {
        ProjectActivityState? result = null;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            // A leading line in the tail window may be truncated mid-record (we started reading from
            // an arbitrary byte offset); TryClassifyLine returns null for it via the JsonException
            // catch below and it's simply skipped, same as any other unparseable line.
            var classified = TryClassifyLine(trimmed);
            if (classified is not null)
            {
                result = classified;
            }
        }

        return result;
    }

    private static ProjectActivityState? TryClassifyLine(string jsonlLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonlLine);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var type = typeEl.GetString();
            if (type is not ("user" or "assistant"))
            {
                return null;
            }

            if (!root.TryGetProperty("message", out var messageEl) || messageEl.ValueKind != JsonValueKind.Object
                || !messageEl.TryGetProperty("role", out var roleEl) || roleEl.ValueKind != JsonValueKind.String)
            {
                // A bookkeeping line that happens to share the "user"/"assistant" type string but
                // carries no real message (not observed, but defend against it anyway).
                return null;
            }

            if (type == "user")
            {
                // Either the human's own prompt or a tool_result continuing the agent loop - either
                // way Claude is about to act next.
                return ProjectActivityState.Responding;
            }

            if (!messageEl.TryGetProperty("content", out var contentEl) || contentEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var isPureText = true;
            var sawAnyBlock = false;
            foreach (var block in contentEl.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var blockTypeEl) || blockTypeEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                sawAnyBlock = true;
                if (blockTypeEl.GetString() != "text")
                {
                    isPureText = false;
                }
            }

            if (!sawAnyBlock)
            {
                return null;
            }

            // Only a message whose blocks are all "text" is a finished, human-readable answer. A
            // thinking-only block means generation hasn't produced its final output yet, and any
            // tool_use/server_tool_use block means the agent loop is still executing - both count as
            // still responding, not idle.
            return isPureText ? ProjectActivityState.Idle : ProjectActivityState.Responding;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
