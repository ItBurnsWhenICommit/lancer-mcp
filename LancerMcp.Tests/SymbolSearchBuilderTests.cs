using LancerMcp.Models;
using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class SymbolSearchBuilderTests
{
    [Fact]
    public void BuildEntries_IncludesTokenBucketsAndSnippet()
    {
        var symbol = new Symbol
        {
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "AuthService.cs",
            Name = "AuthService",
            QualifiedName = "Demo.AuthService",
            Kind = SymbolKind.Class,
            Language = Language.CSharp,
            StartLine = 1,
            StartColumn = 1,
            EndLine = 3,
            EndColumn = 1,
            Signature = "AuthService()",
            Documentation = "Handles authentication flows.",
            LiteralTokens = new[] { "invalid", "password" }
        };

        var parsed = new ParsedFile
        {
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "AuthService.cs",
            Language = Language.CSharp,
            Symbols = new List<Symbol> { symbol },
            SourceText = "public class AuthService { }",
            Success = true
        };

        var entries = SymbolSearchBuilder.BuildEntries(parsed);
        var entry = entries.Single();

        Assert.Contains("auth", entry.NameTokens);
        Assert.Contains("demo", entry.QualifiedTokens);
        Assert.Contains("authentication", entry.DocumentationTokens);
        Assert.Contains("invalid", entry.LiteralTokens);
        Assert.False(string.IsNullOrWhiteSpace(entry.Snippet));
    }
}
