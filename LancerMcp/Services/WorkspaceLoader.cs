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
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public WorkspaceLoader(ILogger<WorkspaceLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets or loads a workspace for the given repository path.
    /// Returns null if the workspace cannot be loaded.
    /// </summary>
    public async Task<WorkspaceCache?> GetOrLoadWorkspaceAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_workspaceCache.TryGetValue(repositoryPath, out var cached))
        {
            return cached;
        }

        // Load workspace (with lock to prevent concurrent loads)
        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_workspaceCache.TryGetValue(repositoryPath, out cached))
            {
                return cached;
            }

            _logger.LogInformation("Loading MSBuild workspace for repository: {RepositoryPath}", repositoryPath);

            var workspace = await LoadWorkspaceAsync(repositoryPath, cancellationToken);
            if (workspace != null)
            {
                _workspaceCache[repositoryPath] = workspace;
                _logger.LogInformation(
                    "Loaded workspace with {ProjectCount} project(s) for repository: {RepositoryPath}",
                    workspace.Projects.Count,
                    repositoryPath);
            }

            return workspace;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Loads an MSBuild workspace from the repository path.
    /// Looks for .sln files first, then .csproj files.
    /// </summary>
    private async Task<WorkspaceCache?> LoadWorkspaceAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        try
        {
            // Find solution or project files (search recursively up to 2 levels deep)
            var solutionFiles = Directory.GetFiles(repositoryPath, "*.sln", SearchOption.AllDirectories);
            var projectFiles = Directory.GetFiles(repositoryPath, "*.csproj", SearchOption.AllDirectories);

            // Filter out obj/bin directories
            solutionFiles = solutionFiles.Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("\\obj\\") && !f.Contains("\\bin\\")).ToArray();
            projectFiles = projectFiles.Where(f => !f.Contains("/obj/") && !f.Contains("/bin/") && !f.Contains("\\obj\\") && !f.Contains("\\bin\\")).ToArray();

            if (solutionFiles.Length == 0 && projectFiles.Length == 0)
            {
                _logger.LogWarning("No .sln or .csproj files found in repository: {RepositoryPath}", repositoryPath);
                return null;
            }

            // Create MSBuild workspace
            var workspace = MSBuildWorkspace.Create();

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
            if (solutionFiles.Length > 0)
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
                RepositoryPath = repositoryPath
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load workspace for repository: {RepositoryPath}", repositoryPath);
            return null;
        }
    }

    /// <summary>
    /// Clears the workspace cache for a repository (e.g., after a git pull).
    /// </summary>
    public void ClearCache(string repositoryPath)
    {
        if (_workspaceCache.TryRemove(repositoryPath, out var cache))
        {
            cache.Workspace.Dispose();
            _logger.LogInformation("Cleared workspace cache for repository: {RepositoryPath}", repositoryPath);
        }
    }

    public void Dispose()
    {
        foreach (var cache in _workspaceCache.Values)
        {
            cache.Workspace.Dispose();
        }
        _workspaceCache.Clear();
        _loadLock.Dispose();
    }
}

/// <summary>
/// Cached workspace data for a repository.
/// </summary>
public sealed class WorkspaceCache
{
    public required MSBuildWorkspace Workspace { get; init; }
    public required Solution Solution { get; init; }
    public required Dictionary<string, ProjectCache> Projects { get; init; }
    public required string RepositoryPath { get; init; }
}

/// <summary>
/// Cached project data.
/// </summary>
public sealed class ProjectCache
{
    public required Project Project { get; init; }
    public required Compilation Compilation { get; init; }
}

