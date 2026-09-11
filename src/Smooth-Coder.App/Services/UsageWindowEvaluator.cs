namespace SmoothCoder.App.Services;

/// <summary>Pure percentage math for the usage bars, kept separate from
/// <see cref="ClaudeAccountUsageTracker"/> so it's trivially unit-testable (no file I/O, no state) -
/// same "static, inputs as parameters" shape as <see cref="ScheduleEvaluator"/>.</summary>
public static class UsageWindowEvaluator
{
    /// <summary>Null until the corresponding window has been calibrated at least once (a rate-limit
    /// event of that kind has never been observed), so callers can distinguish "not yet measured" from
    /// "measured at 0%". Never divides by zero even if a baseline of 0 were somehow persisted.</summary>
    public static double? ComputePercentage(long currentWindowTokens, long? baselineTokens)
    {
        if (baselineTokens is not { } baseline || baseline <= 0)
        {
            return null;
        }

        return currentWindowTokens / (double)baseline * 100.0;
    }
}
