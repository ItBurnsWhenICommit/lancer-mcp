using LancerMcp.Models;
using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class SymbolFingerprintBuilderTests
{
    [Fact]
    public void BuildEntries_IncludesIdentifierTokensFromSnippet()
    {
        var parsedFile = new ParsedFile
        {
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "File.cs",
            Language = Language.CSharp,
            SourceText = "public class UserService { public void Login(User user) { var loginCount = 1; } }",
            Symbols = new List<Symbol>
            {
                new()
                {
                    Id = "sym1",
                    RepositoryName = "repo",
                    BranchName = "main",
                    CommitSha = "sha",
                    FilePath = "File.cs",
                    Name = "Login",
                    Kind = SymbolKind.Method,
                    Language = Language.CSharp,
                    StartLine = 1,
                    StartColumn = 1,
                    EndLine = 1,
                    EndColumn = 80
                }
            },
            Success = true
        };

        var recorder = new RecordingFingerprintService();

        _ = SymbolFingerprintBuilder.BuildEntries(parsedFile, recorder);

        Assert.Contains("user", recorder.SeenTokens);
        Assert.Contains("service", recorder.SeenTokens);
        Assert.Contains("login", recorder.SeenTokens);
        Assert.DoesNotContain("var", recorder.SeenTokens);
        Assert.DoesNotContain("1", recorder.SeenTokens);
    }

    private sealed class RecordingFingerprintService : IFingerprintService
    {
        public List<string> SeenTokens { get; } = new();

        public FingerprintResult Compute(IEnumerable<string> tokens)
        {
            SeenTokens.AddRange(tokens);
            return FingerprintResult.FromHash(0UL);
        }
    }
}
