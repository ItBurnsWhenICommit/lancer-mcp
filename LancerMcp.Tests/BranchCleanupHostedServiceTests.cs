using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;

namespace LancerMcp.Tests;

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

        // Create mock repositories (no-op implementations for testing)
        var mockRepoRepository = new MockRepositoryRepository();
        var mockBranchRepository = new MockBranchRepository();

        _gitTracker = new GitTrackerService(
            NullLogger<GitTrackerService>.Instance,
            _options,
            mockRepoRepository,
            mockBranchRepository);

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

    /// <summary>
    /// Mock implementation of IRepositoryRepository for testing.
    /// </summary>
    private class MockRepositoryRepository : IRepositoryRepository
    {
        private readonly Dictionary<string, Repository> _repos = new();

        public Task<Repository?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_repos.Values.FirstOrDefault(r => r.Id == id));

        public Task<Repository?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(_repos.GetValueOrDefault(name));

        public Task<IEnumerable<Repository>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Repository>>(_repos.Values);

        public Task<Repository> CreateAsync(Repository repository, CancellationToken cancellationToken = default)
        {
            var newRepo = new Repository
            {
                Id = Guid.NewGuid().ToString(),
                Name = repository.Name,
                RemoteUrl = repository.RemoteUrl,
                DefaultBranch = repository.DefaultBranch
            };
            _repos[newRepo.Name] = newRepo;
            return Task.FromResult(newRepo);
        }

        public Task<Repository> UpdateAsync(Repository repository, CancellationToken cancellationToken = default)
        {
            _repos[repository.Name] = repository;
            return Task.FromResult(repository);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            var repo = _repos.Values.FirstOrDefault(r => r.Id == id);
            if (repo != null)
            {
                _repos.Remove(repo.Name);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public Task<bool> ExistsAsync(string name, CancellationToken cancellationToken = default)
            => Task.FromResult(_repos.ContainsKey(name));
    }

    /// <summary>
    /// Mock implementation of IBranchRepository for testing.
    /// </summary>
    private class MockBranchRepository : IBranchRepository
    {
        public Task<Branch?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<Branch?>(null);

        public Task<Branch?> GetByRepoAndNameAsync(string repoId, string name, CancellationToken cancellationToken = default)
            => Task.FromResult<Branch?>(null);

        public Task<IEnumerable<Branch>> GetByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Branch>>(Array.Empty<Branch>());

        public Task<IEnumerable<Branch>> GetByIndexStateAsync(IndexState state, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Branch>>(Array.Empty<Branch>());

        public Task<Branch> CreateAsync(Branch branch, CancellationToken cancellationToken = default)
            => Task.FromResult(branch);

        public Task<Branch> UpdateAsync(Branch branch, CancellationToken cancellationToken = default)
            => Task.FromResult(branch);

        public Task UpdateIndexStateAsync(string id, IndexState state, string? indexedCommitSha = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}

