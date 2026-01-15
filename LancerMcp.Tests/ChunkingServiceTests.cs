using Microsoft.Extensions.Logging.Abstractions;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class ChunkingServiceTests
{
    [Fact]
    public async Task ChunkFileAsync_DeduplicatesChunksWithSameUniqueKey()
    {
        var options = new ServerOptions
        {
            ChunkContextLinesBefore = 0,
            ChunkContextLinesAfter = 0,
            MaxChunkChars = 10_000
        };
        var optionsMonitor = new TestOptionsMonitor(options);

        var gitTracker = new GitTrackerService(
            NullLogger<GitTrackerService>.Instance,
            optionsMonitor,
            new MockRepositoryRepository(),
            new MockBranchRepository(),
            TestHelpers.CreateMockWorkspaceLoader());

        var chunkingService = new ChunkingService(
            gitTracker,
            NullLogger<ChunkingService>.Instance,
            optionsMonitor);

        var firstSymbol = new Symbol
        {
            Id = "sym1",
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "File.cs",
            Name = "First",
            Kind = SymbolKind.Method,
            Language = Language.CSharp,
            StartLine = 2,
            StartColumn = 1,
            EndLine = 3,
            EndColumn = 1
        };

        var secondSymbol = new Symbol
        {
            Id = "sym2",
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "File.cs",
            Name = "Second",
            Kind = SymbolKind.Method,
            Language = Language.CSharp,
            StartLine = 2,
            StartColumn = 1,
            EndLine = 3,
            EndColumn = 1
        };

        var parsedFile = new ParsedFile
        {
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "File.cs",
            Language = Language.CSharp,
            SourceText = string.Join('\n', new[] { "line1", "line2", "line3", "line4", "line5" }),
            Symbols = new List<Symbol> { firstSymbol, secondSymbol },
            Success = true
        };

        var result = await chunkingService.ChunkFileAsync(parsedFile);

        Assert.True(result.Success);
        var chunk = Assert.Single(result.Chunks);
        Assert.Equal(firstSymbol.Id, chunk.SymbolId);
    }
}
