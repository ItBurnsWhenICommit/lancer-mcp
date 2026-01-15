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

    [Fact]
    public void ExtractIdentifierTokens_FiltersKeywordsNumbersAndLength()
    {
        var text = "class UserService { int id; var x1 = 1; string URLParser; }";

        var tokens = SymbolTokenization.ExtractIdentifierTokens(text, maxChars: 4000, maxTokens: 256);

        Assert.Contains("user", tokens);
        Assert.Contains("service", tokens);
        Assert.Contains("url", tokens);
        Assert.Contains("parser", tokens);
        Assert.DoesNotContain("class", tokens);
        Assert.DoesNotContain("int", tokens);
        Assert.DoesNotContain("var", tokens);
        Assert.DoesNotContain("id", tokens);
        Assert.DoesNotContain("1", tokens);
    }
}
