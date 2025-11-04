using Microsoft.Extensions.Options;
using LancerMcp.Configuration;

namespace LancerMcp.Services;

/// <summary>
/// Hosted service that initializes the Git tracker and indexes default branches on application startup.
/// </summary>
public sealed class GitTrackerHostedService : IHostedService
{
    private readonly GitTrackerService _gitTracker;
    private readonly IndexingService _indexingService;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ILogger<GitTrackerHostedService> _logger;

    public GitTrackerHostedService(
        GitTrackerService gitTracker,
        IndexingService indexingService,
        IOptionsMonitor<ServerOptions> options,
        ILogger<GitTrackerHostedService> logger)
    {
        _gitTracker = gitTracker;
        _indexingService = indexingService;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Git tracker initialization");

        try
        {
            await _gitTracker.InitializeAsync(cancellationToken);
            _logger.LogInformation("Git tracker initialized successfully");

            // Index default branches automatically
            await IndexDefaultBranchesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Git tracker");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Git tracker");
        return Task.CompletedTask;
    }

    private async Task IndexDefaultBranchesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting automatic indexing of default branches");

        // Use the configured repositories directly
        var repositories = _options.CurrentValue.Repositories;

        foreach (var repo in repositories)
        {
            try
            {
                _logger.LogInformation("Indexing default branch {Branch} for repository {Repo}", repo.DefaultBranch, repo.Name);

                // Get file changes for the default branch
                var fileChanges = await _gitTracker.GetFileChangesAsync(repo.Name, repo.DefaultBranch, cancellationToken);

                if (!fileChanges.Any())
                {
                    _logger.LogInformation("No files to index for {Repo}/{Branch}", repo.Name, repo.DefaultBranch);
                    continue;
                }

                // Index the files (automatically marks branch as indexed)
                var result = await _indexingService.IndexFilesAsync(fileChanges, cancellationToken);

                _logger.LogInformation(
                    "Indexed {Repo}/{Branch}: {FileCount} files, {SymbolCount} symbols, {EdgeCount} edges",
                    repo.Name,
                    repo.DefaultBranch,
                    result.ParsedFiles.Count,
                    result.TotalSymbols,
                    result.TotalEdges);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index default branch {Branch} for repository {Repo}", repo.DefaultBranch, repo.Name);
            }
        }

        _logger.LogInformation("Completed automatic indexing of default branches");
    }
}

