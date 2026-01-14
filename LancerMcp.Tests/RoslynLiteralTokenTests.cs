using LancerMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LancerMcp.Tests;

public sealed class RoslynLiteralTokenTests
{
    [Fact]
    public async Task ParseFileAsync_CapturesStringLiteralTokensOnMethod()
    {
        var service = new RoslynParserService(NullLogger<RoslynParserService>.Instance);
        var code = @"
namespace Demo;
public class AuthService
{
    public void Login()
    {
        var message = ""Invalid password"";
    }
}";

        var result = await service.ParseFileAsync("repo", "main", "sha", "AuthService.cs", code);
        var login = result.Symbols.First(s => s.Name == "Login");

        Assert.Contains("invalid", login.LiteralTokens ?? Array.Empty<string>());
        Assert.Contains("password", login.LiteralTokens ?? Array.Empty<string>());
    }
}
