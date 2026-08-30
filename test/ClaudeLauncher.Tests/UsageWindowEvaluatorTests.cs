using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class UsageWindowEvaluatorTests
{
    [Fact]
    public void ComputePercentage_NullBaseline_ReturnsNull()
    {
        Assert.Null(UsageWindowEvaluator.ComputePercentage(currentWindowTokens: 1000, baselineTokens: null));
    }

    [Fact]
    public void ComputePercentage_ZeroBaseline_ReturnsNullInsteadOfDividingByZero()
    {
        Assert.Null(UsageWindowEvaluator.ComputePercentage(currentWindowTokens: 1000, baselineTokens: 0));
    }

    [Fact]
    public void ComputePercentage_HalfOfBaseline_Returns50()
    {
        var result = UsageWindowEvaluator.ComputePercentage(currentWindowTokens: 500, baselineTokens: 1000);

        Assert.Equal(50.0, result);
    }

    [Fact]
    public void ComputePercentage_ExceedsBaseline_ReturnsOverHundred()
    {
        var result = UsageWindowEvaluator.ComputePercentage(currentWindowTokens: 1500, baselineTokens: 1000);

        Assert.Equal(150.0, result);
    }

    [Fact]
    public void ComputePercentage_ZeroCurrentTokens_ReturnsZero()
    {
        var result = UsageWindowEvaluator.ComputePercentage(currentWindowTokens: 0, baselineTokens: 1000);

        Assert.Equal(0.0, result);
    }
}
