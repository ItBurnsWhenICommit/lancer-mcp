namespace ProjectIndexerMcp.Services;

/// <summary>
/// Hosted service that initializes the Git tracker on application startup.
/// </summary>
public sealed class GitTrackerHostedService : IHostedService
{
    private readonly GitTrackerService _gitTracker;
    private readonly ILogger<GitTrackerHostedService> _logger;

    public GitTrackerHostedService(
        GitTrackerService gitTracker,
        ILogger<GitTrackerHostedService> logger)
    {
        _gitTracker = gitTracker;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Git tracker initialization");

        try
        {
            await _gitTracker.InitializeAsync(cancellationToken);
            _logger.LogInformation("Git tracker initialized successfully");
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
}

