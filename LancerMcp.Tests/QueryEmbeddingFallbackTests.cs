using System;
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

public sealed class QueryEmbeddingFallbackTests
{
    [Fact]
    public async Task Query_DoesNotGenerateEmbeddings()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Hybrid, EmbeddingsEnabled = true };
        var provider = new FakeEmbeddingProvider(isAvailable: true);
        var orchestrator = CreateOrchestrator(options, provider, new FakeEmbeddingRepository("model-a", 2, hasEmbeddings: true));

        await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid);

        Assert.Equal(0, provider.QueryEmbeddingCalls);
        Assert.Equal(0, provider.BatchEmbeddingCalls);
    }

    [Fact]
    public async Task Hybrid_EmbeddingsDisabled_FallsBackAndMatchesFastOrdering()
    {
        var options = new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Hybrid,
            EmbeddingsEnabled = false,
            EmbeddingModel = "model-a"
        };
        var provider = new FakeEmbeddingProvider(isAvailable: true);
        var embeddingRepository = new FakeEmbeddingRepository("model-a", 2, hasEmbeddings: true);
        var orchestrator = CreateOrchestrator(options, provider, embeddingRepository);

        var hybrid = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid);

        var fast = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Fast);

        Assert.NotEmpty(hybrid.Results);
        Assert.Equal(EmbeddingFallbackCodes.EmbeddingsDisabled, hybrid.Metadata?["fallback"]);
        Assert.Equal(false, hybrid.Metadata?["embeddingUsed"]);
        Assert.Equal(GetResultIds(fast), GetResultIds(hybrid));
    }

    [Fact]
    public async Task Hybrid_ProviderUnavailable_FallsBackAndMatchesFastOrdering()
    {
        var options = new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Hybrid,
            EmbeddingsEnabled = true,
            EmbeddingModel = "model-a"
        };
        var provider = new FakeEmbeddingProvider(isAvailable: false);
        var embeddingRepository = new FakeEmbeddingRepository("model-a", 2, hasEmbeddings: true);
        var orchestrator = CreateOrchestrator(options, provider, embeddingRepository);
        var queryEmbedding = CreateEmbeddingBase64(2);

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

        Assert.NotEmpty(hybrid.Results);
        Assert.Equal(EmbeddingFallbackCodes.ProviderUnavailable, hybrid.Metadata?["fallback"]);
        Assert.Equal(false, hybrid.Metadata?["embeddingUsed"]);
        Assert.Equal(GetResultIds(fast), GetResultIds(hybrid));
    }

    [Fact]
    public async Task Semantic_MissingQueryEmbedding_FallsBackAndMatchesFastOrdering()
    {
        var options = new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Semantic,
            EmbeddingsEnabled = true,
            EmbeddingModel = "model-a"
        };
        var provider = new FakeEmbeddingProvider(isAvailable: true);
        var embeddingRepository = new FakeEmbeddingRepository("model-a", 2, hasEmbeddings: true);
        var orchestrator = CreateOrchestrator(options, provider, embeddingRepository);

        var semantic = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Semantic);

        var fast = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Fast);

        Assert.NotEmpty(semantic.Results);
        Assert.Equal(EmbeddingFallbackCodes.MissingQueryEmbedding, semantic.Metadata?["fallback"]);
        Assert.Equal(false, semantic.Metadata?["embeddingUsed"]);
        Assert.Equal(GetResultIds(fast), GetResultIds(semantic));
    }

    [Fact]
    public async Task Hybrid_QueryEmbeddingInvalid_FallsBack()
    {
        var options = new ServerOptions
        {
            DefaultRetrievalProfile = RetrievalProfile.Hybrid,
            EmbeddingsEnabled = true,
            EmbeddingModel = "model-a"
        };
        var provider = new FakeEmbeddingProvider(isAvailable: true);
        var embeddingRepository = new FakeEmbeddingRepository("model-a", 3, hasEmbeddings: true);
        var orchestrator = CreateOrchestrator(options, provider, embeddingRepository);
        var queryEmbedding = CreateEmbeddingBase64(2);

        var response = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: queryEmbedding,
            queryEmbeddingDims: 2,
            queryEmbeddingModel: "model-a");

        Assert.NotEmpty(response.Results);
        Assert.Equal(EmbeddingFallbackCodes.QueryEmbeddingInvalid, response.Metadata?["fallback"]);
        Assert.Equal(false, response.Metadata?["embeddingUsed"]);
    }

    private static QueryOrchestrator CreateOrchestrator(
        ServerOptions options,
        IEmbeddingProvider provider,
        IEmbeddingRepository embeddingRepository)
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
            provider,
            optionsMonitor);
    }

    private static string CreateEmbeddingBase64(int dims)
    {
        var bytes = new byte[dims * 4];
        return Convert.ToBase64String(bytes);
    }

    private static IReadOnlyList<string> GetResultIds(QueryResponse response)
        => response.Results.Select(result => result.Id).ToList();

    private sealed class FakeChunkRepository : ICodeChunkRepository
    {
        private static readonly IReadOnlyList<CodeChunk> Chunks = new[]
        {
            new CodeChunk
            {
                Id = "chunk1",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/Db.cs",
                Language = Language.CSharp,
                Content = "database connection",
                StartLine = 1,
                EndLine = 2,
                ChunkStartLine = 1,
                ChunkEndLine = 2
            },
            new CodeChunk
            {
                Id = "chunk2",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/Db2.cs",
                Language = Language.CSharp,
                Content = "database connection",
                StartLine = 3,
                EndLine = 4,
                ChunkStartLine = 3,
                ChunkEndLine = 4
            }
        };

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
                ("chunk1", 1.0f, (string?)"database connection"),
                ("chunk2", 0.9f, (string?)"database connection")
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
                Id = "chunk1",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/Db.cs",
                Name = "Db",
                QualifiedName = "Db",
                Kind = SymbolKind.Class,
                Language = Language.CSharp,
                StartLine = 1,
                EndLine = 2,
                StartColumn = 1,
                EndColumn = 1
            },
            new Symbol
            {
                Id = "chunk2",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/Db2.cs",
                Name = "Db2",
                QualifiedName = "Db2",
                Kind = SymbolKind.Class,
                Language = Language.CSharp,
                StartLine = 3,
                EndLine = 4,
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

    private sealed class FakeEmbeddingRepository : IEmbeddingRepository
    {
        private readonly string _model;
        private readonly int? _dims;
        private readonly bool _hasEmbeddings;

        public FakeEmbeddingRepository(string model, int? dims, bool hasEmbeddings)
        {
            _model = model;
            _dims = dims;
            _hasEmbeddings = hasEmbeddings;
        }

        public Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { _model });

        public Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
            => Task.FromResult(_dims);

        public Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
            => Task.FromResult(_hasEmbeddings && string.Equals(model, _model, StringComparison.Ordinal));

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

    private sealed class FakeEmbeddingProvider : IEmbeddingProvider
    {
        private readonly bool _isAvailable;

        public FakeEmbeddingProvider(bool isAvailable)
        {
            _isAvailable = isAvailable;
        }

        public int QueryEmbeddingCalls { get; private set; }
        public int BatchEmbeddingCalls { get; private set; }

        public bool IsAvailable => _isAvailable;

        public Task<EmbeddingProviderResult> TryGenerateQueryEmbeddingAsync(string input, CancellationToken cancellationToken)
        {
            QueryEmbeddingCalls++;
            return Task.FromResult(new EmbeddingProviderResult(
                IsSuccess: false,
                IsTransientFailure: true,
                ErrorCode: "provider_unavailable",
                ErrorMessage: "Embedding provider unavailable.",
                Dims: null,
                Vector: null,
                Embeddings: Array.Empty<Embedding>()));
        }

        public Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken)
        {
            BatchEmbeddingCalls++;
            return Task.FromResult(new EmbeddingProviderResult(
                IsSuccess: false,
                IsTransientFailure: true,
                ErrorCode: "provider_unavailable",
                ErrorMessage: "Embedding provider unavailable.",
                Dims: null,
                Vector: null,
                Embeddings: Array.Empty<Embedding>()));
        }
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
