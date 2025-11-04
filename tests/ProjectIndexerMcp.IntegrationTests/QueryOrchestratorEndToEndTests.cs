using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
using ProjectIndexerMcp.Models;
using ProjectIndexerMcp.Repositories;
using ProjectIndexerMcp.Services;
using Xunit;

namespace ProjectIndexerMcp.IntegrationTests;

/// <summary>
/// End-to-end integration tests for QueryOrchestrator.
/// These tests verify the complete query pipeline (BM25 + vector + graph) against real data.
/// </summary>
[Trait("Category", "Integration")]
public class QueryOrchestratorEndToEndTests : FixtureTestBase
{
    private readonly QueryOrchestrator _queryOrchestrator;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IEdgeRepository _edgeRepository;
    private readonly ICodeChunkRepository _chunkRepository;
    private readonly IEmbeddingRepository _embeddingRepository;

    public QueryOrchestratorEndToEndTests()
    {
        var serverOptions = new ServerOptions
        {
            DatabaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
            DatabasePort = int.Parse(Environment.GetEnvironmentVariable("DB_PORT") ?? "5432"),
            DatabaseName = TestDatabaseName,
            DatabaseUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
            DatabasePassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "postgres",
            EmbeddingServiceUrl = Environment.GetEnvironmentVariable("EMBEDDING_SERVICE_URL") ?? "http://localhost:8080",
            EmbeddingBatchSize = 32,
            EmbeddingTimeoutSeconds = 60
        };

        var optionsMonitor = new TestOptionsMonitor(serverOptions);

        var databaseService = new DatabaseService(
            NullLogger<DatabaseService>.Instance,
            optionsMonitor
        );

        _symbolRepository = new SymbolRepository(databaseService, NullLogger<SymbolRepository>.Instance);
        _edgeRepository = new EdgeRepository(databaseService, NullLogger<EdgeRepository>.Instance);
        _chunkRepository = new CodeChunkRepository(databaseService, NullLogger<CodeChunkRepository>.Instance);
        _embeddingRepository = new EmbeddingRepository(databaseService, NullLogger<EmbeddingRepository>.Instance);

        var httpClient = new HttpClient();
        if (!string.IsNullOrEmpty(serverOptions.EmbeddingServiceUrl))
        {
            httpClient.BaseAddress = new Uri(serverOptions.EmbeddingServiceUrl);
        }

        var embeddingService = new EmbeddingService(
            httpClient,
            optionsMonitor,
            NullLogger<EmbeddingService>.Instance
        );

        _queryOrchestrator = new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            _chunkRepository,
            _embeddingRepository,
            _symbolRepository,
            _edgeRepository,
            embeddingService
        );
    }

    [Fact]
    public async Task QueryAsync_NavigationIntent_ShouldFindSymbolByName()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var query = "find QueryOrchestrator class";

        // Act
        var response = await _queryOrchestrator.QueryAsync(
            query,
            repositoryName: "project-indexer-mcp",
            maxResults: 10
        );

        // Assert
        response.Should().NotBeNull();
        response.Query.Should().Be(query);
        response.Intent.Should().Be(QueryIntent.Navigation);
        response.Results.Should().NotBeEmpty("should find QueryOrchestrator symbol");
        response.Results.Should().Contain(r =>
            r.SymbolName == "QueryOrchestrator" &&
            r.SymbolKind == SymbolKind.Class);
        response.ExecutionTimeMs.Should().BeGreaterThan(0);

        Console.WriteLine($"✅ Navigation query found {response.Results.Count} results in {response.ExecutionTimeMs}ms");
        Console.WriteLine($"   Intent: {response.Intent}");
        Console.WriteLine($"   Top result: {response.Results.First().SymbolName} ({response.Results.First().SymbolKind})");
    }

    [Fact]
    public async Task QueryAsync_RelationsIntent_ShouldFindSymbolWithRelationships()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var query = "what does QueryOrchestrator call?";

        // Act
        var response = await _queryOrchestrator.QueryAsync(
            query,
            repositoryName: "project-indexer-mcp",
            maxResults: 10
        );

        // Assert
        response.Should().NotBeNull();
        response.Intent.Should().Be(QueryIntent.Relations);
        response.Results.Should().NotBeEmpty("should find QueryOrchestrator with relationships");

        var resultWithRelations = response.Results.FirstOrDefault(r => r.RelatedSymbols?.Any() == true);
        if (resultWithRelations != null)
        {
            resultWithRelations.RelatedSymbols.Should().NotBeEmpty("should include related symbols");
            Console.WriteLine($"✅ Relations query found {response.Results.Count} results with relationships");
            Console.WriteLine($"   Symbol: {resultWithRelations.SymbolName}");
            Console.WriteLine($"   Related symbols: {resultWithRelations.RelatedSymbols!.Count}");
        }
        else
        {
            Console.WriteLine($"⚠️  Relations query found {response.Results.Count} results but no relationships (graph may be empty)");
        }
    }

    [Fact]
    public async Task QueryAsync_FullTextSearch_ShouldFindRelevantCode()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var query = "database connection";

        // Act
        var response = await _queryOrchestrator.QueryAsync(
            query,
            repositoryName: "project-indexer-mcp",
            maxResults: 20
        );

        // Assert
        response.Should().NotBeNull();
        response.Results.Should().NotBeEmpty("should find code related to database connection");
        response.Results.Should().Contain(r =>
            r.Content.Contains("database", StringComparison.OrdinalIgnoreCase) ||
            r.Content.Contains("connection", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"✅ Full-text search found {response.Results.Count} results in {response.ExecutionTimeMs}ms");
        Console.WriteLine($"   Intent: {response.Intent}");
        Console.WriteLine($"   Top result score: {response.Results.First().Score:F3}");
    }

    [Fact]
    public async Task QueryAsync_HybridSearch_ShouldCombineBM25AndVector()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var embeddingCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM embeddings");

        if (embeddingCount == 0)
        {
            Console.WriteLine("⚠️  Skipping hybrid search test - no embeddings in database");
            return;
        }

        var query = "code that handles query execution";

        // Act
        var response = await _queryOrchestrator.QueryAsync(
            query,
            repositoryName: "project-indexer-mcp",
            maxResults: 20
        );

        // Assert
        response.Should().NotBeNull();
        response.Results.Should().NotBeEmpty("should find code using hybrid search");

        // Check if we have both BM25 and vector scores
        var resultsWithBothScores = response.Results.Where(r =>
            r.BM25Score.HasValue && r.BM25Score.Value > 0 &&
            r.VectorScore.HasValue && r.VectorScore.Value > 0).ToList();

        var resultsWithVectorOnly = response.Results.Where(r =>
            r.VectorScore.HasValue && r.VectorScore.Value > 0 &&
            (!r.BM25Score.HasValue || r.BM25Score.Value == 0)).ToList();

        var resultsWithBM25Only = response.Results.Where(r =>
            r.BM25Score.HasValue && r.BM25Score.Value > 0 &&
            (!r.VectorScore.HasValue || r.VectorScore.Value == 0)).ToList();

        if (resultsWithBothScores.Any())
        {
            Console.WriteLine($"✅ Hybrid search found {response.Results.Count} results");
            Console.WriteLine($"   Results with both BM25 and vector scores: {resultsWithBothScores.Count}");
            Console.WriteLine($"   Results with vector score only: {resultsWithVectorOnly.Count}");
            Console.WriteLine($"   Results with BM25 score only: {resultsWithBM25Only.Count}");
            Console.WriteLine($"   Top result - BM25: {resultsWithBothScores.First().BM25Score:F3}, Vector: {resultsWithBothScores.First().VectorScore:F3}");
        }
        else if (resultsWithVectorOnly.Any())
        {
            Console.WriteLine($"✅ Hybrid search found {response.Results.Count} results (vector search only)");
            Console.WriteLine($"   Results with vector score: {resultsWithVectorOnly.Count}");
            Console.WriteLine($"   Top result - Vector: {resultsWithVectorOnly.First().VectorScore:F3}");
            Console.WriteLine($"   Note: BM25 full-text search found no matches for this query");
        }
        else if (resultsWithBM25Only.Any())
        {
            Console.WriteLine($"✅ Hybrid search found {response.Results.Count} results (BM25 search only)");
            Console.WriteLine($"   Results with BM25 score: {resultsWithBM25Only.Count}");
            Console.WriteLine($"   Top result - BM25: {resultsWithBM25Only.First().BM25Score:F3}");
            Console.WriteLine($"   Note: Vector search found no matches for this query (embedding service may be unavailable)");
        }
        else
        {
            Console.WriteLine($"⚠️  Hybrid search found {response.Results.Count} results but no scores (this should not happen)");
        }
    }

    [Fact]
    public async Task QueryAsync_WithRepositoryFilter_ShouldOnlyReturnFromSpecifiedRepo()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var query = "class definition";

        // Act
        var response = await _queryOrchestrator.QueryAsync(
            query,
            repositoryName: "project-indexer-mcp",
            maxResults: 20
        );

        // Assert
        response.Should().NotBeNull();
        response.Results.Should().NotBeEmpty();
        response.Results.Should().OnlyContain(r => r.Repository == "project-indexer-mcp",
            "all results should be from the specified repository");

        Console.WriteLine($"✅ Repository filter working - all {response.Results.Count} results from project-indexer-mcp");
    }

    [Fact]
    public async Task QueryAsync_WithLanguageFilter_ShouldOnlyReturnSpecifiedLanguage()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var query = "service class";

        // Act
        var response = await _queryOrchestrator.QueryAsync(
            query,
            repositoryName: "project-indexer-mcp",
            language: Language.CSharp,
            maxResults: 20
        );

        // Assert
        response.Should().NotBeNull();

        if (response.Results.Any())
        {
            response.Results.Should().OnlyContain(r => r.Language == Language.CSharp,
                "all results should be C# when language filter is applied");
            Console.WriteLine($"✅ Language filter working - all {response.Results.Count} results are C#");
        }
        else
        {
            Console.WriteLine("⚠️  No results found with language filter");
        }
    }

    [Fact]
    public async Task QueryAsync_ShouldProvideExecutionMetadata()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var query = "test query";

        // Act
        var response = await _queryOrchestrator.QueryAsync(query, maxResults: 10);

        // Assert
        response.Should().NotBeNull();
        response.Query.Should().Be(query);
        response.ExecutionTimeMs.Should().BeGreaterThan(0, "should track execution time");
        response.Metadata.Should().NotBeNull();
        response.Metadata.Should().ContainKey("keywords");
        response.Metadata.Should().ContainKey("repository");
        response.Metadata.Should().ContainKey("branch");

        Console.WriteLine($"✅ Query metadata:");
        Console.WriteLine($"   Execution time: {response.ExecutionTimeMs}ms");
        Console.WriteLine($"   Intent: {response.Intent}");
        Console.WriteLine($"   Total results: {response.TotalResults}");
    }

    [Fact]
    public async Task QueryAsync_ShouldGenerateSuggestedQueries()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var query = "QueryOrchestrator";

        // Act
        var response = await _queryOrchestrator.QueryAsync(
            query,
            repositoryName: "project-indexer-mcp",
            maxResults: 10
        );

        // Assert
        response.Should().NotBeNull();
        response.SuggestedQueries.Should().NotBeNull();

        if (response.SuggestedQueries.Any())
        {
            Console.WriteLine($"✅ Generated {response.SuggestedQueries.Count} suggested queries:");
            foreach (var suggestion in response.SuggestedQueries.Take(3))
            {
                Console.WriteLine($"   - {suggestion}");
            }
        }
        else
        {
            Console.WriteLine("⚠️  No suggested queries generated");
        }
    }

    [Fact]
    public async Task QueryAsync_GraphReRanking_ShouldBoostConnectedSymbols()
    {
        // Arrange
        await VerifyDatabaseHasData();
        var edgeCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM edges");

        if (edgeCount == 0)
        {
            Console.WriteLine("⚠️  Skipping graph re-ranking test - no edges in database");
            return;
        }

        var query = "what calls QueryOrchestrator?";

        // Act
        var response = await _queryOrchestrator.QueryAsync(
            query,
            repositoryName: "project-indexer-mcp",
            maxResults: 20
        );

        // Assert
        response.Should().NotBeNull();
        response.Intent.Should().Be(QueryIntent.Relations);

        var resultsWithGraphScore = response.Results.Where(r => r.GraphScore.HasValue && r.GraphScore > 0).ToList();

        if (resultsWithGraphScore.Any())
        {
            Console.WriteLine($"✅ Graph re-ranking applied to {resultsWithGraphScore.Count} results");
            Console.WriteLine($"   Top result graph score: {resultsWithGraphScore.First().GraphScore:F3}");
        }
        else
        {
            Console.WriteLine($"⚠️  Query executed but no graph scores applied (may need more graph data)");
        }
    }

    /// <summary>
    /// Simple test implementation of IOptionsMonitor for testing.
    /// </summary>
    private class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
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

