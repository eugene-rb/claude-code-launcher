using System.Text.Json;
using SmoothCoder.App.Models;

namespace SmoothCoder.App.Services;

/// <summary>Parses the verified Codex rollout shape
/// <c>event_msg -&gt; token_count -&gt; rate_limits</c>.</summary>
public static class CodexRateLimitParser
{
    public static CodexUsageSnapshot? TryParseSnapshot(string jsonlLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonlLine);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var rootType) || rootType.GetString() != "event_msg"
                || !root.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("type", out var payloadType) || payloadType.GetString() != "token_count"
                || !payload.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var capturedAt = root.TryGetProperty("timestamp", out var timestamp)
                && timestamp.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(timestamp.GetString(), out var parsedTimestamp)
                    ? parsedTimestamp
                    : DateTimeOffset.UtcNow;

            return new CodexUsageSnapshot(
                capturedAt,
                TryParseWindow(limits, "primary"),
                TryParseWindow(limits, "secondary"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static DateTimeOffset? TryParseReachedReset(string jsonlLine)
    {
        var snapshot = TryParseSnapshot(jsonlLine);
        if (snapshot is null)
        {
            return null;
        }

        var reached = new[] { snapshot.Primary, snapshot.Secondary }
            .Where(window => window is { UsedPercentage: >= 100 })
            .Select(window => window!.ResetsAt)
            .OrderBy(reset => reset)
            .FirstOrDefault();

        return reached == default ? null : reached;
    }

    private static CodexUsageWindow? TryParseWindow(JsonElement limits, string name)
    {
        if (!limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
            || !window.TryGetProperty("used_percent", out var used) || !used.TryGetDouble(out var usedPercent)
            || !window.TryGetProperty("window_minutes", out var minutes) || !minutes.TryGetInt32(out var windowMinutes)
            || !window.TryGetProperty("resets_at", out var reset) || !reset.TryGetInt64(out var resetEpoch))
        {
            return null;
        }

        try
        {
            return new CodexUsageWindow(usedPercent, windowMinutes, DateTimeOffset.FromUnixTimeSeconds(resetEpoch));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
