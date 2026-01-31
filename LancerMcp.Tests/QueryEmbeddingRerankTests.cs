using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryEmbeddingRerankTests
{
    [Fact]
    public async Task Rerank_ChangesOrdering_WhenEmbeddingsExist()
    {
        var options = new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Hybrid,
            EmbeddingsEnabled = true,
            EmbeddingModel = "model-a"
        };
        var queryEmbedding = ToBase64(new[] { 1f, 0f });
        var embeddingRepository = new FakeEmbeddingRepository(
            model: "model-a",
            dims: 2,
            embeddings: new Dictionary<string, float[]>
            {
                ["chunk-a"] = new[] { 0f, 1f },
                ["chunk-b"] = new[] { 1f, 0f },
                ["chunk-c"] = new[] { 0f, 0f }
            });
        var orchestrator = CreateOrchestrator(options, embeddingRepository, isProviderAvailable: true);

        var response = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: queryEmbedding,
            queryEmbeddingDims: 2,
            queryEmbeddingModel: "model-a");

        Assert.Equal(new[] { "symbol-b", "symbol-a", "symbol-c" }, GetResultIds(response));
        Assert.True(response.Metadata?["embeddingUsed"] is bool used && used);
        Assert.Contains("rerank:semantic_boost", response.Results.First().Reasons ?? new List<string>());
    }

    [Fact]
    public async Task Rerank_FallsBack_WhenEmbeddingsMissing()
    {
        var options = new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Hybrid,
            EmbeddingsEnabled = true,
            EmbeddingModel = "model-a"
        };
        var queryEmbedding = ToBase64(new[] { 1f, 0f });
        var embeddingRepository = new FakeEmbeddingRepository(
            model: "model-a",
            dims: 2,
            embeddings: new Dictionary<string, float[]>());
        var orchestrator = CreateOrchestrator(options, embeddingRepository, isProviderAvailable: true);

        var hybrid = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: queryEmbedding,
            queryEmbeddingDims: 2,
            queryEmbeddingModel: "model-a");

        var fast = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Fast);

        Assert.Equal(GetResultIds(fast), GetResultIds(hybrid));
        Assert.Equal(EmbeddingFallbackCodes.QueryEmbeddingInvalid, hybrid.Metadata?["fallback"]);
        Assert.Equal(false, hybrid.Metadata?["embeddingUsed"]);
    }

    private static QueryOrchestrator CreateOrchestrator(
        ServerOptions options,
        IEmbeddingRepository embeddingRepository,
        bool isProviderAvailable)
    {
        IOptionsMonitor<ServerOptions> optionsMonitor = new TestOptionsMonitor(options);

        return new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new FakeChunkRepository(),
            embeddingRepository,
            new FakeSymbolRepository(),
            new FakeSymbolSearchRepository(),
            new ThrowingEdgeRepository(),
            new ThrowingSymbolFingerprintRepository(),
            new FakeEmbeddingProvider(isProviderAvailable),
            optionsMonitor);
    }

    private static string ToBase64(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        for (var i = 0; i < vector.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), vector[i]);
        }

        return Convert.ToBase64String(bytes);
    }

    private static IReadOnlyList<string> GetResultIds(QueryResponse response)
        => response.Results.Select(result => result.Id).ToList();

    private sealed class FakeSymbolSearchRepository : ISymbolSearchRepository
    {
        public Task<IEnumerable<(string SymbolId, float Score, string? Snippet)>> SearchAsync(
            string repoId,
            string query,
            string? branchName,
            int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<(string, float, string?)>>(new[]
            {
                ("symbol-a", 1.0f, (string?)"database connection"),
                ("symbol-b", 0.9f, (string?)"database connection"),
                ("symbol-c", 0.8f, (string?)"database connection")
            });
        }

        public Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyDictionary<string, string?>> GetSnippetsBySymbolIdsAsync(
            IEnumerable<string> symbolIds,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeSymbolRepository : ISymbolRepository
    {
        private static readonly IReadOnlyList<Symbol> Symbols = new[]
        {
            new Symbol
            {
                Id = "symbol-a",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/A.cs",
                Name = "SymbolA",
                QualifiedName = "SymbolA",
                Kind = SymbolKind.Class,
                Language = Language.CSharp,
                StartLine = 1,
                EndLine = 2,
                StartColumn = 1,
                EndColumn = 1
            },
            new Symbol
            {
                Id = "symbol-b",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/B.cs",
                Name = "SymbolB",
                QualifiedName = "SymbolB",
                Kind = SymbolKind.Class,
                Language = Language.CSharp,
                StartLine = 3,
                EndLine = 4,
                StartColumn = 1,
                EndColumn = 1
            },
            new Symbol
            {
                Id = "symbol-c",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/C.cs",
                Name = "SymbolC",
                QualifiedName = "SymbolC",
                Kind = SymbolKind.Class,
                Language = Language.CSharp,
                StartLine = 5,
                EndLine = 6,
                StartColumn = 1,
                EndColumn = 1
            }
        };

        public Task<Symbol?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<IEnumerable<Symbol>> GetByIdsAsync(IEnumerable<string> symbolIds, CancellationToken cancellationToken = default)
        {
            var set = symbolIds.ToHashSet();
            return Task.FromResult<IEnumerable<Symbol>>(Symbols.Where(symbol => set.Contains(symbol.Id)).ToList());
        }

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

    private sealed class FakeChunkRepository : ICodeChunkRepository
    {
        private static readonly IReadOnlyList<CodeChunk> Chunks = new[]
        {
            new CodeChunk
            {
                Id = "chunk-a",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/A.cs",
                SymbolId = "symbol-a",
                SymbolName = "SymbolA",
                SymbolKind = SymbolKind.Class,
                Language = Language.CSharp,
                Content = "database connection",
                StartLine = 1,
                EndLine = 2,
                ChunkStartLine = 1,
                ChunkEndLine = 2
            },
            new CodeChunk
            {
                Id = "chunk-b",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/B.cs",
                SymbolId = "symbol-b",
                SymbolName = "SymbolB",
                SymbolKind = SymbolKind.Class,
                Language = Language.CSharp,
                Content = "database connection",
                StartLine = 3,
                EndLine = 4,
                ChunkStartLine = 3,
                ChunkEndLine = 4
            },
            new CodeChunk
            {
                Id = "chunk-c",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/C.cs",
                SymbolId = "symbol-c",
                SymbolName = "SymbolC",
                SymbolKind = SymbolKind.Class,
                Language = Language.CSharp,
                Content = "database connection",
                StartLine = 5,
                EndLine = 6,
                ChunkStartLine = 5,
                ChunkEndLine = 6
            }
        };

        public Task<IEnumerable<CodeChunk>> GetBySymbolAsync(string symbolId, CancellationToken cancellationToken = default)
        {
            var chunks = Chunks.Where(chunk => string.Equals(chunk.SymbolId, symbolId, StringComparison.Ordinal)).ToList();
            return Task.FromResult<IEnumerable<CodeChunk>>(chunks);
        }

        public Task<IEnumerable<CodeChunk>> SearchFullTextAsync(
            string repoId,
            string query,
            string? branchName = null,
            Language? language = null,
            int limit = 50,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<CodeChunk>>(Chunks);

        public Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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
        private readonly string _model;
        private readonly int? _dims;
        private readonly IReadOnlyDictionary<string, float[]> _embeddings;

        public FakeEmbeddingRepository(string model, int? dims, IReadOnlyDictionary<string, float[]> embeddings)
        {
            _model = model;
            _dims = dims;
            _embeddings = embeddings;
        }

        public Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { _model });

        public Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
            => Task.FromResult(_dims);

        public Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
            => Task.FromResult(_embeddings.Count > 0 && string.Equals(model, _model, StringComparison.Ordinal));

        public Task<Embedding?> GetByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default)
        {
            if (!_embeddings.TryGetValue(chunkId, out var vector))
            {
                return Task.FromResult<Embedding?>(null);
            }

            return Task.FromResult<Embedding?>(CreateEmbedding(chunkId, vector));
        }

        public Task<IReadOnlyList<Embedding>> GetByChunkIdsAsync(
            string repoId,
            string? branchName,
            string model,
            IReadOnlyList<string> chunkIds,
            CancellationToken cancellationToken = default)
        {
            var matches = chunkIds
                .Where(id => _embeddings.ContainsKey(id))
                .Select(id => CreateEmbedding(id, _embeddings[id]))
                .ToList();

            return Task.FromResult<IReadOnlyList<Embedding>>(matches);
        }

        private static Embedding CreateEmbedding(string chunkId, float[] vector)
        {
            return new Embedding
            {
                Id = $"embedding-{chunkId}",
                ChunkId = chunkId,
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                Vector = vector,
                Model = "model-a",
                GeneratedAt = DateTimeOffset.UtcNow
            };
        }

        public Task<Embedding?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        public FakeEmbeddingProvider(bool isAvailable)
        {
            IsAvailable = isAvailable;
        }

        public bool IsAvailable { get; }

        public Task<EmbeddingProviderResult> TryGenerateQueryEmbeddingAsync(string input, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken)
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
