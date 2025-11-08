using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;

namespace LancerMcp.Tests;

/// <summary>
/// Integration tests for GitTrackerService.
/// These tests use a real public repository to verify Git operations work correctly.
/// </summary>
public class GitTrackerServiceTests : IDisposable
{
    private readonly string _testWorkingDirectory;
    private readonly GitTrackerService _gitTracker;
    private readonly IOptionsMonitor<ServerOptions> _options;

    public GitTrackerServiceTests()
    {
        // Create a temporary directory for test repositories
        _testWorkingDirectory = Path.Combine(Path.GetTempPath(), $"git-tracker-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testWorkingDirectory);

        // Configure test options with a small public repository
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
            StaleBranchDays = 14
        };

        _options = new TestOptionsMonitor(serverOptions);

        // Create mock repositories (no-op implementations for testing)
        var mockRepoRepository = new MockRepositoryRepository();
        var mockBranchRepository = new MockBranchRepository();
        var mockWorkspaceLoader = TestHelpers.CreateMockWorkspaceLoader();

        _gitTracker = new GitTrackerService(
            NullLogger<GitTrackerService>.Instance,
            _options,
            mockRepoRepository,
            mockBranchRepository,
            mockWorkspaceLoader);
    }

    [Fact]
    public async Task InitializeAsync_ShouldCloneRepository()
    {
        // Act
        await _gitTracker.InitializeAsync(CancellationToken.None);

        // Assert
        var repos = _gitTracker.GetRepositories();
        Assert.Contains("test-repo", repos.Keys);

        var repo = repos["test-repo"];
        Assert.True(repo.IsCloned);
        Assert.Equal("master", repo.DefaultBranch);
        Assert.Contains("master", repo.Branches.Keys);

        // Verify the repository directory exists
        Assert.True(Directory.Exists(repo.LocalPath));
    }

    [Fact]
    public async Task EnsureBranchTrackedAsync_ShouldTrackNewBranch()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);

        // Act
        var branchState = await _gitTracker.EnsureBranchTrackedAsync("test-repo", "master", CancellationToken.None);

        // Assert
        Assert.NotNull(branchState);
        Assert.Equal("master", branchState.Name);
        Assert.NotNull(branchState.CurrentSha);
        Assert.NotEmpty(branchState.CurrentSha);
        Assert.NotNull(branchState.LastAccessed);
        Assert.True((DateTimeOffset.UtcNow - branchState.LastAccessed.Value).TotalSeconds < 5);
    }

    [Fact]
    public async Task EnsureBranchTrackedAsync_ShouldUpdateExistingBranch()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);
        var firstAccess = await _gitTracker.EnsureBranchTrackedAsync("test-repo", "master", CancellationToken.None);
        var firstAccessTime = firstAccess.LastAccessed;

        // Wait a bit to ensure time difference
        await Task.Delay(100);

        // Act
        var secondAccess = await _gitTracker.EnsureBranchTrackedAsync("test-repo", "master", CancellationToken.None);

        // Assert
        Assert.NotNull(secondAccess.LastAccessed);
        Assert.True(secondAccess.LastAccessed > firstAccessTime);
        Assert.Equal(firstAccess.CurrentSha, secondAccess.CurrentSha);
    }

    [Fact]
    public async Task GetRemoteBranchesAsync_ShouldReturnAvailableBranches()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);

        // Act
        var branches = await _gitTracker.GetRemoteBranchesAsync("test-repo", CancellationToken.None);

        // Assert
        Assert.NotEmpty(branches);
        Assert.Contains("master", branches);
    }

    [Fact]
    public async Task BranchExistsAsync_ShouldReturnTrueForExistingBranch()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);

        // Act
        var exists = await _gitTracker.BranchExistsAsync("test-repo", "master", CancellationToken.None);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task BranchExistsAsync_ShouldReturnFalseForNonExistingBranch()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);

        // Act
        var exists = await _gitTracker.BranchExistsAsync("test-repo", "non-existent-branch-xyz", CancellationToken.None);

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task GetFileChangesAsync_ShouldReturnAllFilesForFirstIndex()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);
        await _gitTracker.EnsureBranchTrackedAsync("test-repo", "master", CancellationToken.None);

        // Act
        var changes = await _gitTracker.GetFileChangesAsync("test-repo", "master", CancellationToken.None);

        // Assert
        Assert.NotEmpty(changes);
        foreach (var change in changes)
        {
            Assert.Equal("test-repo", change.RepositoryName);
            Assert.Equal("master", change.BranchName);
            Assert.NotNull(change.FilePath);
            Assert.NotEmpty(change.FilePath);
            Assert.NotNull(change.CommitSha);
            Assert.NotEmpty(change.CommitSha);
        }
    }

    [Fact]
    public async Task MarkBranchIndexed_ShouldUpdateLastIndexedSha()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);
        var branchState = await _gitTracker.EnsureBranchTrackedAsync("test-repo", "master", CancellationToken.None);
        var currentSha = branchState.CurrentSha;

        // Act
        _gitTracker.MarkBranchIndexed("test-repo", "master");

        // Assert
        var repos = _gitTracker.GetRepositories();
        var updatedBranch = repos["test-repo"].Branches["master"];
        Assert.Equal(currentSha, updatedBranch.LastIndexedSha);
        Assert.NotNull(updatedBranch.LastIndexed);
        Assert.True((DateTimeOffset.UtcNow - updatedBranch.LastIndexed.Value).TotalSeconds < 5);
        Assert.False(updatedBranch.NeedsIndexing);
    }

    [Fact]
    public async Task CleanupStaleBranchesAsync_ShouldNotRemoveRecentlyAccessedBranches()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);
        await _gitTracker.EnsureBranchTrackedAsync("test-repo", "master", CancellationToken.None);

        // Act
        var removedCount = await _gitTracker.CleanupStaleBranchesAsync(staleDays: 14, CancellationToken.None);

        // Assert
        Assert.Equal(0, removedCount);
        var repos = _gitTracker.GetRepositories();
        Assert.Contains("master", repos["test-repo"].Branches.Keys);
    }

    [Fact]
    public async Task CleanupStaleBranchesAsync_ShouldRemoveStaleBranches()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);

        // Get available branches and track a non-default one
        var branches = await _gitTracker.GetRemoteBranchesAsync("test-repo", CancellationToken.None);
        var nonDefaultBranch = branches.FirstOrDefault(b => b != "master");

        // Skip test if no other branches available
        if (nonDefaultBranch == null)
        {
            // Manually add a fake branch for testing purposes
            var repos = _gitTracker.GetRepositories();
            var repo = repos["test-repo"];
            repo.Branches["test-branch"] = new Models.BranchState
            {
                Name = "test-branch",
                CurrentSha = "fake-sha",
                LastAccessed = DateTimeOffset.UtcNow.AddDays(-30)
            };
            nonDefaultBranch = "test-branch";
        }
        else
        {
            var branchState = await _gitTracker.EnsureBranchTrackedAsync("test-repo", nonDefaultBranch, CancellationToken.None);
            // Manually set LastAccessed to 30 days ago to simulate stale branch
            branchState.LastAccessed = DateTimeOffset.UtcNow.AddDays(-30);
        }

        // Act
        var removedCount = await _gitTracker.CleanupStaleBranchesAsync(staleDays: 14, CancellationToken.None);

        // Assert
        Assert.True(removedCount >= 1);
        var reposAfter = _gitTracker.GetRepositories();
        Assert.DoesNotContain(nonDefaultBranch, reposAfter["test-repo"].Branches.Keys);
    }

    [Fact]
    public async Task CleanupStaleBranchesAsync_ShouldNotRemoveDefaultBranch()
    {
        // Arrange
        await _gitTracker.InitializeAsync(CancellationToken.None);
        var repos = _gitTracker.GetRepositories();
        var defaultBranch = repos["test-repo"].Branches["master"];

        // Manually set LastAccessed to 30 days ago
        defaultBranch.LastAccessed = DateTimeOffset.UtcNow.AddDays(-30);

        // Act
        var removedCount = await _gitTracker.CleanupStaleBranchesAsync(staleDays: 14, CancellationToken.None);

        // Assert
        Assert.Equal(0, removedCount); // default branch should never be removed
        Assert.Contains("master", repos["test-repo"].Branches.Keys);
    }

    public void Dispose()
    {
        _gitTracker?.Dispose();

        // Clean up test directory
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

}

