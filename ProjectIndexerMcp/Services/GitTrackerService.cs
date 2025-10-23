using LibGit2Sharp;
using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
using ProjectIndexerMcp.Models;
using System.Collections.Concurrent;

namespace ProjectIndexerMcp.Services;

/// <summary>
/// Manages Git repository cloning, fetching, and incremental change tracking.
/// </summary>
public sealed class GitTrackerService : IDisposable
{
    private readonly ILogger<GitTrackerService> _logger;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ConcurrentDictionary<string, RepositoryState> _repositories = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private bool _disposed;

    public GitTrackerService(
        ILogger<GitTrackerService> logger,
        IOptionsMonitor<ServerOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// Initializes all configured repositories.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;
        _logger.LogInformation("Initializing {Count} repositories", opts.Repositories.Length);

        foreach (var repoConfig in opts.Repositories)
        {
            try
            {
                await EnsureRepositoryAsync(repoConfig, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize repository {Name}", repoConfig.Name);
            }
        }
    }

    /// <summary>
    /// Ensures a repository is cloned and tracked.
    /// </summary>
    private async Task<RepositoryState> EnsureRepositoryAsync(ServerOptions.RepositoryDescriptor config, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        var localPath = Path.Combine(opts.WorkingDirectory, string.Join("_", config.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)));

        var state = _repositories.GetOrAdd(config.Name, _ => new RepositoryState
        {
            Name = config.Name,
            RemoteUrl = config.RemoteUrl,
            LocalPath = localPath,
            DefaultBranch = config.DefaultBranch.Trim().ToLowerInvariant(), // Normalize
            IsCloned = false
        });

        if (!state.IsCloned)
        {
            await CloneOrOpenRepositoryAsync(state, cancellationToken);
        }

        return state;
    }

    /// <summary>
    /// Clones a repository if it doesn't exist, or opens it if it does.
    /// </summary>
    private async Task CloneOrOpenRepositoryAsync(RepositoryState state, CancellationToken cancellationToken)
    {
        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            if (state.IsCloned)
                return;

            if (!Directory.Exists(state.LocalPath) || !Repository.IsValid(state.LocalPath))
            {
                _logger.LogInformation("Cloning repository {Name} from {Url} to {Path}", state.Name, state.RemoteUrl, state.LocalPath);

                Directory.CreateDirectory(state.LocalPath);

                var cloneOptions = new CloneOptions
                {
                    IsBare = true, // Use bare repository to save space
                    Checkout = false,
                    RecurseSubmodules = false
                };

                await Task.Run(() => Repository.Clone(state.RemoteUrl, state.LocalPath, cloneOptions), cancellationToken);

                _logger.LogInformation("Successfully cloned repository {Name}", state.Name);
            }
            else
            {
                _logger.LogInformation("Repository {Name} already exists at {Path}", state.Name, state.LocalPath);
            }

            state.IsCloned = true;
            state.LastUpdated = DateTimeOffset.UtcNow;

            // Initialize default branch tracking
            await TrackBranchAsync(state, state.DefaultBranch, cancellationToken);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Tracks a specific branch and updates its state.
    /// </summary>
    public async Task<BranchState> TrackBranchAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default)
    {
        if (!_repositories.TryGetValue(repositoryName, out var state))
        {
            throw new InvalidOperationException($"Repository {repositoryName} is not initialized");
        }

        return await TrackBranchAsync(state, branchName, cancellationToken);
    }

    /// <summary>
    /// Tracks a specific branch and updates its state.
    /// </summary>
    private async Task<BranchState> TrackBranchAsync(RepositoryState repoState, string branchName, CancellationToken cancellationToken)
    {
        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            using var repo = new Repository(repoState.LocalPath);

            // Fetch latest changes
            _logger.LogInformation("Fetching updates for repository {Name}", repoState.Name);
            var remote = repo.Network.Remotes["origin"];
            var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);

            await Task.Run(() => Commands.Fetch(repo, remote.Name, refSpecs, new FetchOptions(), null), cancellationToken);

            // Find the branch
            var remoteBranchName = $"origin/{branchName}";
            var branch = repo.Branches[remoteBranchName];

            if (branch == null)
            {
                throw new InvalidOperationException($"Branch {branchName} not found in repository {repoState.Name}");
            }

            var currentSha = branch.Tip.Sha;

            var branchState = repoState.Branches.GetValueOrDefault(branchName);
            if (branchState == null)
            {
                branchState = new BranchState
                {
                    Name = branchName,
                    CurrentSha = currentSha
                };
                repoState.Branches[branchName] = branchState;
                _logger.LogInformation("Started tracking branch {Branch} in repository {Repo} at {Sha}",
                    branchName, repoState.Name, currentSha);
            }
            else
            {
                branchState.CurrentSha = currentSha;
                _logger.LogInformation("Updated branch {Branch} in repository {Repo} to {Sha}",
                    branchName, repoState.Name, currentSha);
            }

            repoState.LastUpdated = DateTimeOffset.UtcNow;
            return branchState;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Gets file changes between the last indexed commit and the current commit for a branch.
    /// </summary>
    public async Task<IReadOnlyList<FileChange>> GetFileChangesAsync(
        string repositoryName,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        if (!_repositories.TryGetValue(repositoryName, out var repoState))
        {
            throw new InvalidOperationException($"Repository {repositoryName} is not initialized");
        }

        if (!repoState.Branches.TryGetValue(branchName, out var branchState))
        {
            throw new InvalidOperationException($"Branch {branchName} is not tracked in repository {repositoryName}");
        }

        return await Task.Run(() => GetFileChangesCore(repoState, branchState), cancellationToken);
    }

    private List<FileChange> GetFileChangesCore(RepositoryState repoState, BranchState branchState)
    {
        using var repo = new Repository(repoState.LocalPath);
        var changes = new List<FileChange>();

        var currentCommit = repo.Lookup<Commit>(branchState.CurrentSha!);
        if (currentCommit == null)
        {
            _logger.LogWarning("Current commit {Sha} not found in repository {Repo}",
                branchState.CurrentSha, repoState.Name);
            return changes;
        }

        Commit? lastIndexedCommit = null;
        if (!string.IsNullOrEmpty(branchState.LastIndexedSha))
        {
            lastIndexedCommit = repo.Lookup<Commit>(branchState.LastIndexedSha);
        }

        // If no previous index, get all files from current commit
        if (lastIndexedCommit == null)
        {
            foreach (var entry in currentCommit.Tree)
            {
                if (entry.TargetType == TreeEntryTargetType.Blob)
                {
                    changes.Add(new FileChange
                    {
                        RepositoryName = repoState.Name,
                        BranchName = branchState.Name,
                        CommitSha = currentCommit.Sha,
                        FilePath = entry.Path,
                        ChangeType = ChangeType.Added
                    });
                }
            }
        }
        else
        {
            // Compare trees to find changes
            var diff = repo.Diff.Compare<TreeChanges>(lastIndexedCommit.Tree, currentCommit.Tree);

            foreach (var change in diff)
            {
                var changeType = change.Status switch
                {
                    ChangeKind.Added => ChangeType.Added,
                    ChangeKind.Modified => ChangeType.Modified,
                    ChangeKind.Deleted => ChangeType.Deleted,
                    ChangeKind.Renamed => ChangeType.Renamed,
                    _ => ChangeType.Modified
                };

                changes.Add(new FileChange
                {
                    RepositoryName = repoState.Name,
                    BranchName = branchState.Name,
                    CommitSha = currentCommit.Sha,
                    FilePath = change.Path,
                    ChangeType = changeType,
                    OldFilePath = change.OldPath != change.Path ? change.OldPath : null
                });
            }
        }

        _logger.LogInformation("Found {Count} file changes in {Repo}/{Branch}", changes.Count, repoState.Name, branchState.Name);

        return changes;
    }

    /// <summary>
    /// Marks a branch as indexed up to the current commit.
    /// </summary>
    public void MarkBranchIndexed(string repositoryName, string branchName)
    {
        if (!_repositories.TryGetValue(repositoryName, out var repoState))
        {
            throw new InvalidOperationException($"Repository {repositoryName} is not initialized");
        }

        if (!repoState.Branches.TryGetValue(branchName, out var branchState))
        {
            throw new InvalidOperationException($"Branch {branchName} is not tracked in repository {repositoryName}");
        }

        branchState.LastIndexedSha = branchState.CurrentSha;
        branchState.LastIndexed = DateTimeOffset.UtcNow;

        _logger.LogInformation("Marked branch {Branch} in repository {Repo} as indexed at {Sha}", branchName, repositoryName, branchState.CurrentSha);
    }

    /// <summary>
    /// Gets all tracked repositories.
    /// </summary>
    public IReadOnlyDictionary<string, RepositoryState> GetRepositories() => _repositories;

    public void Dispose()
    {
        if (_disposed)
            return;

        _updateLock.Dispose();
        _disposed = true;
    }
}

