using LancerMcp.Benchmarks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkQuerySetTests
{
    [Fact]
    public void FromJson_LoadsQueriesAndTopK()
    {
        var json = """
{
  "name": "csharp-minimal",
  "topK": 5,
  "queries": [
    { "query": "find UserService class", "expectedSymbols": ["UserService"] }
  ]
}
""";

        var set = BenchmarkQuerySet.FromJson(json);

        Assert.Equal("csharp-minimal", set.Name);
        Assert.Equal(5, set.TopK);
        Assert.Single(set.Queries);
        Assert.Equal("find UserService class", set.Queries[0].Query);
        Assert.Contains("UserService", set.Queries[0].ExpectedSymbols);
    }
}
