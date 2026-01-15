using System.Net.Http;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryOrchestratorSimilarityTests
{
    [Fact]
    public async Task QueryAsync_SimilarQuery_DoesNotUseSymbolSearch()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Fast };
        var optionsMonitor = new TestOptionsMonitor(options);
        var symbolSearchRepository = new ThrowingSymbolSearchRepository();

        var orchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new ThrowingCodeChunkRepository(),
            new ThrowingEmbeddingRepository(),
            new FakeSymbolRepository(null),
            symbolSearchRepository,
            new ThrowingEdgeRepository(),
            new FakeFingerprintRepository(null),
            new EmbeddingService(new HttpClient(), optionsMonitor, NullLogger<EmbeddingService>.Instance),
            optionsMonitor);

        var response = await orchestrator.QueryAsync(
            query: "similar:missing-id",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Fast);

        Assert.Empty(response.Results);
    }

    [Fact]
    public async Task QueryAsync_SimilarSeedMissing_ReturnsErrorMetadata()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Fast };
        var optionsMonitor = new TestOptionsMonitor(options);

        var orchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new ThrowingCodeChunkRepository(),
            new ThrowingEmbeddingRepository(),
            new FakeSymbolRepository(null),
            new FakeSymbolSearchRepository(),
            new ThrowingEdgeRepository(),
            new FakeFingerprintRepository(null),
            new EmbeddingService(new HttpClient(), optionsMonitor, NullLogger<EmbeddingService>.Instance),
            optionsMonitor);

        var response = await orchestrator.QueryAsync(
            query: "similar:missing-id",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Fast);

        Assert.Empty(response.Results);
        Assert.Equal("seed_not_found", response.Metadata?["errorCode"]);
        Assert.NotNull(response.Metadata?["error"]);
    }

    [Fact]
    public async Task QueryAsync_SimilarSeedMissingFingerprint_ReturnsErrorMetadata()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Fast };
        var optionsMonitor = new TestOptionsMonitor(options);
        var seedSymbol = new Symbol
        {
            Id = "seed-id",
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "src/Seed.cs",
            Name = "Seed",
            QualifiedName = "Repo.Seed",
            Kind = SymbolKind.Method,
            Language = Language.CSharp,
            StartLine = 1,
            StartColumn = 1,
            EndLine = 3,
            EndColumn = 1
        };

        var orchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new ThrowingCodeChunkRepository(),
            new ThrowingEmbeddingRepository(),
            new FakeSymbolRepository(seedSymbol),
            new FakeSymbolSearchRepository(),
            new ThrowingEdgeRepository(),
            new FakeFingerprintRepository(null),
            new EmbeddingService(new HttpClient(), optionsMonitor, NullLogger<EmbeddingService>.Instance),
            optionsMonitor);

        var response = await orchestrator.QueryAsync(
            query: "similar:seed-id",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Fast);

        Assert.Empty(response.Results);
        Assert.Equal("seed_fingerprint_missing", response.Metadata?["errorCode"]);
        Assert.NotNull(response.Metadata?["error"]);
    }

    [Fact]
    public async Task QueryAsync_SimilarSeedWithCandidates_ReturnsClosestExcludingSeed()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Fast };
        var optionsMonitor = new TestOptionsMonitor(options);
        var seedSymbol = new Symbol
        {
            Id = "seed-id",
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "src/Seed.cs",
            Name = "Seed",
            QualifiedName = "Repo.Seed",
            Kind = SymbolKind.Method,
            Language = Language.CSharp,
            StartLine = 1,
            StartColumn = 1,
            EndLine = 3,
            EndColumn = 1
        };

        var seedFingerprint = new SymbolFingerprintEntry
        {
            SymbolId = "seed-id",
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "src/Seed.cs",
            Language = Language.CSharp,
            Kind = SymbolKind.Method,
            FingerprintKind = SimHashService.FingerprintKind,
            Fingerprint = 0UL,
            Band0 = 0,
            Band1 = 0,
            Band2 = 0,
            Band3 = 0
        };

        var candidates = new[]
        {
            ("seed-id", 0UL),
            ("cand-close", 1UL),
            ("cand-far", 3UL)
        };

        var symbols = new Dictionary<string, Symbol>
        {
            ["seed-id"] = seedSymbol,
            ["cand-close"] = new Symbol
            {
                Id = "cand-close",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/Close.cs",
                Name = "AlphaMatch",
                QualifiedName = "Repo.AlphaMatch",
                Kind = SymbolKind.Method,
                Language = Language.CSharp,
                StartLine = 5,
                StartColumn = 1,
                EndLine = 7,
                EndColumn = 1
            },
            ["cand-far"] = new Symbol
            {
                Id = "cand-far",
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "src/Far.cs",
                Name = "BetaMatch",
                QualifiedName = "Repo.BetaMatch",
                Kind = SymbolKind.Method,
                Language = Language.CSharp,
                StartLine = 9,
                StartColumn = 1,
                EndLine = 11,
                EndColumn = 1
            }
        };

        var snippets = new Dictionary<string, string?>
        {
            ["cand-close"] = "alpha token appears here",
            ["cand-far"] = "beta token appears here"
        };

        var fingerprintRepository = new FakeFingerprintRepository(seedFingerprint, candidates);
        var symbolRepository = new FakeSymbolRepository(null, symbols);
        var symbolSearchRepository = new FakeSymbolSearchRepository(snippets);

        var orchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new ThrowingCodeChunkRepository(),
            new ThrowingEmbeddingRepository(),
            symbolRepository,
            symbolSearchRepository,
            new ThrowingEdgeRepository(),
            fingerprintRepository,
            new EmbeddingService(new HttpClient(), optionsMonitor, NullLogger<EmbeddingService>.Instance),
            optionsMonitor);

        var response = await orchestrator.QueryAsync(
            query: "similar:seed-id alpha",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Fast);

        Assert.Single(response.Results);
        Assert.Equal("cand-close", response.Results[0].Id);
        Assert.DoesNotContain(response.Results, r => r.Id == "seed-id");
        Assert.Equal("repo", fingerprintRepository.LastRepoId);
        Assert.Equal("main", fingerprintRepository.LastBranchName);
        Assert.Equal(Language.CSharp, fingerprintRepository.LastLanguage);
        Assert.Equal(SymbolKind.Method, fingerprintRepository.LastKind);
        Assert.Contains(response.Results[0].Reasons ?? new List<string>(), reason => reason == "similarity:simhash");
        Assert.Contains(response.Results[0].Reasons ?? new List<string>(), reason => reason.StartsWith("distance:", StringComparison.Ordinal));
    }

    private sealed class FakeSymbolRepository : ISymbolRepository
    {
        private readonly Symbol? _symbol;
        private readonly IReadOnlyDictionary<string, Symbol>? _symbols;

        public FakeSymbolRepository(Symbol? symbol, IReadOnlyDictionary<string, Symbol>? symbols = null)
        {
            _symbol = symbol;
            _symbols = symbols;
        }

        public Task<Symbol?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            if (_symbols != null && _symbols.TryGetValue(id, out var symbol))
            {
                return Task.FromResult<Symbol?>(symbol);
            }

            if (_symbol != null && _symbol.Id == id)
            {
                return Task.FromResult<Symbol?>(_symbol);
            }

            return Task.FromResult<Symbol?>(null);
        }

        public Task<IEnumerable<Symbol>> GetByIdsAsync(IEnumerable<string> symbolIds, CancellationToken cancellationToken = default)
        {
            if (_symbols != null)
            {
                var results = symbolIds.Select(id => _symbols.TryGetValue(id, out var symbol) ? symbol : null)
                    .Where(symbol => symbol != null)
                    .Cast<Symbol>()
                    .ToList();

                return Task.FromResult<IEnumerable<Symbol>>(results);
            }

            if (_symbol != null)
            {
                var results = symbolIds.Where(id => id == _symbol.Id).Select(_ => _symbol).ToList();
                return Task.FromResult<IEnumerable<Symbol>>(results);
            }

            return Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());
        }

        public Task<IEnumerable<Symbol>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> SearchByNameAsync(string repoId, string query, string? branchName = null, bool fuzzy = false, int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> GetByKindAsync(string repoId, SymbolKind kind, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<Symbol> CreateAsync(Symbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(symbol);

        public Task<int> CreateBatchAsync(IEnumerable<Symbol> symbols, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakeSymbolSearchRepository : ISymbolSearchRepository
    {
        private readonly IReadOnlyDictionary<string, string?> _snippets;

        public FakeSymbolSearchRepository()
            : this(new Dictionary<string, string?>())
        {
        }

        public FakeSymbolSearchRepository(IReadOnlyDictionary<string, string?> snippets)
        {
            _snippets = snippets;
        }

        public Task<IEnumerable<(string SymbolId, float Score, string? Snippet)>> SearchAsync(
            string repoId,
            string query,
            string? branchName,
            int limit,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<(string, float, string?)>>(Array.Empty<(string, float, string?)>());

        public Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyDictionary<string, string?>> GetSnippetsBySymbolIdsAsync(
            IEnumerable<string> symbolIds,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, string?>();
            foreach (var symbolId in symbolIds)
            {
                if (_snippets.TryGetValue(symbolId, out var snippet))
                {
                    result[symbolId] = snippet;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, string?>>(result);
        }
    }

    private sealed class ThrowingSymbolSearchRepository : ISymbolSearchRepository
    {
        public Task<IEnumerable<(string SymbolId, float Score, string? Snippet)>> SearchAsync(
            string repoId,
            string query,
            string? branchName,
            int limit,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Symbol search should not be used for similar: queries.");

        public Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<IReadOnlyDictionary<string, string?>> GetSnippetsBySymbolIdsAsync(
            IEnumerable<string> symbolIds,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());
    }

    private sealed class FakeFingerprintRepository : ISymbolFingerprintRepository
    {
        private readonly SymbolFingerprintEntry? _entry;
        private readonly IReadOnlyList<(string SymbolId, ulong Fingerprint)> _candidates;

        public FakeFingerprintRepository(
            SymbolFingerprintEntry? entry,
            IReadOnlyList<(string SymbolId, ulong Fingerprint)>? candidates = null)
        {
            _entry = entry;
            _candidates = candidates ?? Array.Empty<(string, ulong)>();
        }

        public string? LastRepoId { get; private set; }
        public string? LastBranchName { get; private set; }
        public Language LastLanguage { get; private set; }
        public SymbolKind LastKind { get; private set; }

        public Task<SymbolFingerprintEntry?> GetBySymbolIdAsync(string symbolId, CancellationToken cancellationToken = default)
            => Task.FromResult(_entry);

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
        {
            LastRepoId = repoId;
            LastBranchName = branchName;
            LastLanguage = language;
            LastKind = kind;
            return Task.FromResult<IEnumerable<(string, ulong)>>(_candidates);
        }

        public Task<int> CreateBatchAsync(IEnumerable<SymbolFingerprintEntry> entries, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
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
        public Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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

    private sealed class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
    {
        private readonly ServerOptions _options;

        public TestOptionsMonitor(ServerOptions options)
        {
            _options = options;
        }

        public ServerOptions CurrentValue => _options;

        public ServerOptions Get(string? name) => _options;

        public IDisposable? OnChange(Action<ServerOptions, string?> listener) => null;
    }
}
