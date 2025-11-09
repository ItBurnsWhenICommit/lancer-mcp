using Microsoft.Extensions.Options;
using LancerMcp.Configuration;
using LancerMcp.Models;
using System.Collections.Concurrent;

namespace LancerMcp.Services;

/// <summary>
/// Orchestrates the indexing pipeline: language detection, parsing, symbol extraction, chunking, embedding, and storage.
/// </summary>
public sealed class IndexingService
{
    private readonly ILogger<IndexingService> _logger;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly DatabaseService _db;
    private readonly GitTrackerService _gitTracker;
    private readonly LanguageDetectionService _languageDetection;
    private readonly RoslynParserService _roslynParser;
    private readonly BasicParserService _basicParser;
    private readonly ChunkingService _chunkingService;
    private readonly EmbeddingService _embeddingService;
    private readonly WorkspaceLoader _workspaceLoader;
    private readonly PersistenceService _persistenceService;
    private readonly EdgeResolutionService _edgeResolutionService;
    private readonly SemaphoreSlim _concurrencyLimiter;

    // Static lock dictionary to prevent concurrent workspace checkouts across IndexingService instances
    // Key: "repositoryName:branchName"
    // Locks are created on-demand and reused across IndexingService instances to prevent concurrent
    // checkout operations on the same repository/branch combination.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _checkoutLocks = new();

    // Track last access time for each lock to enable cleanup of unused locks
    private static readonly ConcurrentDictionary<string, DateTime> _lockLastAccess = new();

    public IndexingService(
        ILogger<IndexingService> logger,
        IOptionsMonitor<ServerOptions> options,
        DatabaseService db,
        GitTrackerService gitTracker,
        LanguageDetectionService languageDetection,
        RoslynParserService roslynParser,
        BasicParserService basicParser,
        ChunkingService chunkingService,
        EmbeddingService embeddingService,
        WorkspaceLoader workspaceLoader,
        PersistenceService persistenceService,
        EdgeResolutionService edgeResolutionService)
    {
        _logger = logger;
        _options = options;
        _db = db;
        _gitTracker = gitTracker;
        _languageDetection = languageDetection;
        _roslynParser = roslynParser;
        _basicParser = basicParser;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _workspaceLoader = workspaceLoader;
        _persistenceService = persistenceService;
        _edgeResolutionService = edgeResolutionService;

        var concurrency = options.CurrentValue.FileReadConcurrency;
        _concurrencyLimiter = new SemaphoreSlim(concurrency, concurrency);
    }

    /// <summary>
    /// Indexes a batch of file changes, persists to PostgreSQL, and automatically marks the branch as indexed.
    /// </summary>
    public async Task<IndexingResult> IndexFilesAsync(IEnumerable<FileChange> fileChanges, CancellationToken cancellationToken = default)
    {
        var result = new IndexingResult();
        var fileChangesList = fileChanges.ToList();

        if (!fileChangesList.Any())
        {
            return result;
        }

        // Step 0: Group file changes by repository/branch to ensure we use the correct workspace for each group
        // This is critical - if a batch contains changes from multiple repositories/branches, we need to load the right workspace for each
        var groupedChanges = fileChangesList
            .GroupBy(fc => new { fc.RepositoryName, fc.BranchName })
            .ToList();

        _logger.LogDebug("Processing {GroupCount} repository/branch group(s)", groupedChanges.Count);

        // Step 1: Process each repository/branch group with its own workspace
        var allTasks = new List<Task<ParsedFile?>>();
        var workspaceHandles = new List<WorkspaceHandle>();

        foreach (var group in groupedChanges)
        {
            var repositoryPath = _gitTracker.GetRepositoryPath(group.Key.RepositoryName);
            WorkspaceCache? workspaceCache = null;

            if (!string.IsNullOrEmpty(repositoryPath))
            {
                // Get or create a lock for this repository/branch combination
                // Note: GetOrAdd can create multiple SemaphoreSlim instances under concurrent access,
                // though only one will be stored. The unused instances will not be disposed, resulting
                // in a minor memory leak. This is an acceptable tradeoff for performance, as using a
                // synchronized lock pattern would introduce contention. Unused locks are cleaned up
                // periodically via CleanupUnusedCheckoutLocks().
                var lockKey = $"{group.Key.RepositoryName}:{group.Key.BranchName}";
                var checkoutLock = _checkoutLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

                // Track access time for cleanup
                _lockLastAccess[lockKey] = DateTime.UtcNow;

                // Acquire lock to prevent concurrent checkout operations across IndexingService instances
                await checkoutLock.WaitAsync(cancellationToken);
                try
                {
                    // Ensure the branch is checked out BEFORE loading workspace
                    // This is synchronized at both IndexingService and GitTrackerService levels
                    await _gitTracker.EnsureBranchCheckedOutAsync(group.Key.RepositoryName, group.Key.BranchName, cancellationToken);

                    // Load workspace for this specific repository AND branch
                    // This returns a handle with reference counting for safe disposal
                    var handle = await _workspaceLoader.GetOrLoadWorkspaceAsync(repositoryPath, group.Key.BranchName, cancellationToken);
                    if (handle != null)
                    {
                        workspaceHandles.Add(handle);
                        workspaceCache = handle.Cache;
                    }
                }
                finally
                {
                    checkoutLock.Release();
                }
            }

            // Parse all files in this group with the correct workspace
            foreach (var fileChange in group)
            {
                // Handle deleted files by removing their data from the database
                if (fileChange.ChangeType == ChangeType.Deleted)
                {
                    try
                    {
                        await _persistenceService.DeleteFileDataAsync(fileChange.RepositoryName, fileChange.BranchName, fileChange.FilePath, cancellationToken);
                        result.DeletedFiles.Add(fileChange.FilePath);
                        _logger.LogInformation("Deleted indexed data for removed file: {FilePath}", fileChange.FilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to delete indexed data for file: {FilePath}", fileChange.FilePath);
                        result.FailedFiles++;
                    }
                    continue;
                }

                allTasks.Add(IndexFileAsync(fileChange, workspaceCache, cancellationToken));
            }
        }

        var parsedFiles = await Task.WhenAll(allTasks);

        try
        {
            foreach (var parsedFile in parsedFiles)
            {
                if (parsedFile == null)
                {
                    result.SkippedFiles++;
                    continue;
                }

                if (parsedFile.Success)
                {
                    result.ParsedFiles.Add(parsedFile);
                    result.TotalSymbols += parsedFile.Symbols.Count;
                    result.TotalEdges += parsedFile.Edges.Count;
                }
                else
                {
                    result.FailedFiles++;
                    _logger.LogWarning("Failed to parse {FilePath}: {Error}", parsedFile.FilePath, parsedFile.ErrorMessage);
                }
            }

            _logger.LogInformation(
                "Parsing complete: {ParsedCount} parsed, {SkippedCount} skipped, {FailedCount} failed, {DeletedCount} deleted, {SymbolCount} symbols, {EdgeCount} edges",
                result.ParsedFiles.Count,
                result.SkippedFiles,
                result.FailedFiles,
                result.DeletedFiles.Count,
                result.TotalSymbols,
                result.TotalEdges);

            // Step 2: Persist to PostgreSQL
            if (result.ParsedFiles.Any())
            {
                await PersistToStorageAsync(result.ParsedFiles, cancellationToken);
            }
        }
        finally
        {
            // Release workspace handles after persistence is complete
            // This ensures workspace resources are available for retry if persistence fails
            // This decrements reference counts and allows disposal if workspaces were marked for disposal
            foreach (var handle in workspaceHandles)
            {
                handle.Dispose();
            }
        }

        // Step 3: Automatically mark all branches as indexed after successful indexing
        foreach (var group in groupedChanges)
        {
            _gitTracker.MarkBranchAsIndexed(group.Key.RepositoryName, group.Key.BranchName);
        }

        return result;
    }

    /// <summary>
    /// Indexes a single file.
    /// </summary>
    private async Task<ParsedFile?> IndexFileAsync(FileChange fileChange, WorkspaceCache? workspaceCache, CancellationToken cancellationToken)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            // Read file content directly from Git blob
            var content = await _gitTracker.GetFileContentAsync(
                fileChange.RepositoryName,
                fileChange.CommitSha,
                fileChange.FilePath,
                cancellationToken);

            if (content == null)
            {
                _logger.LogDebug("File not found or is binary: {FilePath}", fileChange.FilePath);
                return null;
            }

            // Check file size
            if (content.Length > _options.CurrentValue.MaxFileBytes)
            {
                _logger.LogDebug("File too large ({Size} bytes): {FilePath}", content.Length, fileChange.FilePath);
                return null;
            }

            // Detect language
            var language = _languageDetection.DetectLanguage(fileChange.FilePath, content);

            // Check if we should index this language
            if (!_languageDetection.ShouldIndex(language))
            {
                _logger.LogDebug("Skipping {Language} file: {FilePath}", language, fileChange.FilePath);
                return null;
            }

            // Parse based on language
            ParsedFile parsedFile;
            if (language == Language.CSharp)
            {
                // Try to find the compilation for this file's project
                Microsoft.CodeAnalysis.Compilation? compilation = null;
                if (workspaceCache != null)
                {
                    compilation = FindCompilationForFile(workspaceCache, fileChange.FilePath);
                }

                parsedFile = await _roslynParser.ParseFileAsync(
                    fileChange.RepositoryName,
                    fileChange.BranchName,
                    fileChange.CommitSha,
                    fileChange.FilePath,
                    content,
                    compilation,
                    cancellationToken);
            }
            else
            {
                parsedFile = await _basicParser.ParseFileAsync(
                    fileChange.RepositoryName,
                    fileChange.BranchName,
                    fileChange.CommitSha,
                    fileChange.FilePath,
                    content,
                    language,
                    cancellationToken);
            }

            // Cache the source text to avoid re-reading from Git during chunking.
            // ChunkingService.ChunkFileAsync() uses this cached text to avoid the performance
            // cost of reading from Git's object database again. Without this cache, each file
            // would be read from Git twice: once for parsing and once for chunking, which is
            // expensive for large repositories.
            parsedFile.SourceText = content;

            return parsedFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error indexing file: {FilePath}", fileChange.FilePath);
            return new ParsedFile
            {
                RepositoryName = fileChange.RepositoryName,
                BranchName = fileChange.BranchName,
                CommitSha = fileChange.CommitSha,
                FilePath = fileChange.FilePath,
                Language = Language.Unknown,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    /// <summary>
    /// Persists parsed files, symbols, edges, chunks, and embeddings to PostgreSQL.
    /// All operations are wrapped in a single database transaction to ensure atomicity.
    /// If any step fails, the entire transaction is rolled back, preventing partial writes or data corruption.
    /// </summary>
    private async Task PersistToStorageAsync(List<ParsedFile> parsedFiles, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Persisting {Count} files to storage (transactional)...", parsedFiles.Count);

        // Step 1: Chunk files (CPU-heavy, do BEFORE transaction)
        _logger.LogInformation("Chunking {Count} files...", parsedFiles.Count);
        var allChunks = new List<CodeChunk>();
        foreach (var parsedFile in parsedFiles)
        {
            var chunkedFile = await _chunkingService.ChunkFileAsync(parsedFile, cancellationToken);
            allChunks.AddRange(chunkedFile.Chunks);
        }
        _logger.LogInformation("Generated {Count} chunks", allChunks.Count);

        // Step 2: Generate embeddings (HTTP calls, do BEFORE transaction)
        List<Embedding> embeddings = new();
        if (allChunks.Any())
        {
            _logger.LogInformation("Generating embeddings for {Count} chunks...", allChunks.Count);
            embeddings = await _embeddingService.GenerateEmbeddingsAsync(allChunks, cancellationToken);
            _logger.LogInformation("Generated {Count} embeddings", embeddings.Count);
        }

        // Step 3: Open transaction and persist all data atomically
        await using var connection = await _db.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Step 3a: Delete old data for files being re-indexed
            var filesToDelete = parsedFiles
                .Select(f => new { f.RepositoryName, f.BranchName, f.FilePath })
                .Distinct()
                .ToList();

            foreach (var file in filesToDelete)
            {
                await _persistenceService.DeleteFileDataAsync(file.RepositoryName, file.BranchName, file.FilePath, cancellationToken, connection, transaction);
            }

            // Step 3b: Persist commits
            // Fetch commit metadata from Git for each unique commit
            var commitGroups = parsedFiles
                .GroupBy(f => new { f.RepositoryName, f.BranchName, f.CommitSha })
                .ToList();

            var commits = new List<Commit>();
            foreach (var g in commitGroups)
            {
                var commitMetadata = await _gitTracker.GetCommitMetadataAsync(
                    g.Key.RepositoryName,
                    g.Key.CommitSha,
                    cancellationToken);

                commits.Add(new Commit
                {
                    Id = Guid.NewGuid().ToString(),
                    RepoId = g.Key.RepositoryName,
                    Sha = g.Key.CommitSha,
                    BranchName = g.Key.BranchName,
                    AuthorName = commitMetadata?.AuthorName ?? "Unknown",
                    AuthorEmail = commitMetadata?.AuthorEmail ?? "unknown@example.com",
                    CommitMessage = commitMetadata?.CommitMessage ?? "Indexed commit",
                    CommittedAt = commitMetadata?.CommittedAt ?? DateTimeOffset.UtcNow,
                    IndexedAt = DateTimeOffset.UtcNow
                });
            }

            if (commits.Any())
            {
                await _persistenceService.CreateCommitsBatchAsync(connection, transaction, commits, cancellationToken);
                _logger.LogInformation("Persisted {Count} commits", commits.Count);
            }

            // Step 3c: Persist files
            var files = parsedFiles.Select(f => new FileMetadata
            {
                Id = Guid.NewGuid().ToString(),
                RepoId = f.RepositoryName,
                BranchName = f.BranchName,
                CommitSha = f.CommitSha,
                FilePath = f.FilePath,
                Language = f.Language,
                SizeBytes = f.SourceText != null ? System.Text.Encoding.UTF8.GetByteCount(f.SourceText) : 0,
                LineCount = f.SourceText != null ? CalculateLineCount(f.SourceText) : 0,
                IndexedAt = DateTimeOffset.UtcNow
            }).ToList();

            if (files.Any())
            {
                await _persistenceService.CreateFilesBatchAsync(connection, transaction, files, cancellationToken);
                _logger.LogInformation("Persisted {Count} files", files.Count);
            }

            // Step 3d: Persist symbols
            var allSymbols = parsedFiles.SelectMany(f => f.Symbols).ToList();
            if (allSymbols.Any())
            {
                await _persistenceService.CreateSymbolsBatchAsync(connection, transaction, allSymbols, cancellationToken);
                _logger.LogInformation("Persisted {Count} symbols", allSymbols.Count);
            }

            // Step 3e: Resolve cross-file edges
            var allEdges = parsedFiles.SelectMany(f => f.Edges).ToList();
            if (allEdges.Any())
            {
                _logger.LogInformation("Resolving cross-file edges for {Count} edges...", allEdges.Count);
                var (resolvedEdges, resolvedCount) = await _edgeResolutionService.ResolveCrossFileEdgesAsync(connection, transaction, allEdges, allSymbols, cancellationToken);
                _logger.LogInformation("Resolved {ResolvedCount} cross-file edges out of {TotalCount}", resolvedCount, allEdges.Count);

                await _persistenceService.CreateEdgesBatchAsync(connection, transaction, resolvedEdges, cancellationToken);
                _logger.LogInformation("Persisted {Count} edges", resolvedEdges.Count);
            }

            // Step 3f: Persist chunks
            if (allChunks.Any())
            {
                await _persistenceService.CreateChunksBatchAsync(connection, transaction, allChunks, cancellationToken);
                _logger.LogInformation("Persisted {Count} chunks", allChunks.Count);
            }

            // Step 3g: Persist embeddings
            if (embeddings.Any())
            {
                await _persistenceService.CreateEmbeddingsBatchAsync(connection, transaction, embeddings, cancellationToken);
                _logger.LogInformation("Persisted {Count} embeddings", embeddings.Count);
            }

            // Commit the transaction
            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Storage persistence complete (transaction committed)");
        }
        catch (Exception ex)
        {
            // Rollback the transaction on error
            _logger.LogError(ex, "Failed to persist data to storage, rolling back transaction");
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Finds the compilation for a file by matching it to a project in the workspace.
    /// </summary>
    private Microsoft.CodeAnalysis.Compilation? FindCompilationForFile(WorkspaceCache workspaceCache, string filePath)
    {
        // Combine the repository path with the relative file path to get the absolute path
        var absoluteFilePath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.Combine(workspaceCache.RepositoryPath, filePath);
        var normalizedFilePath = Path.GetFullPath(absoluteFilePath);

        // Try to find a project that contains this file
        foreach (var projectCache in workspaceCache.Projects.Values)
        {
            var project = projectCache.Project;

            // Check if any document in the project matches this file path
            foreach (var document in project.Documents)
            {
                if (document.FilePath != null)
                {
                    var normalizedDocPath = Path.GetFullPath(document.FilePath);
                    if (normalizedDocPath.Equals(normalizedFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug(
                            "Found compilation for {FilePath} in project {ProjectName}",
                            filePath,
                            project.Name);
                        return projectCache.Compilation;
                    }
                }
            }
        }

        // If no exact match, try to find a project by directory proximity
        foreach (var projectCache in workspaceCache.Projects.Values)
        {
            var project = projectCache.Project;
            if (project.FilePath != null)
            {
                var projectDir = Path.GetDirectoryName(project.FilePath);
                if (projectDir != null && normalizedFilePath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Using compilation from nearby project {ProjectName} for {FilePath}",
                        project.Name,
                        filePath);
                    return projectCache.Compilation;
                }
            }
        }

        // Fallback: use the first available compilation
        if (workspaceCache.Projects.Count > 0)
        {
            var firstProject = workspaceCache.Projects.Values.First();
            _logger.LogDebug(
                "Using first available compilation from project {ProjectName} for {FilePath}",
                firstProject.Project.Name,
                filePath);
            return firstProject.Compilation;
        }

        return null;
    }

    /// <summary>
    /// Calculates the number of lines in a file. Returns 0 for empty files.
    /// </summary>
    private static int CalculateLineCount(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        var lineCount = 1; // Non-empty files have at least one line
        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                lineCount++;
            }
        }

        return lineCount;
    }

    /// <summary>
    /// Cleans up checkout locks that haven't been accessed in the specified time period.
    /// This prevents memory leaks from accumulating locks for branches that are no longer indexed.
    /// </summary>
    /// <param name="olderThan">Remove locks not accessed within this timespan (default: 7 days)</param>
    /// <returns>Number of locks removed</returns>
    public static int CleanupUnusedCheckoutLocks(TimeSpan? olderThan = null)
    {
        var threshold = olderThan ?? TimeSpan.FromDays(7);
        var cutoff = DateTime.UtcNow - threshold;
        var removed = 0;

        foreach (var kvp in _lockLastAccess.ToArray())
        {
            if (kvp.Value < cutoff)
            {
                // Remove from both dictionaries
                if (_lockLastAccess.TryRemove(kvp.Key, out _) &&
                    _checkoutLocks.TryRemove(kvp.Key, out var lockObj))
                {
                    lockObj.Dispose();
                    removed++;
                }
            }
        }

        return removed;
    }
}

public sealed class IndexingResult
{
    /// <summary>
    /// Successfully parsed files.
    /// </summary>
    public List<ParsedFile> ParsedFiles { get; init; } = new();

    /// <summary>
    /// Files that were deleted.
    /// </summary>
    public List<string> DeletedFiles { get; init; } = new();

    /// <summary>
    /// Number of files skipped (wrong language, too large, etc.).
    /// </summary>
    public int SkippedFiles { get; set; }

    /// <summary>
    /// Number of files that failed to parse.
    /// </summary>
    public int FailedFiles { get; set; }

    /// <summary>
    /// Total number of symbols extracted.
    /// </summary>
    public int TotalSymbols { get; set; }

    /// <summary>
    /// Total number of edges extracted.
    /// </summary>
    public int TotalEdges { get; set; }
}
