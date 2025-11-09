using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Concurrent;

namespace LancerMcp.Services;

/// <summary>
/// Loads and caches MSBuild workspaces for repositories to provide full semantic analysis.
/// </summary>
public sealed class WorkspaceLoader : IDisposable
{
    private readonly ILogger<WorkspaceLoader> _logger;
    private readonly ConcurrentDictionary<string, WorkspaceCache> _workspaceCache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perKeyLocks = new();
    private volatile bool _disposed;

    public WorkspaceLoader(ILogger<WorkspaceLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets or loads a workspace for the given repository path and branch.
    /// Returns a handle with reference counting for safe disposal.
    /// Returns null if the workspace cannot be loaded.
    /// </summary>
    public async Task<WorkspaceHandle?> GetOrLoadWorkspaceAsync(string repositoryPath, string branchName, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorkspaceLoader));
        }

        // Cache key includes both repository path and branch name
        var cacheKey = $"{repositoryPath}:{branchName}";

        // Fast path: Check cache first without locking
        if (_workspaceCache.TryGetValue(cacheKey, out var cached))
        {
            return cached.AcquireReference();
        }

        // Slow path: Load workspace with lock to prevent concurrent loads for the same key
        // Use per-key locking to minimize contention between different repositories/branches
        var perKeyLock = _perKeyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));

        await perKeyLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock (another thread may have loaded it)
            if (_workspaceCache.TryGetValue(cacheKey, out cached))
            {
                return cached.AcquireReference();
            }

            _logger.LogInformation("Loading MSBuild workspace for repository: {RepositoryPath}, branch: {BranchName}", repositoryPath, branchName);

            // Note: Branch checkout is handled by GitTrackerService.EnsureBranchCheckedOutAsync
            // which is called by IndexingService before loading workspace.
            // This ensures synchronized access to the working tree.

            var workspace = await LoadWorkspaceAsync(repositoryPath, branchName, cancellationToken);
            if (workspace != null)
            {
                _workspaceCache[cacheKey] = workspace;
                _logger.LogInformation(
                    "Loaded workspace with {ProjectCount} project(s) for repository: {RepositoryPath}, branch: {BranchName}",
                    workspace.Projects.Count,
                    repositoryPath,
                    branchName);
                return workspace.AcquireReference();
            }

            return null;
        }
        finally
        {
            perKeyLock.Release();
        }
    }

    /// <summary>
    /// Loads an MSBuild workspace from the repository path.
    /// Looks for .sln files first, then .csproj files.
    /// </summary>
    private async Task<WorkspaceCache?> LoadWorkspaceAsync(string repositoryPath, string branchName, CancellationToken cancellationToken)
    {
        MSBuildWorkspace? workspace = null;
        try
        {
            // Find solution or project files, skipping .git and build output directories
            // This is much faster than Directory.GetFiles with AllDirectories which walks .git/objects
            var solutionFiles = FindFilesExcludingDirectories(repositoryPath, "*.sln");
            var projectFiles = FindFilesExcludingDirectories(repositoryPath, "*.csproj");

            if (solutionFiles.Count == 0 && projectFiles.Count == 0)
            {
                _logger.LogWarning("No .sln or .csproj files found in repository: {RepositoryPath}", repositoryPath);
                return null;
            }

            // Create MSBuild workspace
            workspace = MSBuildWorkspace.Create();

            // Suppress diagnostics for cleaner logs
            workspace.WorkspaceFailed += (sender, args) =>
            {
                if (args.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                {
                    _logger.LogWarning("Workspace diagnostic: {Message}", args.Diagnostic.Message);
                }
            };

            // Load solution if available, otherwise load first project
            Solution solution;
            if (solutionFiles.Count > 0)
            {
                var solutionPath = solutionFiles[0];
                _logger.LogInformation("Loading solution: {SolutionPath}", solutionPath);
                solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
            }
            else
            {
                var projectPath = projectFiles[0];
                _logger.LogInformation("Loading project: {ProjectPath}", projectPath);
                var project = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken);
                solution = project.Solution;
            }

            // Build cache of projects and compilations
            var projects = new Dictionary<string, ProjectCache>();
            foreach (var project in solution.Projects)
            {
                // Only process C# projects
                if (project.Language != LanguageNames.CSharp)
                {
                    continue;
                }

                try
                {
                    var compilation = await project.GetCompilationAsync(cancellationToken);
                    if (compilation != null)
                    {
                        projects[project.FilePath ?? project.Name] = new ProjectCache
                        {
                            Project = project,
                            Compilation = compilation
                        };

                        _logger.LogDebug(
                            "Loaded project: {ProjectName} with {DocumentCount} documents",
                            project.Name,
                            project.Documents.Count());
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get compilation for project: {ProjectName}", project.Name);
                }
            }

            if (projects.Count == 0)
            {
                _logger.LogWarning("No C# projects with compilations found in workspace");
                workspace.Dispose();
                return null;
            }

            return new WorkspaceCache
            {
                Workspace = workspace,
                Solution = solution,
                Projects = projects,
                RepositoryPath = repositoryPath,
                BranchName = branchName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workspace for repository: {RepositoryPath}", repositoryPath);
            // Dispose workspace if it was created but loading failed
            workspace?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Clears the workspace cache for a repository (e.g., after a git pull).
    /// If branchName is specified, only clears that branch's cache.
    /// Otherwise, clears all branches for the repository.
    ///
    /// This marks workspaces for disposal and removes them from the cache.
    /// Actual disposal happens when all active references are released (safe for concurrent indexing).
    /// Also removes the corresponding per-key locks to prevent unbounded memory growth.
    /// </summary>
    public void ClearCache(string repositoryPath, string? branchName = null)
    {
        if (branchName != null)
        {
            // Clear specific branch
            var cacheKey = $"{repositoryPath}:{branchName}";
            if (_workspaceCache.TryRemove(cacheKey, out var workspace))
            {
                workspace.MarkForDisposal();

                // Remove and dispose the corresponding lock
                if (_perKeyLocks.TryRemove(cacheKey, out var lockObj))
                {
                    lockObj.Dispose();
                }

                _logger.LogInformation("Cleared workspace cache for repository: {RepositoryPath}, branch: {BranchName}", repositoryPath, branchName);
            }
        }
        else
        {
            // Clear all branches for this repository
            var keysToRemove = _workspaceCache.Keys
                .Where(k => k.StartsWith($"{repositoryPath}:", StringComparison.Ordinal))
                .ToList();

            foreach (var key in keysToRemove)
            {
                if (_workspaceCache.TryRemove(key, out var workspace))
                {
                    workspace.MarkForDisposal();

                    // Remove and dispose the corresponding lock
                    if (_perKeyLocks.TryRemove(key, out var lockObj))
                    {
                        lockObj.Dispose();
                    }
                }
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogInformation("Cleared workspace cache for {Count} branch(es) in repository: {RepositoryPath}", keysToRemove.Count, repositoryPath);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose all workspaces
        foreach (var cache in _workspaceCache.Values)
        {
            cache.Workspace.Dispose();
        }
        _workspaceCache.Clear();

        // Dispose any remaining per-key locks
        // Note: Locks are normally removed when cache entries are evicted via ClearCache(),
        // but we clean up any remaining locks here during final disposal.
        foreach (var lockObj in _perKeyLocks.Values)
        {
            lockObj.Dispose();
        }
        _perKeyLocks.Clear();
    }

    /// <summary>
    /// Recursively finds files matching a pattern while excluding build/IDE directories.
    /// This is much more efficient than Directory.GetFiles with AllDirectories which walks .git/objects.
    ///
    /// Note: This uses a dedicated hard-coded list for workspace discovery, NOT the user-configurable
    /// ExcludeFolders (which controls indexing scope). This ensures we can always find .sln/.csproj files
    /// even if users exclude certain directories from indexing.
    /// </summary>
    private static List<string> FindFilesExcludingDirectories(string rootPath, string pattern)
    {
        // Hard-coded list of directories to skip during workspace discovery
        // These are build outputs, IDE folders, and version control that should never contain .sln/.csproj
        var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            "bin",
            "obj",
            "node_modules",
            ".vs",
            ".vscode",
            "packages",
            "dist",
            "build",
            "target",
            ".next",
            ".nuget"
        };

        var results = new List<string>();
        var dirsToSearch = new Queue<string>();
        dirsToSearch.Enqueue(rootPath);

        while (dirsToSearch.Count > 0)
        {
            var currentDir = dirsToSearch.Dequeue();

            try
            {
                // Add matching files from current directory
                results.AddRange(Directory.GetFiles(currentDir, pattern, SearchOption.TopDirectoryOnly));

                // Queue subdirectories that aren't excluded
                foreach (var subDir in Directory.GetDirectories(currentDir))
                {
                    var dirName = Path.GetFileName(subDir);
                    if (!excludedDirs.Contains(dirName))
                    {
                        dirsToSearch.Enqueue(subDir);
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
            catch (DirectoryNotFoundException)
            {
                // Skip directories that were deleted during traversal
            }
        }

        return results;
    }
}

/// <summary>
/// Cached workspace data for a repository branch with reference counting for safe disposal.
/// Thread-safe: All access to _referenceCount and _isMarkedForDisposal is protected by _lock.
/// </summary>
public sealed class WorkspaceCache : IDisposable
{
    // All access to these fields is protected by _lock for thread safety
    private int _referenceCount;
    private bool _isMarkedForDisposal;
    private readonly object _lock = new();

    public required MSBuildWorkspace Workspace { get; init; }
    public required Solution Solution { get; init; }
    public required Dictionary<string, ProjectCache> Projects { get; init; }
    public required string RepositoryPath { get; init; }
    public required string BranchName { get; init; }

    /// <summary>
    /// Increments the reference count. Returns a disposable handle that decrements on disposal.
    /// </summary>
    public WorkspaceHandle AcquireReference()
    {
        lock (_lock)
        {
            _referenceCount++;
            return new WorkspaceHandle(this);
        }
    }

    /// <summary>
    /// Decrements the reference count and disposes if marked for disposal and count reaches zero.
    /// </summary>
    internal void ReleaseReference()
    {
        lock (_lock)
        {
            _referenceCount--;
            if (_referenceCount == 0 && _isMarkedForDisposal)
            {
                DisposeInternal();
            }
        }
    }

    /// <summary>
    /// Marks this workspace for disposal. Will dispose immediately if no references, otherwise waits.
    /// </summary>
    public void MarkForDisposal()
    {
        lock (_lock)
        {
            _isMarkedForDisposal = true;
            if (_referenceCount == 0)
            {
                DisposeInternal();
            }
        }
    }

    private void DisposeInternal()
    {
        Workspace?.Dispose();
    }

    public void Dispose()
    {
        MarkForDisposal();
    }
}

/// <summary>
/// Handle that represents a reference to a WorkspaceCache. Releases the reference on disposal.
/// </summary>
public sealed class WorkspaceHandle : IDisposable
{
    private readonly WorkspaceCache _cache;
    private bool _disposed;

    internal WorkspaceHandle(WorkspaceCache cache)
    {
        _cache = cache;
    }

    public WorkspaceCache Cache => _cache;

    public void Dispose()
    {
        if (!_disposed)
        {
            _cache.ReleaseReference();
            _disposed = true;
        }
    }
}

/// <summary>
/// Cached project data.
/// </summary>
public sealed class ProjectCache
{
    public required Project Project { get; init; }
    public required Compilation Compilation { get; init; }
}

