using LancerMcp.Benchmarks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkResponseParserTests
{
    [Fact]
    public void Parse_ExtractsSymbolsAndSnippetChars()
    {
        var json = "{" +
                   "\"results\":[" +
                   "{\"symbol\":\"UserService\",\"content\":\"public class UserService {}\"}," +
                   "{\"symbol\":\"AuthService\"}" +
                   "]}";

        var parsed = BenchmarkResponseParser.Parse(json);

        Assert.Equal(2, parsed.ResultSymbols.Count);
        Assert.Contains("UserService", parsed.ResultSymbols);
        Assert.Equal("public class UserService {}".Length, parsed.SnippetChars);
    }
}
