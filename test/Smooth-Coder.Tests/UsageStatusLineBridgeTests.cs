using System.Globalization;
using SmoothCoder.App.Models;
using SmoothCoder.App.Services;

namespace SmoothCoder.Tests;

public class UsageStatusLineBridgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a status-line stdin payload. Plain interpolation with escaped braces, not a
    /// raw-string literal, because the JSON's own trailing "}}" would collide with the "}}"
    /// interpolation terminator.</summary>
    private static string PayloadJson(
        double? ctx = null,
        (double Pct, DateTimeOffset Reset)? fiveHour = null,
        (double Pct, DateTimeOffset Reset)? sevenDay = null)
    {
        static string Inv(double d) => d.ToString(CultureInfo.InvariantCulture);
        var parts = new List<string>();
        if (ctx is not null)
        {
            parts.Add($"\"context_window\":{{\"used_percentage\":{Inv(ctx.Value)}}}");
        }

        var windows = new List<string>();
        if (fiveHour is { } f)
        {
            windows.Add($"\"five_hour\":{{\"used_percentage\":{Inv(f.Pct)},\"resets_at\":{f.Reset.ToUnixTimeSeconds()}}}");
        }

        if (sevenDay is { } s)
        {
            windows.Add($"\"seven_day\":{{\"used_percentage\":{Inv(s.Pct)},\"resets_at\":{s.Reset.ToUnixTimeSeconds()}}}");
        }

        if (windows.Count > 0)
        {
            parts.Add($"\"rate_limits\":{{{string.Join(",", windows)}}}");
        }

        return "{" + string.Join(",", parts) + "}";
    }

    [Fact]
    public void BuildSnapshot_FullPayload_ExtractsBothWindowsAndContext()
    {
        var reset5h = Now.AddHours(3);
        var json = PayloadJson(ctx: 60, fiveHour: (23.5, reset5h), sevenDay: (41.2, Now.AddDays(4)));

        var snapshot = UsageStatusLineBridge.BuildSnapshot(json, previous: null, Now);

        Assert.Equal(Now, snapshot.CapturedAt);
        Assert.Equal(60, snapshot.ContextUsedPercentage);
        Assert.Equal(23.5, snapshot.FiveHour!.UsedPercentage);
        Assert.Equal(reset5h.ToUnixTimeSeconds(), snapshot.FiveHour.ResetsAt.ToUnixTimeSeconds());
        Assert.Equal(41.2, snapshot.SevenDay!.UsedPercentage);
    }

    [Fact]
    public void BuildSnapshot_NoRateLimits_KeepsContextOnlyAndNoWindows()
    {
        var snapshot = UsageStatusLineBridge.BuildSnapshot(PayloadJson(ctx: 12), previous: null, Now);

        Assert.Equal(12, snapshot.ContextUsedPercentage);
        Assert.Null(snapshot.FiveHour);
        Assert.Null(snapshot.SevenDay);
    }

    [Fact]
    public void BuildSnapshot_MalformedJson_DoesNotThrowAndCarriesPreviousForward()
    {
        var previous = new UsageSnapshot
        {
            CapturedAt = Now.AddMinutes(-2),
            FiveHour = new UsageSnapshotWindow { UsedPercentage = 30, ResetsAt = Now.AddHours(2) },
        };

        var snapshot = UsageStatusLineBridge.BuildSnapshot("not json {", previous, Now);

        Assert.Equal(Now, snapshot.CapturedAt);
        Assert.Equal(30, snapshot.FiveHour!.UsedPercentage);
    }

    [Fact]
    public void BuildSnapshot_WindowAbsentThisTime_CarriesForwardOnlyWhileResetInFuture()
    {
        var previous = new UsageSnapshot
        {
            CapturedAt = Now.AddMinutes(-5),
            FiveHour = new UsageSnapshotWindow { UsedPercentage = 55, ResetsAt = Now.AddHours(1) },
            SevenDay = new UsageSnapshotWindow { UsedPercentage = 88, ResetsAt = Now.AddMinutes(-1) },
        };

        var snapshot = UsageStatusLineBridge.BuildSnapshot(PayloadJson(ctx: 1), previous, Now);

        Assert.Equal(55, snapshot.FiveHour!.UsedPercentage); // carried
        Assert.Null(snapshot.SevenDay);                       // expired, dropped
    }

    [Fact]
    public void BuildSnapshot_NewPayloadWindowWinsOverCarriedPrevious()
    {
        var previous = new UsageSnapshot
        {
            CapturedAt = Now.AddMinutes(-5),
            FiveHour = new UsageSnapshotWindow { UsedPercentage = 10, ResetsAt = Now.AddHours(1) },
        };

        var snapshot = UsageStatusLineBridge.BuildSnapshot(PayloadJson(fiveHour: (44, Now.AddHours(2))), previous, Now);

        Assert.Equal(44, snapshot.FiveHour!.UsedPercentage);
    }

    [Fact]
    public void FormatLine_OmitsMissingParts()
    {
        var snapshot = new UsageSnapshot
        {
            FiveHour = new UsageSnapshotWindow { UsedPercentage = 23.5, ResetsAt = Now.AddHours(1) },
            ContextUsedPercentage = 60,
        };

        Assert.Equal("5h 24% · ctx 60%", UsageStatusLineBridge.FormatLine(snapshot));
    }

    [Fact]
    public void FormatLine_EmptySnapshot_IsEmptyString()
    {
        Assert.Equal(string.Empty, UsageStatusLineBridge.FormatLine(new UsageSnapshot()));
    }

    [Fact]
    public void Run_WritesSnapshotAndPrintsLine()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SmoothCoderBridgeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var snapshotStore = new UsageSnapshotStore(Path.Combine(dir, "snapshot.json"));
            var settingsStore = new AppSettingsStore(Path.Combine(dir, "settings.json"));
            var stdout = new StringWriter();

            UsageStatusLineBridge.Run(
                new StringReader(PayloadJson(fiveHour: (50, Now.AddHours(1)))), stdout, snapshotStore, settingsStore, Now);

            Assert.Contains("5h 50%", stdout.ToString());
            Assert.Equal(50, snapshotStore.Load()!.FiveHour!.UsedPercentage);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
