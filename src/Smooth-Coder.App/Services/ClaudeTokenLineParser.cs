using System.Globalization;
using System.Text.Json;

namespace SmoothCoder.App.Services;

/// <summary>Extracts a per-message token-usage sample from one Claude Code transcript JSONL line, for
/// account-wide usage tracking (see <see cref="ClaudeAccountUsageTracker"/>). Verified against a real
/// 28.8MB/1318-message transcript on this machine: every <c>"type":"assistant"</c> line carries a
/// <c>message.usage</c> object shaped like
/// <c>{"input_tokens":N,"cache_creation_input_tokens":N,"cache_read_input_tokens":N,"output_tokens":N}</c>.
/// Lines with <c>"isSidechain":true</c> (sub-agent/Task-tool calls) still carry usage and are counted -
/// they consume the same account-wide rate limit as a top-level turn. Parsing is defensive throughout:
/// malformed/unexpected input yields null, never an exception.</summary>
public static class ClaudeTokenLineParser
{
    public readonly record struct TokenSample(DateTimeOffset Timestamp, long Tokens);

    public static TokenSample? TryParseLine(string jsonlLine)
    {
        if (!jsonlLine.Contains("\"type\":\"assistant\"", StringComparison.Ordinal)
            || !jsonlLine.Contains("\"usage\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonlLine);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String
                || typeEl.GetString() != "assistant")
            {
                return null;
            }

            if (!root.TryGetProperty("timestamp", out var tsEl) || tsEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(tsEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
            {
                return null;
            }

            if (!root.TryGetProperty("message", out var messageEl) || !messageEl.TryGetProperty("usage", out var usageEl)
                || usageEl.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var tokens = ReadLong(usageEl, "input_tokens")
                + ReadLong(usageEl, "cache_creation_input_tokens")
                + ReadLong(usageEl, "cache_read_input_tokens")
                + ReadLong(usageEl, "output_tokens");

            return new TokenSample(timestamp, tokens);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long ReadLong(JsonElement usageEl, string propertyName) =>
        usageEl.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var value)
            ? value
            : 0;
}
