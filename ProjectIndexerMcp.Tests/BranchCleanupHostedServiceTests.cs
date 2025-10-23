using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
using ProjectIndexerMcp.Services;

namespace ProjectIndexerMcp.Tests;

/// <summary>
/// Tests for the BranchCleanupHostedService.
/// </summary>
public class BranchCleanupHostedServiceTests : IDisposable
{
    private readonly string _testWorkingDirectory;
    private readonly GitTrackerService _gitTracker;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly BranchCleanupHostedService _cleanupService;

    public BranchCleanupHostedServiceTests()
    {
        _testWorkingDirectory = Path.Combine(Path.GetTempPath(), $"cleanup-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testWorkingDirectory);

        var serverOptions = new ServerOptions
        {
            WorkingDirectory = _testWorkingDirectory,
            Repositories =
            [
                new ServerOptions.RepositoryDescriptor
                {
                    Name = "test-repo",
                    RemoteUrl = "git@github.com:octocat/Hello-World.git",
                    DefaultBranch = "master"
                }
            ],
            StaleBranchDays = 7 // Use shorter period for testing
        };

        _options = new TestOptionsMonitor(serverOptions);
        _gitTracker = new GitTrackerService(
            NullLogger<GitTrackerService>.Instance,
            _options);

        _cleanupService = new BranchCleanupHostedService(
            _gitTracker,
            _options,
            NullLogger<BranchCleanupHostedService>.Instance);
    }

    [Fact]
    public async Task StopAsync_ShouldCancelExecution()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var startTask = _cleanupService.StartAsync(cts.Token);

        // Give it a moment to start
        await Task.Delay(100);

        // Act
        await _cleanupService.StopAsync(CancellationToken.None);

        // Assert - should complete quickly after stop
        var completed = await Task.WhenAny(startTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(startTask, completed); // service should stop promptly
    }

    public void Dispose()
    {
        _cleanupService?.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _gitTracker?.Dispose();

        if (Directory.Exists(_testWorkingDirectory))
        {
            try
            {
                Directory.Delete(_testWorkingDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

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

