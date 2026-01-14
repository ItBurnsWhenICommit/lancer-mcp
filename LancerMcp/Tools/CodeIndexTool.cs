using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Services;
using Microsoft.Extensions.Options;

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
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ILogger<CodeIndexTool> _logger;

    public CodeIndexTool(
        GitTrackerService gitTracker,
        IndexingService indexingService,
        QueryOrchestrator queryOrchestrator,
        IOptionsMonitor<ServerOptions> options,
        ILogger<CodeIndexTool> logger)
    {
        _gitTracker = gitTracker;
        _indexingService = indexingService;
        _queryOrchestrator = queryOrchestrator;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Unified query interface for code indexing operations.
    /// The server interprets the query intent and returns the best results.
    /// </summary>
    [McpServerTool]
    [Description("Query the code index for a specific repository. Supports natural language queries for code search, symbol lookup, finding references, call graphs, and code navigation. The server interprets your intent and returns relevant results.")]
    public async Task<string> Query(
        [Description("Repository name to search in (required). Must match one of the configured repository names.")]
        string repository,
        [Description("Natural language query describing what you're looking for. Examples: 'find all classes that implement IRepository', 'show me the definition of UserService', 'what calls the Login method?', 'find recent changes in authentication code'")]
        string query,
        [Description("Optional: specific branch to search in. If not provided, uses the default branch.")]
        string? branch = null,
        [Description("Optional: maximum number of results to return (default: 50)")]
        int? maxResults = null,
        [Description("Optional: retrieval profile (Fast, Hybrid, Semantic). Defaults to Fast.")]
        string? profile = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing query: {Query} (repo: {Repo}, branch: {Branch})", query, repository, branch ?? "default");

            // Validate repository is specified
            if (string.IsNullOrWhiteSpace(repository))
            {
                return JsonSerializer.Serialize(new
                {
                    error = "Repository parameter is required",
                    availableRepositories = _gitTracker.GetRepositories().Keys.ToArray()
                });
            }

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

            RetrievalProfile? profileOverride = null;
            if (!string.IsNullOrWhiteSpace(profile))
            {
                if (!Enum.TryParse(profile, ignoreCase: true, out RetrievalProfile parsedProfile))
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Unknown retrieval profile '{profile}'",
                        allowedProfiles = Enum.GetNames<RetrievalProfile>()
                    });
                }

                profileOverride = parsedProfile;
            }

            // Execute query using QueryOrchestrator
            var queryResponse = await _queryOrchestrator.QueryAsync(
                query: query,
                repositoryName: repository,
                branchName: targetBranch,
                language: null,
                maxResults: maxResults ?? 50,
                profileOverride: profileOverride,
                cancellationToken: cancellationToken);

            var responseOptions = _options.CurrentValue;
            var optimizedResult = queryResponse.ToOptimizedFormat(new QueryResponseCompactionOptions
            {
                MaxResults = responseOptions.MaxResponseResults,
                MaxSnippetChars = responseOptions.MaxResponseSnippetChars,
                MaxJsonBytes = responseOptions.MaxResponseBytes
            });

            return JsonSerializer.Serialize(optimizedResult);
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

            return JsonSerializer.Serialize(errorResult);
        }
    }
}
