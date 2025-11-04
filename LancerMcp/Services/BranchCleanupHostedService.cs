using Microsoft.Extensions.Options;
using LancerMcp.Configuration;

namespace LancerMcp.Services;

/// <summary>
/// Background service that periodically cleans up stale branches that haven't been accessed recently.
/// Runs once per day at midnight UTC.
/// </summary>
public sealed class BranchCleanupHostedService : BackgroundService
{
    private readonly GitTrackerService _gitTracker;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ILogger<BranchCleanupHostedService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromDays(1);

    public BranchCleanupHostedService(
        GitTrackerService gitTracker,
        IOptionsMonitor<ServerOptions> options,
        ILogger<BranchCleanupHostedService> logger)
    {
        _gitTracker = gitTracker;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Branch cleanup service started");

        // Wait until the next midnight UTC before starting the first cleanup
        var now = DateTimeOffset.UtcNow;
        var nextMidnight = now.Date.AddDays(1);
        var initialDelay = nextMidnight - now;

        // Ensure the delay is positive (in case of clock skew or if we're exactly at midnight)
        if (initialDelay <= TimeSpan.Zero)
        {
            initialDelay = TimeSpan.FromMinutes(1); // Wait 1 minute if we're at or past midnight
        }

        _logger.LogInformation("First cleanup scheduled for {NextRun} (in {Delay})", nextMidnight, initialDelay);

        try
        {
            await Task.Delay(initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Branch cleanup service stopped before first run");
            return;
        }

        // Run cleanup daily
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during branch cleanup");
            }

            // Wait for the next day
            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Branch cleanup service stopped");
                break;
            }
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        var staleDays = _options.CurrentValue.StaleBranchDays;

        _logger.LogInformation("Starting branch cleanup (removing branches not accessed for {Days} days)", staleDays);

        await _gitTracker.CleanupStaleBranchesAsync(staleDays, cancellationToken);
    }
}

