using LancerMcp.Benchmarks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkStatisticsTests
{
    [Fact]
    public void Percentile_UsesNearestRank()
    {
        var values = new long[] { 10, 20, 30, 40, 50 };

        Assert.Equal(30, BenchmarkStatistics.Percentile(values, 0.50));
        Assert.Equal(50, BenchmarkStatistics.Percentile(values, 0.95));
    }
}
