using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryEmbeddingModelResolutionTests
{
    [Fact]
    public async Task MissingModel_UsesDefaultModel()
    {
        var options = new TestOptionsMonitor(new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Hybrid,
            EmbeddingsEnabled = true,
            EmbeddingModel = "model-a"
        });

        var orchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new FakeChunkRepository(),
            new FakeEmbeddingRepository("model-a", 2),
            new ThrowingSymbolRepository(),
            new ThrowingSymbolSearchRepository(),
            new ThrowingEdgeRepository(),
            new ThrowingSymbolFingerprintRepository(),
            new TestEmbeddingProvider(),
            options);

        var response = await orchestrator.QueryAsync(
            query: "db",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: Convert.ToBase64String(new byte[8]),
            queryEmbeddingDims: 2,
            queryEmbeddingModel: null);

        Assert.Equal("model-a", response.Metadata?["embeddingModel"]);
    }

    [Fact]
    public async Task AmbiguousModel_FallsBackWithError()
    {
        var options = new TestOptionsMonitor(new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Hybrid,
            EmbeddingsEnabled = true,
            EmbeddingModel = ""
        });

        var orchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new FakeChunkRepository(),
            new FakeEmbeddingRepository("model-a", 2, "model-b"),
            new ThrowingSymbolRepository(),
            new ThrowingSymbolSearchRepository(),
            new ThrowingEdgeRepository(),
            new ThrowingSymbolFingerprintRepository(),
            new TestEmbeddingProvider(),
            options);

        var response = await orchestrator.QueryAsync(
            query: "db",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: Convert.ToBase64String(new byte[8]),
            queryEmbeddingDims: 2,
            queryEmbeddingModel: null);

        Assert.Equal("embedding_model_ambiguous", response.Metadata?["errorCode"]);
    }

    [Fact]
    public async Task DimsMismatch_FallsBackWithError()
    {
        var options = new TestOptionsMonitor(new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Hybrid,
            EmbeddingsEnabled = true,
            EmbeddingModel = "model-a"
        });

        var orchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new FakeChunkRepository(),
            new FakeEmbeddingRepository("model-a", 3),
            new ThrowingSymbolRepository(),
            new ThrowingSymbolSearchRepository(),
            new ThrowingEdgeRepository(),
            new ThrowingSymbolFingerprintRepository(),
            new TestEmbeddingProvider(),
            options);

        var response = await orchestrator.QueryAsync(
            query: "db",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: Convert.ToBase64String(new byte[8]),
            queryEmbeddingDims: 2,
            queryEmbeddingModel: null);

        Assert.Equal("embedding_dims_mismatch", response.Metadata?["errorCode"]);
    }

    private sealed class FakeChunkRepository : ICodeChunkRepository
    {
        public Task<IEnumerable<CodeChunk>> SearchFullTextAsync(
            string repoId,
            string query,
            string? branchName = null,
            Language? language = null,
            int limit = 50,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<CodeChunk>>(new[]
            {
                new CodeChunk
                {
                    Id = "chunk1",
                    RepositoryName = repoId,
                    BranchName = branchName ?? "main",
                    CommitSha = "sha",
                    FilePath = "src/Db.cs",
                    Language = Language.CSharp,
                    Content = "database",
                    StartLine = 1,
                    EndLine = 2,
                    ChunkStartLine = 1,
                    ChunkEndLine = 2
                }
            });
        }

        public Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetBySymbolAsync(string symbolId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByLanguageAsync(string repoId, Language language, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CodeChunk> CreateAsync(CodeChunk chunk, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CreateBatchAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeEmbeddingRepository : IEmbeddingRepository
    {
        private readonly List<string> _models;
        private readonly int _dims;

        public FakeEmbeddingRepository(string primaryModel, int dims, string? secondaryModel = null)
        {
            _models = new List<string> { primaryModel };
            if (!string.IsNullOrWhiteSpace(secondaryModel))
            {
                _models.Add(secondaryModel);
            }
            _dims = dims;
        }

        public Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_models);

        public Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
            => Task.FromResult<int?>(_dims);

        public Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
            => Task.FromResult(_models.Contains(model));

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

    private sealed class ThrowingSymbolRepository : ISymbolRepository
    {
        public Task<Symbol?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> GetByIdsAsync(IEnumerable<string> symbolIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> SearchByNameAsync(string repoId, string query, string? branchName = null, bool fuzzy = false, int limit = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Symbol>> GetByKindAsync(string repoId, SymbolKind kind, int limit = 100, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Symbol> CreateAsync(Symbol symbol, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CreateBatchAsync(IEnumerable<Symbol> symbols, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class ThrowingSymbolSearchRepository : ISymbolSearchRepository
    {
        public Task<IEnumerable<(string SymbolId, float Score, string? Snippet)>> SearchAsync(string repoId, string query, string? branchName, int limit, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyDictionary<string, string?>> GetSnippetsBySymbolIdsAsync(
            IEnumerable<string> symbolIds,
            CancellationToken cancellationToken = default)
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

    private sealed class ThrowingSymbolFingerprintRepository : ISymbolFingerprintRepository
    {
        public Task<SymbolFingerprintEntry?> GetBySymbolIdAsync(string symbolId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<(string SymbolId, ulong Fingerprint)>> FindCandidatesAsync(
            string repoId,
            string branchName,
            Language language,
            SymbolKind kind,
            string fingerprintKind,
            int band0,
            int band1,
            int band2,
            int band3,
            int limit,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<int> CreateBatchAsync(IEnumerable<SymbolFingerprintEntry> entries, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
