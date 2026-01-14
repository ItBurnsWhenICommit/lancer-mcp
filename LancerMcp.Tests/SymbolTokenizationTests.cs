using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class SymbolTokenizationTests
{
    [Fact]
    public void Tokenize_SplitsCamelCaseAndQualifiedNames()
    {
        var tokens = SymbolTokenization.Tokenize("MyApp.Services.UserService.GetUserById");

        Assert.Contains("my", tokens);
        Assert.Contains("services", tokens);
        Assert.Contains("user", tokens);
        Assert.Contains("service", tokens);
        Assert.Contains("get", tokens);
        Assert.Contains("by", tokens);
        Assert.Contains("id", tokens);
    }
}
