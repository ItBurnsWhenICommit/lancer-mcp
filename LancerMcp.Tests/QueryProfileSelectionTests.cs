using System.Net.Http;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryProfileSelectionTests
{
    [Fact]
    public async Task QueryAsync_DefaultsToFastProfile()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Fast };
        var optionsMonitor = new TestOptionsMonitor(options);
        var orchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new ThrowingCodeChunkRepository(),
            new ThrowingEmbeddingRepository(),
            new ReturningSymbolRepository(),
            new ThrowingSymbolSearchRepository(),
            new ThrowingEdgeRepository(),
            new EmbeddingService(new HttpClient(), optionsMonitor, NullLogger<EmbeddingService>.Instance),
            optionsMonitor);

        var response = await orchestrator.QueryAsync(
            query: "find UserService",
            repositoryName: "repo",
            branchName: "main");

        Assert.NotNull(response.Metadata);
        Assert.Equal("Fast", response.Metadata?["profile"]?.ToString());
    }

    private sealed class ReturningSymbolRepository : ISymbolRepository
    {
        public Task<IEnumerable<Symbol>> SearchByNameAsync(string repoId, string query, string? branchName = null, bool fuzzy = false, int limit = 50, CancellationToken cancellationToken = default)
        {
            var symbol = new Symbol
            {
                RepositoryName = repoId,
                BranchName = branchName ?? "main",
                CommitSha = "sha",
                FilePath = "src/UserService.cs",
                Name = "UserService",
                QualifiedName = "Demo.UserService",
                Kind = SymbolKind.Class,
                Language = Language.CSharp,
                StartLine = 1,
                StartColumn = 1,
                EndLine = 5,
                EndColumn = 1
            };

            return Task.FromResult<IEnumerable<Symbol>>(new[] { symbol });
        }

        public Task<Symbol?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> GetByIdsAsync(IEnumerable<string> symbolIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> GetByKindAsync(string repoId, SymbolKind kind, int limit = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Symbol> CreateAsync(Symbol symbol, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CreateBatchAsync(IEnumerable<Symbol> symbols, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ThrowingCodeChunkRepository : ICodeChunkRepository
    {
        public Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetBySymbolAsync(string symbolId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByLanguageAsync(string repoId, Language language, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> SearchFullTextAsync(string repoId, string query, string? branchName = null, Language? language = null, int limit = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CodeChunk> CreateAsync(CodeChunk chunk, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CreateBatchAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ThrowingEmbeddingRepository : IEmbeddingRepository
    {
        public Task<Embedding?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Embedding?> GetByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Embedding>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<(Embedding Embedding, float Distance)>> SearchBySimilarityAsync(float[] queryVector, string? repoId = null, string? branchName = null, int limit = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<(string ChunkId, float Score, float? BM25Score, float? VectorScore)>> HybridSearchAsync(string queryText, float[] queryVector, string? repoId = null, string? branchName = null, float bm25Weight = 0.3f, float vectorWeight = 0.7f, int limit = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Embedding> CreateAsync(Embedding embedding, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CreateBatchAsync(IEnumerable<Embedding> embeddings, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsForChunkAsync(string chunkId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ThrowingSymbolSearchRepository : ISymbolSearchRepository
    {
        public Task<IEnumerable<(string SymbolId, float Score, string? Snippet)>> SearchAsync(string repoId, string query, string? branchName, int limit, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class ThrowingEdgeRepository : IEdgeRepository
    {
        public Task<SymbolEdge?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<SymbolEdge>> GetBySourceAsync(string sourceSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<SymbolEdge>> GetByTargetAsync(string targetSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<SymbolEdge>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SymbolEdge> CreateAsync(SymbolEdge edge, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CreateBatchAsync(IEnumerable<SymbolEdge> edges, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
