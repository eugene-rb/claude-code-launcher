namespace ClaudeLauncher.App.Models;

/// <summary>Account-wide baseline for estimating "% of Claude's usage limit consumed", persisted
/// separately from <see cref="AppSettings"/> (see <see cref="Services.UsageCalibrationStore"/> for why).
/// Anthropic never exposes the numeric token limit itself - only a "you've hit it" event - so the
/// baseline for each window is captured from the account's own cumulative token consumption at the
/// moment a rate-limit event of that kind was last observed, and used as that window's 100% mark from
/// then on. Null until the corresponding limit has been hit at least once.</summary>
public sealed class UsageCalibration
{
    public long? SessionWindowBaselineTokens { get; set; }

    public DateTimeOffset? SessionWindowCalibratedAt { get; set; }

    public long? WeeklyWindowBaselineTokens { get; set; }

    public DateTimeOffset? WeeklyWindowCalibratedAt { get; set; }
}
