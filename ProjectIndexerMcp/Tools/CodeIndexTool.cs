using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using ProjectIndexerMcp.Services;

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
    public async Task<string> Query(
        [Description("Natural language query describing what you're looking for. Examples: 'find all classes that implement IRepository', 'show me the definition of UserService', 'what calls the Login method?', 'find recent changes in authentication code'")]
        string query,
        [Description("Specific repository name to search in. Must match one of the configured repository names.")]
        string repository,
        [Description("Optional: specific branch to search in. If not provided, uses the default branch.")]
        string? branch = null,
        [Description("Optional: maximum number of results to return (default: 50)")]
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing query: {Query} (repo: {Repo}, branch: {Branch})", query, repository, branch ?? "default");

            // Get repository state
            var repos = _gitTracker.GetRepositories();

            if (!repos.TryGetValue(repository, out var repoState))
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Repository '{repository}' not found",
                    availableRepositories = repos.Keys.ToArray()
                });
            }

            // Determine which branch to query
            var targetBranch = branch ?? repoState.DefaultBranch;

            // PHASE 1: Lazy on-demand branch tracking
            // If the branch isn't tracked yet, track it now
            try
            {
                await _gitTracker.EnsureBranchTrackedAsync(repository, targetBranch, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return JsonSerializer.Serialize(new
                {
                    error = ex.Message,
                    availableBranches = await _gitTracker.GetRemoteBranchesAsync(repository, cancellationToken)
                });
            }

            // TODO: Step 3-7 will implement the full query orchestration:
            // 1. Intent detection (search vs navigation vs relations)
            // 2. Query parsing and expansion
            // 3. Hybrid search (BM25 + vector + graph)
            // 4. Result ranking and context packaging
            // 5. Return formatted results

            // For now (Step 2), we can only provide repository status
            var result = new
            {
                query,
                repository,
                branch = targetBranch,
                status = "partial_implementation",
                message = "Full query orchestration not yet implemented. Currently showing repository status only.",
                note = "Phase 1 complete: Lazy on-demand branch tracking. Steps 3-7 will add: parsing, symbol extraction, PostgreSQL storage, embeddings, and hybrid search.",
                repositoryInfo = new
                {
                    name = repoState.Name,
                    remoteUrl = repoState.RemoteUrl,
                    defaultBranch = repoState.DefaultBranch,
                    isCloned = repoState.IsCloned,
                    lastUpdated = repoState.LastUpdated,
                    trackedBranches = repoState.Branches.Select(b => new
                    {
                        name = b.Value.Name,
                        currentSha = b.Value.CurrentSha,
                        lastIndexedSha = b.Value.LastIndexedSha,
                        lastIndexed = b.Value.LastIndexed,
                        lastAccessed = b.Value.LastAccessed,
                        needsIndexing = b.Value.NeedsIndexing,
                        isQueried = b.Key.Equals(targetBranch, StringComparison.OrdinalIgnoreCase)
                    }).ToArray()
                }
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
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

            return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}

