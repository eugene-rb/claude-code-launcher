namespace SmoothCoder.App.Models;

/// <summary>Account-wide baseline for estimating "% of Claude's usage limit consumed", persisted
/// separately from <see cref="AppSettings"/> (see <see cref="Services.UsageCalibrationStore"/> for why).
/// Anthropic never exposes the numeric token limit itself - only a "you've hit it" event and (via the
/// status-line bridge, see <see cref="UsageSnapshot"/>) a live <c>used_percentage</c> - so the baseline
/// for each window is fitted to whatever ground-truth percentage was last observed:
/// <c>baseline = tokens-in-window / (observed% / 100)</c>. A rate-limit event is just the
/// <c>observed% = 100</c> case of that same fit. Null until a window has been calibrated at least once.</summary>
public sealed class UsageCalibration
{
    public long? SessionWindowBaselineTokens { get; set; }

    public DateTimeOffset? SessionWindowCalibratedAt { get; set; }

    public long? WeeklyWindowBaselineTokens { get; set; }

    public DateTimeOffset? WeeklyWindowCalibratedAt { get; set; }

    /// <summary>The true window boundary from the last status-line reading (the payload's
    /// <c>resets_at</c>). Lets the tracker sum tokens since the real block start instead of a rolling
    /// "last 5 hours" approximation. Persisted so the block math survives a restart before the first
    /// status-line render of the new run. Ignored once it's in the past.</summary>
    public DateTimeOffset? SessionWindowResetsAt { get; set; }

    /// <summary>Last real <c>used_percentage</c> observed for this window and when it was captured -
    /// the app shows this directly ("実測") while it's fresh, instead of the token estimate.</summary>
    public double? SessionWindowLastRealPercent { get; set; }

    public DateTimeOffset? SessionWindowLastRealAt { get; set; }

    public DateTimeOffset? WeeklyWindowResetsAt { get; set; }

    public double? WeeklyWindowLastRealPercent { get; set; }

    public DateTimeOffset? WeeklyWindowLastRealAt { get; set; }
}
