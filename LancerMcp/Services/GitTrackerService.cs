using System.Collections.Concurrent;
using LibGit2Sharp;
using Microsoft.Extensions.Options;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using GitRepository = LibGit2Sharp.Repository;
using GitCommit = LibGit2Sharp.Commit;

namespace LancerMcp.Services;

/// <summary>
/// Manages Git repository cloning, fetching, and incremental change tracking.
/// Persists repository and branch metadata to PostgreSQL.
/// </summary>
public sealed class GitTrackerService : IDisposable
{
    private readonly ILogger<GitTrackerService> _logger;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly IRepositoryRepository _repositoryRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly WorkspaceLoader _workspaceLoader;
    private readonly ConcurrentDictionary<string, RepositoryState> _repositories = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private bool _disposed;

    public GitTrackerService(
        ILogger<GitTrackerService> logger,
        IOptionsMonitor<ServerOptions> options,
        IRepositoryRepository repositoryRepository,
        IBranchRepository branchRepository,
        WorkspaceLoader workspaceLoader)
    {
        _logger = logger;
        _options = options;
        _repositoryRepository = repositoryRepository;
        _branchRepository = branchRepository;
        _workspaceLoader = workspaceLoader;
    }

    /// <summary>
    /// Creates credentials provider for Git operations (uses SSH agent for authentication).
    /// </summary>
    private Credentials CreateCredentialsProvider(string url, string usernameFromUrl, SupportedCredentialTypes types)
    {
        _logger.LogDebug("Credentials requested for {Url}, username: {Username}, types: {Types}", url, usernameFromUrl, types);
        return new DefaultCredentials();
    }

    /// <summary>
    /// Initializes all configured repositories.
    /// Loads existing repository and branch metadata from the database.
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
    /// Persists repository metadata to the database.
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
            DefaultBranch = config.DefaultBranch.Trim(),
            IsCloned = false
        });

        if (!state.IsCloned)
        {
            await CloneOrOpenRepositoryAsync(state, cancellationToken);

            // Persist repository metadata to database
            var repo = await EnsureRepositoryInDatabaseAsync(state, cancellationToken);

            // Load existing branch state from database
            await LoadBranchStateFromDatabaseAsync(state, repo.Id, cancellationToken);
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

            if (!Directory.Exists(state.LocalPath) || !GitRepository.IsValid(state.LocalPath))
            {
                _logger.LogInformation("Cloning repository {Name} from {Url} to {Path}", state.Name, state.RemoteUrl, state.LocalPath);

                // Clean up any existing directory that's not a valid repository
                if (Directory.Exists(state.LocalPath))
                {
                    _logger.LogWarning("Removing invalid repository directory at {Path}", state.LocalPath);
                    Directory.Delete(state.LocalPath, recursive: true);
                }

                Directory.CreateDirectory(state.LocalPath);

                var cloneOptions = new CloneOptions
                {
                    IsBare = false, // Use working tree so MSBuildWorkspace can find .sln/.csproj files
                    Checkout = true,
                    RecurseSubmodules = false
                };

                // Configure SSH credentials for cloning
                cloneOptions.FetchOptions.CredentialsProvider = CreateCredentialsProvider;

                await Task.Run(() => GitRepository.Clone(state.RemoteUrl, state.LocalPath, cloneOptions), cancellationToken);

                _logger.LogInformation("Successfully cloned repository {Name}", state.Name);
            }
            else
            {
                _logger.LogInformation("Repository {Name} already exists at {Path}", state.Name, state.LocalPath);
            }

            state.IsCloned = true;
            state.LastUpdated = DateTimeOffset.UtcNow;

            // Track the default branch (without acquiring lock since we already have it)
            await UpdateBranchInternalAsync(state, state.DefaultBranch, cancellationToken);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Updates a branch by fetching from remote and updating its state.
    /// Acquires the update lock.
    /// </summary>
    private async Task<BranchState> UpdateBranchAsync(RepositoryState repoState, string branchName, CancellationToken cancellationToken)
    {
        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            return await UpdateBranchInternalAsync(repoState, branchName, cancellationToken);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Internal method to update a branch without acquiring the lock (assumes lock is already held).
    /// Fetches from remote and updates the branch state.
    /// Persists branch metadata to the database.
    /// </summary>
    private async Task<BranchState> UpdateBranchInternalAsync(RepositoryState repoState, string branchName, CancellationToken cancellationToken)
    {
        using var repo = new GitRepository(repoState.LocalPath);

        // Fetch latest changes
        _logger.LogInformation("Fetching updates for repository {Name}", repoState.Name);
        var remote = repo.Network.Remotes["origin"];
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);

        var fetchOptions = new FetchOptions
        {
            CredentialsProvider = CreateCredentialsProvider
        };

        await Task.Run(() => Commands.Fetch(repo, remote.Name, refSpecs, fetchOptions, null), cancellationToken);

        // Clear workspace cache after fetching updates so the next indexing will reload the workspace
        _workspaceLoader.ClearCache(repoState.LocalPath);

        // Find the branch
        var remoteBranchName = $"origin/{branchName}";
        var branch = repo.Branches[remoteBranchName]
            ?? throw new InvalidOperationException($"Branch {branchName} not found in repository {repoState.Name}");

        var currentSha = branch.Tip.Sha;

        var branchState = repoState.Branches.GetValueOrDefault(branchName);
        if (branchState == null)
        {
            branchState = new BranchState
            {
                Name = branchName,
                CurrentSha = currentSha,
                LastAccessed = DateTimeOffset.UtcNow
            };
            repoState.Branches[branchName] = branchState;
            _logger.LogInformation("Started tracking branch {Branch} in repository {Repo} at {Sha}", branchName, repoState.Name, currentSha);
        }
        else
        {
            branchState.CurrentSha = currentSha;
            branchState.LastAccessed = DateTimeOffset.UtcNow;
            _logger.LogInformation("Updated branch {Branch} in repository {Repo} to {Sha}", branchName, repoState.Name, currentSha);
        }

        // Persist branch metadata to database
        await EnsureBranchInDatabaseAsync(repoState, branchName, currentSha, cancellationToken);

        repoState.LastUpdated = DateTimeOffset.UtcNow;
        return branchState;
    }

    /// <summary>
    /// Marks a branch as indexed at its current commit SHA.
    /// Persists the index state to the database asynchronously (fire-and-forget).
    /// </summary>
    public void MarkBranchAsIndexed(string repositoryName, string branchName)
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
        branchState.LastAccessed = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Marked branch {Branch} in repository {Repo} as indexed at {Sha}",
            branchName,
            repositoryName,
            branchState.CurrentSha);

        // Persist to database asynchronously (fire-and-forget)
        // We don't await here to avoid blocking the caller
        _ = Task.Run(async () =>
        {
            try
            {
                await UpdateBranchIndexStateAsync(repositoryName, branchName, branchState.CurrentSha!, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update branch index state in database for {Repo}/{Branch}", repositoryName, branchName);
            }
        });
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
        using var repo = new GitRepository(repoState.LocalPath);
        var changes = new List<FileChange>();

        var currentCommit = repo.Lookup<GitCommit>(branchState.CurrentSha!);
        if (currentCommit == null)
        {
            _logger.LogWarning("Current commit {Sha} not found in repository {Repo}",
                branchState.CurrentSha, repoState.Name);
            return changes;
        }

        GitCommit? lastIndexedCommit = null;
        if (!string.IsNullOrEmpty(branchState.LastIndexedSha))
        {
            lastIndexedCommit = repo.Lookup<GitCommit>(branchState.LastIndexedSha);
        }

        // If no previous index, get all files from current commit (recursively)
        if (lastIndexedCommit == null)
        {
            // Recursively traverse the entire tree to get all files
            void TraverseTree(Tree tree, string basePath = "")
            {
                foreach (var entry in tree)
                {
                    var fullPath = string.IsNullOrEmpty(basePath) ? entry.Name : $"{basePath}/{entry.Name}";

                    if (entry.TargetType == TreeEntryTargetType.Blob)
                    {
                        changes.Add(new FileChange
                        {
                            RepositoryName = repoState.Name,
                            BranchName = branchState.Name,
                            CommitSha = currentCommit.Sha,
                            FilePath = fullPath,
                            ChangeType = ChangeType.Added
                        });
                    }
                    else if (entry.TargetType == TreeEntryTargetType.Tree)
                    {
                        // Recursively traverse subdirectory
                        var subTree = (Tree)entry.Target;
                        TraverseTree(subTree, fullPath);
                    }
                }
            }

            TraverseTree(currentCommit.Tree);
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
    /// Backwards-compatible alias for <see cref="MarkBranchAsIndexed"/> to avoid breaking existing callers/tests.
    /// </summary>
    public void MarkBranchIndexed(string repositoryName, string branchName)
    {
        MarkBranchAsIndexed(repositoryName, branchName);
    }


    /// <summary>
    /// Gets all tracked repositories.
    /// </summary>
    public IReadOnlyDictionary<string, RepositoryState> GetRepositories() => _repositories;

    /// <summary>
    /// Gets all remote branches for a repository without tracking them.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetRemoteBranchesAsync(string repositoryName, CancellationToken cancellationToken = default)
    {
        if (!_repositories.TryGetValue(repositoryName, out var repoState))
        {
            throw new InvalidOperationException($"Repository {repositoryName} is not initialized");
        }

        return await Task.Run(() =>
        {
            using var repo = new GitRepository(repoState.LocalPath);

            // Get all remote branches (origin/*)
            return repo.Branches
                .Where(b => b.IsRemote && b.FriendlyName.StartsWith("origin/", StringComparison.Ordinal))
                .Select(b => b.FriendlyName["origin/".Length..])
                .ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Checks if a branch exists remotely without tracking it.
    /// </summary>
    public async Task<bool> BranchExistsAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default)
    {
        if (!_repositories.TryGetValue(repositoryName, out var repoState))
        {
            throw new InvalidOperationException($"Repository {repositoryName} is not initialized");
        }

        return await Task.Run(() =>
        {
            using var repo = new GitRepository(repoState.LocalPath);
            var remoteBranchName = $"origin/{branchName}";
            return repo.Branches[remoteBranchName] is not null;
        }, cancellationToken);
    }

    /// <summary>
    /// Ensures a branch is tracked and up-to-date by always fetching the latest data from remote.
    /// If the branch is not tracked, it will be tracked on-demand.
    /// Always fetches from remote to ensure we have the latest commits.
    /// </summary>
    public async Task<BranchState> EnsureBranchTrackedAsync(string repositoryName, string branchName, CancellationToken cancellationToken = default)
    {
        if (!_repositories.TryGetValue(repositoryName, out var repoState))
        {
            throw new InvalidOperationException($"Repository {repositoryName} is not initialized");
        }

        // Always fetch and update the branch to ensure we have the latest data
        // This is important because changes could have happened at any time
        _logger.LogDebug("Fetching latest data for branch {Branch} in repository {Repo}", branchName, repositoryName);

        var branchState = await UpdateBranchAsync(repoState, branchName, cancellationToken);
        branchState.LastAccessed = DateTimeOffset.UtcNow;

        return branchState;
    }

    /// <summary>
    /// Cleans up stale branches that haven't been accessed for the specified number of days.
    /// Returns the number of branches removed.
    /// </summary>
    public async Task<int> CleanupStaleBranchesAsync(int staleDays, CancellationToken cancellationToken = default)
    {
        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-staleDays);
            var totalRemoved = 0;

            foreach (var (repoName, repoState) in _repositories)
            {
                var staleBranches = repoState.Branches
                    .Where(kvp => kvp.Value.LastAccessed < cutoffDate && kvp.Key != repoState.DefaultBranch)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var branchName in staleBranches)
                {
                    if (repoState.Branches.Remove(branchName, out var removedBranch))
                    {
                        _logger.LogInformation(
                            "Removed stale branch {Branch} from repository {Repo} (last accessed: {LastAccessed})",
                            branchName, repoName, removedBranch.LastAccessed);
                        totalRemoved++;
                    }
                }

                if (staleBranches.Count > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} stale branches from repository {Repo}", staleBranches.Count, repoName);
                }
            }

            if (totalRemoved > 0)
            {
                _logger.LogInformation("Total stale branches cleaned up: {Count} (older than {Days} days)", totalRemoved, staleDays);
            }
            else
            {
                _logger.LogDebug("No stale branches found for cleanup");
            }

            return totalRemoved;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Gets the local path for a repository.
    /// </summary>
    public string? GetRepositoryPath(string repositoryName)
    {
        return _repositories.TryGetValue(repositoryName, out var repoState) ? repoState.LocalPath : null;
    }

    /// <summary>
    /// Ensures repository metadata exists in the database.
    /// Creates or updates the repository record.
    /// </summary>
    private async Task<Models.Repository> EnsureRepositoryInDatabaseAsync(RepositoryState state, CancellationToken cancellationToken)
    {
        var existingRepo = await _repositoryRepository.GetByNameAsync(state.Name, cancellationToken);

        if (existingRepo != null)
        {
            _logger.LogDebug("Repository {Name} already exists in database with ID {Id}", state.Name, existingRepo.Id);
            return existingRepo;
        }

        var newRepo = new Models.Repository
        {
            Id = state.Name,
            Name = state.Name,
            RemoteUrl = state.RemoteUrl,
            DefaultBranch = state.DefaultBranch
        };

        var createdRepo = await _repositoryRepository.CreateAsync(newRepo, cancellationToken);
        _logger.LogInformation("Created repository {Name} in database with ID {Id}", state.Name, createdRepo.Id);

        return createdRepo;
    }

    /// <summary>
    /// Ensures branch metadata exists in the database.
    /// Creates or updates the branch record.
    /// </summary>
    private async Task<Models.Branch> EnsureBranchInDatabaseAsync(
        RepositoryState repoState,
        string branchName,
        string headCommitSha,
        CancellationToken cancellationToken)
    {
        // Ensure repository exists in database first
        var repo = await EnsureRepositoryInDatabaseAsync(repoState, cancellationToken);

        // Check if branch already exists
        var existingBranch = await _branchRepository.GetByRepoAndNameAsync(repo.Id, branchName, cancellationToken);

        if (existingBranch != null)
        {
            // Update if HEAD has changed
            if (existingBranch.HeadCommitSha != headCommitSha)
            {
                var updatedBranch = new Models.Branch
                {
                    Id = existingBranch.Id,
                    RepoId = existingBranch.RepoId,
                    Name = existingBranch.Name,
                    HeadCommitSha = headCommitSha,
                    IndexState = IndexState.Stale, // Mark as stale when HEAD changes
                    IndexedCommitSha = existingBranch.IndexedCommitSha,
                    LastIndexedAt = existingBranch.LastIndexedAt,
                    CreatedAt = existingBranch.CreatedAt,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                var result = await _branchRepository.UpdateAsync(updatedBranch, cancellationToken);
                _logger.LogDebug("Updated branch {Branch} in database, HEAD changed to {Sha}", branchName, headCommitSha);
                return result;
            }

            return existingBranch;
        }

        // Create new branch
        var newBranch = new Models.Branch
        {
            RepoId = repo.Id,
            Name = branchName,
            HeadCommitSha = headCommitSha,
            IndexState = IndexState.Pending
        };

        var createdBranch = await _branchRepository.CreateAsync(newBranch, cancellationToken);
        _logger.LogInformation("Created branch {Branch} in database for repository {Repo}", branchName, repoState.Name);

        return createdBranch;
    }

    /// <summary>
    /// Updates branch index state in the database after successful indexing.
    /// </summary>
    private async Task UpdateBranchIndexStateAsync(
        string repositoryName,
        string branchName,
        string indexedCommitSha,
        CancellationToken cancellationToken)
    {
        var repo = await _repositoryRepository.GetByNameAsync(repositoryName, cancellationToken);
        if (repo == null)
        {
            _logger.LogWarning("Repository {Name} not found in database, skipping branch state update", repositoryName);
            return;
        }

        var branch = await _branchRepository.GetByRepoAndNameAsync(repo.Id, branchName, cancellationToken);
        if (branch == null)
        {
            _logger.LogWarning("Branch {Branch} not found in database for repository {Repo}, skipping state update", branchName, repositoryName);
            return;
        }

        await _branchRepository.UpdateIndexStateAsync(branch.Id, IndexState.Completed, indexedCommitSha, cancellationToken);
        _logger.LogDebug("Updated branch {Branch} index state to Completed in database", branchName);
    }

    /// <summary>
    /// Loads existing branch state from the database into in-memory state.
    /// This allows the service to resume tracking branches after a restart.
    /// </summary>
    private async Task LoadBranchStateFromDatabaseAsync(
        RepositoryState repoState,
        string repoId,
        CancellationToken cancellationToken)
    {
        var branches = await _branchRepository.GetByRepoIdAsync(repoId, cancellationToken);
        var branchList = branches.ToList();

        if (branchList.Count == 0)
        {
            _logger.LogDebug("No existing branches found in database for repository {Repo}", repoState.Name);
            return;
        }

        foreach (var dbBranch in branchList)
        {
            // Only load branches that have been indexed (have IndexedCommitSha)
            // Skip branches that are only Pending and have never been indexed
            if (dbBranch.IndexedCommitSha == null && dbBranch.IndexState == IndexState.Pending)
            {
                _logger.LogDebug("Skipping pending branch {Branch} that has never been indexed", dbBranch.Name);
                continue;
            }

            var branchState = new BranchState
            {
                Name = dbBranch.Name,
                CurrentSha = dbBranch.HeadCommitSha,
                LastIndexedSha = dbBranch.IndexedCommitSha,
                LastIndexed = dbBranch.LastIndexedAt,
                LastAccessed = DateTimeOffset.UtcNow // Set to now since we're loading on startup
            };

            repoState.Branches[dbBranch.Name] = branchState;
            _logger.LogInformation(
                "Loaded branch {Branch} from database: HEAD={HeadSha}, LastIndexed={IndexedSha}, State={State}",
                dbBranch.Name,
                dbBranch.HeadCommitSha[..7],
                dbBranch.IndexedCommitSha?[..7] ?? "none",
                dbBranch.IndexState);
        }

        _logger.LogInformation("Loaded {Count} branch(es) from database for repository {Repo}", repoState.Branches.Count, repoState.Name);
    }

    /// <summary>
    /// Gets the content of a file from a specific commit in a repository.
    /// Reads directly from Git object database (pack files), not from working directory.
    /// </summary>
    /// <param name="repositoryName">Repository name.</param>
    /// <param name="commitSha">Commit SHA.</param>
    /// <param name="filePath">File path relative to repository root.</param>
    /// <returns>File content as string, or null if file not found.</returns>
    public async Task<string?> GetFileContentAsync(string repositoryName, string commitSha, string filePath, CancellationToken cancellationToken = default)
    {
        if (!_repositories.TryGetValue(repositoryName, out var repoState))
        {
            throw new InvalidOperationException($"Repository {repositoryName} is not initialized");
        }

        return await Task.Run(() =>
        {
            using var repo = new GitRepository(repoState.LocalPath);

            // Look up the commit
            var commit = repo.Lookup<GitCommit>(commitSha);
            if (commit == null)
            {
                _logger.LogWarning("Commit {Sha} not found in repository {Repo}", commitSha, repositoryName);
                return null;
            }

            // Navigate to the file in the commit's tree
            var treeEntry = commit[filePath];
            if (treeEntry == null)
            {
                _logger.LogDebug("File {FilePath} not found in commit {Sha}", filePath, commitSha);
                return null;
            }

            // Check if it's a blob (file)
            if (treeEntry.TargetType != TreeEntryTargetType.Blob)
            {
                _logger.LogDebug("Path {FilePath} is not a file in commit {Sha}", filePath, commitSha);
                return null;
            }

            // Get the blob and read its content
            var blob = (Blob)treeEntry.Target;

            // Check if it's a binary file
            if (blob.IsBinary)
            {
                _logger.LogDebug("File {FilePath} is binary, skipping", filePath);
                return null;
            }

            // Read content as text
            return blob.GetContentText();
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _updateLock.Dispose();
        _disposed = true;
    }
}
