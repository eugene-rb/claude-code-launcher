using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudeLauncher.App.Services;

/// <summary>Detects and parses Claude Code's "rate_limit" transcript events. Verified against 35 real
/// events collected from this machine's own ~/.claude/projects transcripts, spanning CLI versions
/// 2.1.207-2.1.235: every event is a JSONL line shaped like
/// {"type":"assistant","timestamp":"...Z","message":{"content":[{"type":"text",
/// "text":"You've hit your session limit · resets 3:30am (Asia/Tokyo)"}]},
/// "error":"rate_limit","isApiErrorMessage":true,"apiErrorStatus":429}.
/// Detection relies on the structural "error":"rate_limit" marker, not the wording, since only the
/// reset-time text is guessed at from observed samples and could drift in a future CLI version.
/// Parsing is defensive throughout: malformed/unexpected input yields null, never an exception.</summary>
public static partial class UsageLimitEventParser
{
    public static DateTimeOffset? TryParseLine(string jsonlLine)
    {
        if (!jsonlLine.Contains("\"error\":\"rate_limit\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonlLine);
            var root = doc.RootElement;

            if (!root.TryGetProperty("error", out var errorEl) || errorEl.ValueKind != JsonValueKind.String
                || errorEl.GetString() != "rate_limit")
            {
                return null;
            }

            if (!root.TryGetProperty("timestamp", out var tsEl) || tsEl.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(tsEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var eventTimestamp))
            {
                return null;
            }

            if (!root.TryGetProperty("message", out var messageEl) || !messageEl.TryGetProperty("content", out var contentEl)
                || contentEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var block in contentEl.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                    && typeEl.GetString() == "text"
                    && block.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
                    && textEl.GetString() is { } text)
                {
                    var parsed = TryParseResetTime(text, eventTimestamp);
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses "You've hit your session|weekly limit &#183; resets &lt;time&gt;[, &lt;month day&gt;]
    /// (&lt;tz&gt;)" using the event's own timestamp as the reference "now" — never the caller's clock —
    /// so a late poll can't miscompute which day the reset falls on.</summary>
    public static DateTimeOffset? TryParseResetTime(string messageText, DateTimeOffset eventTimestamp)
    {
        var match = ResetTimeRegex().Match(messageText.Trim());
        if (!match.Success)
        {
            return null;
        }

        if (!TryParseTimeOfDay(match.Groups["time"].Value, out var timeOfDay))
        {
            return null;
        }

        var tz = ResolveTimeZone(match.Groups["tz"].Success ? match.Groups["tz"].Value : null);
        var eventLocal = TimeZoneInfo.ConvertTime(eventTimestamp, tz);

        DateOnly date;
        if (match.Groups["date"].Success)
        {
            if (!TryParseMonthDay(match.Groups["date"].Value, eventLocal, out date))
            {
                return null;
            }
        }
        else
        {
            date = DateOnly.FromDateTime(eventLocal.DateTime);
        }

        var candidate = date.ToDateTime(TimeOnly.FromTimeSpan(timeOfDay));
        var offset = tz.GetUtcOffset(candidate);
        var result = new DateTimeOffset(candidate, offset);

        // A reset time should always be at/after the moment the limit was hit. If combining "today"
        // (in the target zone) with the parsed clock time lands before the event, the message must
        // mean tomorrow (e.g. hit at 23:58, "resets 12:05am").
        if (!match.Groups["date"].Success && result < eventLocal)
        {
            candidate = candidate.AddDays(1);
            offset = tz.GetUtcOffset(candidate);
            result = new DateTimeOffset(candidate, offset);
        }

        return result;
    }

    private static bool TryParseTimeOfDay(string text, out TimeSpan timeOfDay)
    {
        var normalized = text.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        foreach (var format in new[] { "h:mmtt", "htt" })
        {
            if (DateTime.TryParseExact(normalized, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                timeOfDay = parsed.TimeOfDay;
                return true;
            }
        }

        timeOfDay = default;
        return false;
    }

    private static bool TryParseMonthDay(string text, DateTimeOffset referenceLocal, out DateOnly date)
    {
        if (!DateTime.TryParseExact(text.Trim(), "MMM d", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            date = default;
            return false;
        }

        var year = referenceLocal.Year;
        var candidate = new DateOnly(year, parsed.Month, parsed.Day);
        // Weekly-limit resets can land in the following calendar year (e.g. hit in December,
        // resets "Jan 3"); only the month/day are in the text, so infer the year from whichever
        // choice keeps the reset on/after the event.
        if (candidate.ToDateTime(TimeOnly.MinValue) < referenceLocal.Date)
        {
            candidate = new DateOnly(year + 1, parsed.Month, parsed.Day);
        }

        date = candidate;
        return true;
    }

    private static TimeZoneInfo ResolveTimeZone(string? tzId)
    {
        if (string.IsNullOrWhiteSpace(tzId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(tzId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    [GeneratedRegex(@"^You've hit your (?:session|weekly) limit\s*[·∙]\s*resets\s+(?:(?<date>[A-Za-z]{3}\s+\d{1,2}),\s*)?(?<time>\d{1,2}(?::\d{2})?\s*(?:am|pm))\s*(?:\((?<tz>[^)]+)\))?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ResetTimeRegex();
}
