using LancerMcp.Benchmarks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkReportTests
{
    [Fact]
    public void Build_ComputesHitRateAndLatencies()
    {
        var querySet = new BenchmarkQuerySet
        {
            Name = "csharp-minimal",
            TopK = 5,
            Queries = new List<BenchmarkQuerySpec>
            {
                new("find UserService class", new List<string> { "UserService" }),
                new("find HashPassword method", new List<string> { "HashPassword" })
            }
        };

        var executions = new List<BenchmarkQueryExecution>
        {
            new("find UserService class", 12, 1000, 120, new[] { "UserService" }),
            new("find HashPassword method", 25, 900, 80, new[] { "OtherSymbol" })
        };

        var report = BenchmarkReport.Build(
            indexing: new BenchmarkIndexingStats(100, 1000, 2000, 5, 10, 8),
            querySet: querySet,
            executions: executions);

        Assert.Equal(0.5, report.TopKHitRate);
        Assert.Equal(12, report.QueryLatencyP50Ms);
        Assert.Equal(25, report.QueryLatencyP95Ms);
    }
}
