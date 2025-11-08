using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;

namespace LancerMcp.Tests.Mocks;

/// <summary>
/// Simple test implementation of IOptionsMonitor for testing.
/// </summary>
public class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
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
public class MockRepositoryRepository : IRepositoryRepository
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
public class MockBranchRepository : IBranchRepository
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

/// <summary>
/// Helper methods for creating test instances.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Creates a mock WorkspaceLoader for testing.
    /// WorkspaceLoader is sealed, so we create a real instance.
    /// Tests don't actually use it since we're not loading workspaces in unit tests.
    /// </summary>
    public static WorkspaceLoader CreateMockWorkspaceLoader()
    {
        return new WorkspaceLoader(NullLogger<WorkspaceLoader>.Instance);
    }
}

