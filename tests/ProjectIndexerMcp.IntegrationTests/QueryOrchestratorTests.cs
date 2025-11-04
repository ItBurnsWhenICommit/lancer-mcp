using FluentAssertions;
using ProjectIndexerMcp.Models;
using Xunit;

namespace ProjectIndexerMcp.IntegrationTests;

/// <summary>
/// Integration tests for QueryOrchestrator using pre-indexed fixtures.
/// These tests verify the complete query pipeline against real data.
/// </summary>
[Trait("Category", "Integration")]
public class QueryOrchestratorTests : FixtureTestBase
{
    [Fact]
    public async Task Database_ShouldContainIndexedData()
    {
        // Verify fixtures are properly loaded
        await VerifyDatabaseHasData();

        // Verify specific repositories exist
        var repos = await QueryAsync<string>("SELECT DISTINCT repo_id FROM symbols");
        repos.Should().Contain("project-indexer-mcp");
    }

    [Fact]
    public async Task GetRepositoryStats_ProjectIndexerMcp_ShouldHaveSymbols()
    {
        // Arrange & Act
        var stats = await GetRepositoryStatsAsync("project-indexer-mcp");

        // Assert
        stats.SymbolCount.Should().BeGreaterThan(0, "project-indexer-mcp should have indexed symbols");
        stats.FileCount.Should().BeGreaterThan(0, "project-indexer-mcp should have indexed files");

        Console.WriteLine($"project-indexer-mcp stats:");
        Console.WriteLine($"  Symbols: {stats.SymbolCount}");
        Console.WriteLine($"  Files: {stats.FileCount}");
        Console.WriteLine($"  Chunks: {stats.ChunkCount}");
        Console.WriteLine($"  Embeddings: {stats.EmbeddingCount}");
        Console.WriteLine($"  Edges: {stats.EdgeCount}");
    }

    [Fact]
    public async Task SymbolRepository_SearchByName_ShouldFindQueryOrchestrator()
    {
        // Arrange & Act
        var symbols = await QueryAsync<Symbol>(@"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE name ILIKE @Name
            LIMIT 10",
            new { Name = "%QueryOrchestrator%" });

        // Assert
        symbols.Should().NotBeEmpty("QueryOrchestrator class should be indexed");
        symbols.Should().Contain(s => s.Name == "QueryOrchestrator" && s.Kind == SymbolKind.Class);
    }

    [Fact]
    public async Task SymbolRepository_GetByQualifiedName_ShouldFindSpecificSymbol()
    {
        // Arrange & Act
        var symbol = await QueryAsync<Symbol>(@"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE qualified_name ILIKE @QualifiedName
            LIMIT 1",
            new { QualifiedName = "%QueryOrchestrator%" });

        // Assert
        var result = symbol.FirstOrDefault();
        result.Should().NotBeNull("QueryOrchestrator should be indexed with qualified name");
        result!.Name.Should().Be("QueryOrchestrator");
        result.Kind.Should().Be(SymbolKind.Class);
        result.Language.Should().Be(Language.CSharp);
    }

    [Fact]
    public async Task EdgeRepository_GetOutgoingEdges_ShouldFindRelationships()
    {
        // Arrange - Find QueryOrchestrator.QueryAsync method (methods have edges, not the class itself)
        var symbol = await QueryAsync<Symbol>(@"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE qualified_name LIKE @QualifiedName
            LIMIT 1",
            new { QualifiedName = "ProjectIndexerMcp.Services.QueryOrchestrator.QueryAsync%" });

        var queryAsyncMethod = symbol.FirstOrDefault();
        queryAsyncMethod.Should().NotBeNull("QueryOrchestrator.QueryAsync method should exist in the database");

        // Act - Get outgoing edges
        var edgeCount = await ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM edges WHERE source_symbol_id = @SourceId",
            new { SourceId = queryAsyncMethod!.Id });

        // Assert
        edgeCount.Should().BeGreaterThan(0, "QueryAsync method should have outgoing edges (calls to other methods)");

        Console.WriteLine($"QueryOrchestrator.QueryAsync has {edgeCount} outgoing edges");
    }

    [Fact]
    public async Task CodeChunks_ShouldExistInDatabase()
    {
        // Arrange & Act
        var chunkCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM code_chunks");

        // Assert
        chunkCount.Should().BeGreaterThan(0, "fixtures should contain code chunks");

        Console.WriteLine($"Database contains {chunkCount} code chunks");
    }

    [Fact]
    public async Task Embeddings_ShouldExistInDatabase()
    {
        // Arrange & Act
        var embeddingCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM embeddings");

        // Assert
        embeddingCount.Should().BeGreaterThan(0, "fixtures should contain embeddings");

        Console.WriteLine($"Database contains {embeddingCount} embeddings");
    }

    [Fact]
    public async Task FullTextSearch_ShouldFindRelevantChunks()
    {
        // Arrange & Act
        var results = await QueryAsync<dynamic>(@"
            SELECT id, content,
                   ts_rank(to_tsvector('english', content), plainto_tsquery('english', @Query)) as rank
            FROM code_chunks
            WHERE to_tsvector('english', content) @@ plainto_tsquery('english', @Query)
            ORDER BY rank DESC
            LIMIT 10",
            new { Query = "query orchestrator" });

        // Assert
        results.Should().NotBeEmpty("should find chunks related to query orchestrator");

        Console.WriteLine($"Full-text search found {results.Count()} results");
    }
}

