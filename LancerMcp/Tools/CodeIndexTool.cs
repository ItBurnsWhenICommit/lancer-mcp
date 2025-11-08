using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using LancerMcp.Services;

namespace LancerMcp.Tools;

/// <summary>
/// The single unified MCP tool for code indexing and querying.
/// This tool handles all query types server-side: search, navigation, relations, etc.
/// </summary>
[McpServerToolType]
public sealed class CodeIndexTool
{
    private readonly GitTrackerService _gitTracker;
    private readonly IndexingService _indexingService;
    private readonly QueryOrchestrator _queryOrchestrator;
    private readonly ILogger<CodeIndexTool> _logger;

    public CodeIndexTool(
        GitTrackerService gitTracker,
        IndexingService indexingService,
        QueryOrchestrator queryOrchestrator,
        ILogger<CodeIndexTool> logger)
    {
        _gitTracker = gitTracker;
        _indexingService = indexingService;
        _queryOrchestrator = queryOrchestrator;
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
        [Description("Optional: specific repository name to search in. If not provided, searches across all configured repositories. Must match one of the configured repository names.")]
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

            string? targetBranch = null;

            // If repository is specified, validate and handle branch tracking
            if (!string.IsNullOrEmpty(repository))
            {
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
                targetBranch = branch ?? repoState.DefaultBranch;

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

                // STEP 3: Index files if needed
                if (!repoState.Branches.TryGetValue(targetBranch, out var branchState))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Branch '{targetBranch}' not found in repository '{repository}'",
                        availableBranches = await _gitTracker.GetRemoteBranchesAsync(repository, cancellationToken)
                    });
                }

                if (branchState.NeedsIndexing)
                {
                    _logger.LogInformation("Branch {Branch} needs indexing, triggering indexing now", targetBranch);

                    var fileChanges = await _gitTracker.GetFileChangesAsync(repository, targetBranch, cancellationToken);

                    if (fileChanges.Any())
                    {
                        // Index files (automatically marks branch as indexed)
                        var indexingResult = await _indexingService.IndexFilesAsync(fileChanges, cancellationToken);

                        _logger.LogInformation(
                            "Indexed {Count} files: {Symbols} symbols, {Edges} edges",
                            indexingResult.ParsedFiles.Count,
                            indexingResult.TotalSymbols,
                            indexingResult.TotalEdges);
                    }
                }
            }
            else
            {
                // Multi-repository search - use branch parameter if provided
                targetBranch = branch;
            }

            // Execute query using QueryOrchestrator
            var queryResponse = await _queryOrchestrator.QueryAsync(
                query: query,
                repositoryName: repository,
                branchName: targetBranch,
                language: null,
                maxResults: maxResults ?? 50,
                cancellationToken: cancellationToken);

            // Format results for LLM consumption
            var result = new
            {
                query = queryResponse.Query,
                intent = queryResponse.Intent.ToString(),
                repository,
                branch = targetBranch,
                totalResults = queryResponse.TotalResults,
                executionTimeMs = queryResponse.ExecutionTimeMs,
                results = queryResponse.Results.Select(r => new
                {
                    type = r.Type,
                    filePath = r.FilePath,
                    language = r.Language.ToString(),
                    symbolName = r.SymbolName,
                    symbolKind = r.SymbolKind?.ToString(),
                    content = r.Content,
                    startLine = r.StartLine,
                    endLine = r.EndLine,
                    score = r.Score,
                    bm25Score = r.BM25Score,
                    vectorScore = r.VectorScore,
                    graphScore = r.GraphScore,
                    signature = r.Signature,
                    documentation = r.Documentation,
                    relatedSymbols = r.RelatedSymbols?.Select(rs => new
                    {
                        name = rs.Name,
                        kind = rs.Kind.ToString(),
                        relationType = rs.RelationType,
                        filePath = rs.FilePath,
                        line = rs.Line
                    }).ToArray()
                }).ToArray(),
                suggestedQueries = queryResponse.SuggestedQueries,
                metadata = queryResponse.Metadata
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

