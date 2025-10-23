using ModelContextProtocol.Server;
using ProjectIndexerMcp.Services;
using System.ComponentModel;
using System.Text.Json;

namespace ProjectIndexerMcp.Tools;

/// <summary>
/// The single unified MCP tool for code indexing and querying.
/// This tool handles all query types server-side: search, navigation, relations, etc.
/// </summary>
[McpServerToolType]
public sealed class CodeIndexTool
{
    private readonly GitTrackerService _gitTracker;
    private readonly ILogger<CodeIndexTool> _logger;

    public CodeIndexTool(GitTrackerService gitTracker, ILogger<CodeIndexTool> logger)
    {
        _gitTracker = gitTracker;
        _logger = logger;
    }

    /// <summary>
    /// Unified query interface for code indexing operations.
    /// The server interprets the query intent and returns the best results.
    /// </summary>
    [McpServerTool]
    [Description("Query the code index. Supports natural language queries for code search, symbol lookup, finding references, call graphs, and code navigation. The server interprets your intent and returns relevant results.")]
    public Task<string> Query(
        [Description("Natural language query describing what you're looking for. Examples: 'find all classes that implement IRepository', 'show me the definition of UserService', 'what calls the Login method?', 'find recent changes in authentication code'")]
        string query,
        [Description("Optional: specific repository name to search in. If not provided, searches all tracked repositories.")]
        string? repository = null,
        [Description("Optional: specific branch to search in. If not provided, uses the default branch.")]
        string? branch = null,
        [Description("Optional: maximum number of results to return (default: 50)")]
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing query: {Query} (repo: {Repo}, branch: {Branch})", query, repository ?? "all", branch ?? "default");

            // TODO: Step 3-7 will implement the full query orchestration:
            // 1. Intent detection (search vs navigation vs relations)
            // 2. Query parsing and expansion
            // 3. Hybrid search (BM25 + vector + graph)
            // 4. Result ranking and context packaging
            // 5. Return formatted results

            // For now (Step 2), we can only provide repository status
            var repos = _gitTracker.GetRepositories();

            // Filter by repository if specified
            var filteredRepos = repository != null
                ? repos.Where(r => r.Key.Equals(repository, StringComparison.OrdinalIgnoreCase))
                : repos;

            var result = new
            {
                query,
                status = "partial_implementation",
                message = "Full query orchestration not yet implemented. Currently showing repository status only.",
                note = "Steps 3-7 will add: parsing, symbol extraction, PostgreSQL storage, embeddings, and hybrid search.",
                repositories = filteredRepos.Select(r => new
                {
                    name = r.Value.Name,
                    remoteUrl = r.Value.RemoteUrl,
                    defaultBranch = r.Value.DefaultBranch,
                    isCloned = r.Value.IsCloned,
                    lastUpdated = r.Value.LastUpdated,
                    branches = r.Value.Branches
                        .Where(b => branch == null || b.Key.Equals(branch, StringComparison.OrdinalIgnoreCase))
                        .Select(b => new
                        {
                            name = b.Value.Name,
                            currentSha = b.Value.CurrentSha,
                            lastIndexedSha = b.Value.LastIndexedSha,
                            lastIndexed = b.Value.LastIndexed,
                            needsIndexing = b.Value.NeedsIndexing
                        }).ToArray()
                }).ToArray()
            };

            return Task.FromResult(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process query: {Query}", query);

            var errorResult = new
            {
                query,
                success = false,
                error = ex.Message,
                errorType = ex.GetType().Name
            };

            return Task.FromResult(JsonSerializer.Serialize(errorResult, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

