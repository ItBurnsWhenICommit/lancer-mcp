using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
using ProjectIndexerMcp.Models;
using ProjectIndexerMcp.Repositories;
using ProjectIndexerMcp.Services;
using Xunit;

namespace ProjectIndexerMcp.IntegrationTests;

/// <summary>
/// Integration tests for GitTrackerService that verify PostgreSQL persistence.
/// These tests use a real database connection to verify repository and branch metadata storage.
/// </summary>
[Trait("Category", "Integration")]
public class GitTrackerIntegrationTests : FixtureTestBase
{
    private readonly DatabaseService _databaseService;
    private readonly IRepositoryRepository _repositoryRepository;
    private readonly IBranchRepository _branchRepository;

    public GitTrackerIntegrationTests()
    {
        var serverOptions = new ServerOptions
        {
            DatabaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
            DatabasePort = int.Parse(Environment.GetEnvironmentVariable("DB_PORT") ?? "5432"),
            DatabaseName = TestDatabaseName,
            DatabaseUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
            DatabasePassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "postgres"
        };

        _databaseService = new DatabaseService(
            NullLogger<DatabaseService>.Instance,
            new TestOptionsMonitor(serverOptions)
        );

        _repositoryRepository = new RepositoryRepository(_databaseService, NullLogger<RepositoryRepository>.Instance);
        _branchRepository = new BranchRepository(_databaseService, NullLogger<BranchRepository>.Instance);
    }

    [Fact]
    public async Task RepositoryRepository_CreateAndRetrieve_ShouldPersistToDatabase()
    {
        // Arrange
        var testRepo = new Repository
        {
            Name = $"test-repo-{Guid.NewGuid()}",
            RemoteUrl = "git@github.com:test/repo.git",
            DefaultBranch = "main"
        };

        // Act - Create
        var created = await _repositoryRepository.CreateAsync(testRepo);

        // Assert - Created repository should have an ID
        created.Should().NotBeNull();
        created.Id.Should().NotBeNullOrEmpty();
        created.Name.Should().Be(testRepo.Name);
        created.RemoteUrl.Should().Be(testRepo.RemoteUrl);
        created.DefaultBranch.Should().Be(testRepo.DefaultBranch);

        // Act - Retrieve by ID
        var retrievedById = await _repositoryRepository.GetByIdAsync(created.Id);

        // Assert - Retrieved by ID should match
        retrievedById.Should().NotBeNull();
        retrievedById!.Id.Should().Be(created.Id);
        retrievedById.Name.Should().Be(testRepo.Name);

        // Act - Retrieve by Name
        var retrievedByName = await _repositoryRepository.GetByNameAsync(testRepo.Name);

        // Assert - Retrieved by Name should match
        retrievedByName.Should().NotBeNull();
        retrievedByName!.Id.Should().Be(created.Id);
        retrievedByName.Name.Should().Be(testRepo.Name);

        Console.WriteLine($"✅ Repository persisted to PostgreSQL with ID: {created.Id}");
    }

    [Fact]
    public async Task BranchRepository_CreateAndRetrieve_ShouldPersistToDatabase()
    {
        // Arrange - Create a repository first
        var testRepo = new Repository
        {
            Name = $"test-repo-{Guid.NewGuid()}",
            RemoteUrl = "git@github.com:test/repo.git",
            DefaultBranch = "main"
        };
        var repo = await _repositoryRepository.CreateAsync(testRepo);

        var testBranch = new Branch
        {
            RepoId = repo.Id,
            Name = "feature/test-branch",
            HeadCommitSha = "abc123def456",
            IndexState = IndexState.Pending,
            IndexedCommitSha = null,
            LastIndexedAt = null
        };

        // Act - Create
        var created = await _branchRepository.CreateAsync(testBranch);

        // Assert - Created branch should have an ID
        created.Should().NotBeNull();
        created.Id.Should().NotBeNullOrEmpty();
        created.RepoId.Should().Be(repo.Id);
        created.Name.Should().Be(testBranch.Name);
        created.HeadCommitSha.Should().Be(testBranch.HeadCommitSha);
        created.IndexState.Should().Be(IndexState.Pending);

        // Act - Retrieve by repo and name
        var retrieved = await _branchRepository.GetByRepoAndNameAsync(repo.Id, testBranch.Name);

        // Assert - Retrieved should match
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
        retrieved.Name.Should().Be(testBranch.Name);
        retrieved.HeadCommitSha.Should().Be(testBranch.HeadCommitSha);

        Console.WriteLine($"✅ Branch persisted to PostgreSQL with ID: {created.Id}");
    }

    [Fact]
    public async Task BranchRepository_UpdateIndexState_ShouldPersistChanges()
    {
        // Arrange - Create repository and branch
        var testRepo = new Repository
        {
            Name = $"test-repo-{Guid.NewGuid()}",
            RemoteUrl = "git@github.com:test/repo.git",
            DefaultBranch = "main"
        };
        var repo = await _repositoryRepository.CreateAsync(testRepo);

        var testBranch = new Branch
        {
            RepoId = repo.Id,
            Name = "main",
            HeadCommitSha = "abc123",
            IndexState = IndexState.Pending
        };
        var branch = await _branchRepository.CreateAsync(testBranch);

        // Act - Update to Indexed state
        var updatedBranch = new Branch
        {
            Id = branch.Id,
            RepoId = branch.RepoId,
            Name = branch.Name,
            HeadCommitSha = branch.HeadCommitSha,
            IndexState = IndexState.Completed,
            IndexedCommitSha = "abc123",
            LastIndexedAt = DateTimeOffset.UtcNow,
            CreatedAt = branch.CreatedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var updated = await _branchRepository.UpdateAsync(updatedBranch);

        // Assert - Update should be persisted
        updated.Should().NotBeNull();
        updated.IndexState.Should().Be(IndexState.Completed);
        updated.IndexedCommitSha.Should().Be("abc123");
        updated.LastIndexedAt.Should().NotBeNull();

        // Act - Retrieve again to verify persistence
        var retrieved = await _branchRepository.GetByIdAsync(branch.Id);

        // Assert - Retrieved should reflect the update
        retrieved.Should().NotBeNull();
        retrieved!.IndexState.Should().Be(IndexState.Completed);
        retrieved.IndexedCommitSha.Should().Be("abc123");
        retrieved.LastIndexedAt.Should().NotBeNull();

        Console.WriteLine($"✅ Branch index state updated in PostgreSQL: {retrieved.IndexState}");
    }

    [Fact]
    public async Task BranchRepository_GetByRepoId_ShouldReturnAllBranches()
    {
        // Arrange - Create repository with multiple branches
        var testRepo = new Repository
        {
            Name = $"test-repo-{Guid.NewGuid()}",
            RemoteUrl = "git@github.com:test/repo.git",
            DefaultBranch = "main"
        };
        var repo = await _repositoryRepository.CreateAsync(testRepo);

        var branches = new[]
        {
            new Branch { RepoId = repo.Id, Name = "main", HeadCommitSha = "sha1", IndexState = IndexState.Completed },
            new Branch { RepoId = repo.Id, Name = "develop", HeadCommitSha = "sha2", IndexState = IndexState.Pending },
            new Branch { RepoId = repo.Id, Name = "feature/test", HeadCommitSha = "sha3", IndexState = IndexState.Stale }
        };

        foreach (var branch in branches)
        {
            await _branchRepository.CreateAsync(branch);
        }

        // Act
        var retrieved = await _branchRepository.GetByRepoIdAsync(repo.Id);
        var branchList = retrieved.ToList();

        // Assert
        branchList.Should().HaveCount(3);
        branchList.Should().Contain(b => b.Name == "main" && b.IndexState == IndexState.Completed);
        branchList.Should().Contain(b => b.Name == "develop" && b.IndexState == IndexState.Pending);
        branchList.Should().Contain(b => b.Name == "feature/test" && b.IndexState == IndexState.Stale);

        Console.WriteLine($"✅ Retrieved {branchList.Count} branches from PostgreSQL");
    }

    [Fact]
    public async Task BranchRepository_GetByIndexState_ShouldReturnOnlyStaleBranches()
    {
        // Arrange - Create repository with mixed branch states
        var testRepo = new Repository
        {
            Name = $"test-repo-{Guid.NewGuid()}",
            RemoteUrl = "git@github.com:test/repo.git",
            DefaultBranch = "main"
        };
        var repo = await _repositoryRepository.CreateAsync(testRepo);

        await _branchRepository.CreateAsync(new Branch
        {
            RepoId = repo.Id,
            Name = "main",
            HeadCommitSha = "sha1",
            IndexState = IndexState.Completed,
            LastIndexedAt = DateTimeOffset.UtcNow.AddDays(-1)
        });

        await _branchRepository.CreateAsync(new Branch
        {
            RepoId = repo.Id,
            Name = "stale-branch",
            HeadCommitSha = "sha2",
            IndexState = IndexState.Stale,
            LastIndexedAt = DateTimeOffset.UtcNow.AddDays(-10)
        });

        // Act
        var staleBranches = await _branchRepository.GetByIndexStateAsync(IndexState.Stale);
        var staleList = staleBranches.ToList();

        // Assert
        staleList.Should().HaveCount(1);
        staleList[0].Name.Should().Be("stale-branch");
        staleList[0].IndexState.Should().Be(IndexState.Stale);

        Console.WriteLine($"✅ Retrieved {staleList.Count} stale branches from PostgreSQL");
    }

    [Fact]
    public async Task RepositoryRepository_GetAll_ShouldReturnAllRepositories()
    {
        // Arrange - Create multiple repositories
        var repo1 = await _repositoryRepository.CreateAsync(new Repository
        {
            Name = $"test-repo-1-{Guid.NewGuid()}",
            RemoteUrl = "git@github.com:test/repo1.git",
            DefaultBranch = "main"
        });

        var repo2 = await _repositoryRepository.CreateAsync(new Repository
        {
            Name = $"test-repo-2-{Guid.NewGuid()}",
            RemoteUrl = "git@github.com:test/repo2.git",
            DefaultBranch = "master"
        });

        // Act
        var allRepos = await _repositoryRepository.GetAllAsync();
        var repoList = allRepos.ToList();

        // Assert - Should contain at least our test repos
        repoList.Should().Contain(r => r.Id == repo1.Id);
        repoList.Should().Contain(r => r.Id == repo2.Id);

        Console.WriteLine($"✅ Retrieved {repoList.Count} repositories from PostgreSQL");
    }

    /// <summary>
    /// Simple test implementation of IOptionsMonitor for testing.
    /// </summary>
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

