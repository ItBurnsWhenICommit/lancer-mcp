using Microsoft.Extensions.Options;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;

namespace LancerMcp.Services;

/// <summary>
/// Orchestrates the indexing pipeline: language detection, parsing, symbol extraction, chunking, embedding, and storage.
/// </summary>
public sealed class IndexingService
{
    private readonly ILogger<IndexingService> _logger;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly GitTrackerService _gitTracker;
    private readonly LanguageDetectionService _languageDetection;
    private readonly RoslynParserService _roslynParser;
    private readonly BasicParserService _basicParser;
    private readonly ChunkingService _chunkingService;
    private readonly EmbeddingService _embeddingService;
    private readonly IRepositoryRepository _repositoryRepository;
    private readonly ICommitRepository _commitRepository;
    private readonly IFileRepository _fileRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IEdgeRepository _edgeRepository;
    private readonly ICodeChunkRepository _chunkRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public IndexingService(
        ILogger<IndexingService> logger,
        IOptionsMonitor<ServerOptions> options,
        GitTrackerService gitTracker,
        LanguageDetectionService languageDetection,
        RoslynParserService roslynParser,
        BasicParserService basicParser,
        ChunkingService chunkingService,
        EmbeddingService embeddingService,
        IRepositoryRepository repositoryRepository,
        ICommitRepository commitRepository,
        IFileRepository fileRepository,
        ISymbolRepository symbolRepository,
        IEdgeRepository edgeRepository,
        ICodeChunkRepository chunkRepository,
        IEmbeddingRepository embeddingRepository)
    {
        _logger = logger;
        _options = options;
        _gitTracker = gitTracker;
        _languageDetection = languageDetection;
        _roslynParser = roslynParser;
        _basicParser = basicParser;
        _chunkingService = chunkingService;
        _embeddingService = embeddingService;
        _repositoryRepository = repositoryRepository;
        _commitRepository = commitRepository;
        _fileRepository = fileRepository;
        _symbolRepository = symbolRepository;
        _edgeRepository = edgeRepository;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;

        var concurrency = options.CurrentValue.FileReadConcurrency;
        _concurrencyLimiter = new SemaphoreSlim(concurrency, concurrency);
    }

    /// <summary>
    /// Indexes a batch of file changes, persists to PostgreSQL, and automatically marks the branch as indexed.
    /// </summary>
    public async Task<IndexingResult> IndexFilesAsync(IEnumerable<FileChange> fileChanges, CancellationToken cancellationToken = default)
    {
        var result = new IndexingResult();
        var tasks = new List<Task<ParsedFile?>>();
        var fileChangesList = fileChanges.ToList();

        if (!fileChangesList.Any())
        {
            return result;
        }

        // Step 1: Parse all files and handle deletions
        foreach (var fileChange in fileChangesList)
        {
            // Handle deleted files by removing their data from the database
            if (fileChange.ChangeType == ChangeType.Deleted)
            {
                result.DeletedFiles.Add(fileChange.FilePath);
                await DeleteFileDataAsync(fileChange.RepositoryName, fileChange.BranchName, fileChange.FilePath, cancellationToken);
                _logger.LogInformation("Deleted indexed data for removed file: {FilePath}", fileChange.FilePath);
                continue;
            }

            tasks.Add(IndexFileAsync(fileChange, cancellationToken));
        }

        var parsedFiles = await Task.WhenAll(tasks);

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

        // Step 3: Automatically mark the branch as indexed after successful indexing
        var firstChange = fileChangesList.First();
        _gitTracker.MarkBranchAsIndexed(firstChange.RepositoryName, firstChange.BranchName);

        return result;
    }

    /// <summary>
    /// Indexes a single file.
    /// </summary>
    private async Task<ParsedFile?> IndexFileAsync(FileChange fileChange, CancellationToken cancellationToken)
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
                parsedFile = await _roslynParser.ParseFileAsync(
                    fileChange.RepositoryName,
                    fileChange.BranchName,
                    fileChange.CommitSha,
                    fileChange.FilePath,
                    content,
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
    /// </summary>
    /// <remarks>
    /// TODO: This method is not transactional. If any step fails after DeleteFileDataAsync,
    /// the old data is already deleted, leaving gaps until the next successful re-index.
    /// Future improvement: Refactor repositories to accept connection/transaction parameters
    /// and wrap the entire delete-insert workflow in a single database transaction.
    /// </remarks>
    private async Task PersistToStorageAsync(List<ParsedFile> parsedFiles, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Persisting {Count} files to storage...", parsedFiles.Count);

        try
        {
            // Step 0: Ensure repositories exist in the database
            var repositoryNames = parsedFiles.Select(f => f.RepositoryName).Distinct().ToList();
            foreach (var repoName in repositoryNames)
            {
                await EnsureRepositoryExistsAsync(repoName, cancellationToken);
            }

            // Step 0.5: Delete old symbols/edges/chunks for files being re-indexed
            // NOTE: This is not transactional - if insert fails, old data is already gone
            var filesToDelete = parsedFiles
                .Select(f => new { f.RepositoryName, f.BranchName, f.FilePath })
                .Distinct()
                .ToList();

            foreach (var file in filesToDelete)
            {
                await DeleteFileDataAsync(file.RepositoryName, file.BranchName, file.FilePath, cancellationToken);
            }

            // Step 1: Persist commits
            var commits = parsedFiles
                .GroupBy(f => new { f.RepositoryName, f.BranchName, f.CommitSha })
                .Select(g => new Commit
                {
                    Id = Guid.NewGuid().ToString(),
                    RepoId = g.First().RepositoryName,
                    Sha = g.Key.CommitSha,
                    BranchName = g.Key.BranchName,
                    AuthorName = "Unknown", // TODO: Get from Git
                    AuthorEmail = "unknown@example.com",
                    CommitMessage = "Indexed commit",
                    CommittedAt = DateTimeOffset.UtcNow,
                    IndexedAt = DateTimeOffset.UtcNow
                })
                .ToList();

            if (commits.Any())
            {
                await _commitRepository.CreateBatchAsync(commits, cancellationToken);
                _logger.LogInformation("Persisted {Count} commits", commits.Count);
            }

            // Step 2: Persist files (we don't have content size here, will be updated later if needed)
            var files = parsedFiles.Select(f => new FileMetadata
            {
                Id = Guid.NewGuid().ToString(),
                RepoId = f.RepositoryName,
                BranchName = f.BranchName,
                CommitSha = f.CommitSha,
                FilePath = f.FilePath,
                Language = f.Language,
                SizeBytes = 0, // TODO: Get actual file size from Git
                LineCount = 0, // TODO: Get actual line count from Git
                IndexedAt = DateTimeOffset.UtcNow
            }).ToList();

            if (files.Any())
            {
                await _fileRepository.CreateBatchAsync(files, cancellationToken);
                _logger.LogInformation("Persisted {Count} files", files.Count);
            }

            // Step 3: Persist symbols
            var allSymbols = parsedFiles.SelectMany(f => f.Symbols).ToList();
            if (allSymbols.Any())
            {
                await _symbolRepository.CreateBatchAsync(allSymbols, cancellationToken);
                _logger.LogInformation("Persisted {Count} symbols", allSymbols.Count);
            }

            // Step 4: Persist edges
            var allEdges = parsedFiles.SelectMany(f => f.Edges).ToList();
            if (allEdges.Any())
            {
                await _edgeRepository.CreateBatchAsync(allEdges, cancellationToken);
                _logger.LogInformation("Persisted {Count} edges", allEdges.Count);
            }

            // Step 5: Chunk symbols and persist chunks
            var allChunks = new List<CodeChunk>();
            foreach (var parsedFile in parsedFiles)
            {
                var chunkedFile = await _chunkingService.ChunkFileAsync(parsedFile, cancellationToken);
                allChunks.AddRange(chunkedFile.Chunks);
            }

            if (allChunks.Any())
            {
                await _chunkRepository.CreateBatchAsync(allChunks, cancellationToken);
                _logger.LogInformation("Persisted {Count} chunks", allChunks.Count);

                // Step 6: Generate and persist embeddings
                var embeddings = await _embeddingService.GenerateEmbeddingsAsync(allChunks, cancellationToken);
                await _embeddingRepository.CreateBatchAsync(embeddings, cancellationToken);
                _logger.LogInformation("Persisted {Count} embeddings", embeddings.Count);
            }

            _logger.LogInformation("Storage persistence complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist data to storage");
            throw;
        }
    }

    /// <summary>
    /// Ensures a repository exists in the database, creating it if necessary.
    /// </summary>
    private async Task EnsureRepositoryExistsAsync(string repositoryName, CancellationToken cancellationToken)
    {
        // Check if repository already exists
        var existingRepo = await _repositoryRepository.GetByNameAsync(repositoryName, cancellationToken);
        if (existingRepo != null)
        {
            return; // Repository already exists
        }

        // Get repository configuration from options
        var repoConfig = _options.CurrentValue.Repositories
            .FirstOrDefault(r => r.Name == repositoryName);

        if (repoConfig == null)
        {
            _logger.LogWarning("Repository {Name} not found in configuration, creating with minimal info", repositoryName);
            repoConfig = new ServerOptions.RepositoryDescriptor
            {
                Name = repositoryName,
                RemoteUrl = "unknown",
                DefaultBranch = "main"
            };
        }

        // Create repository record
        var repository = new Repository
        {
            Id = repositoryName, // Use repository name as ID for consistency
            Name = repositoryName,
            RemoteUrl = repoConfig.RemoteUrl,
            DefaultBranch = repoConfig.DefaultBranch,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _repositoryRepository.CreateAsync(repository, cancellationToken);
        _logger.LogInformation("Created repository record for {Name}", repositoryName);
    }

    /// <summary>
    /// Deletes all indexed data (symbols, edges, chunks, embeddings, file metadata) for a specific file.
    /// This prevents duplicate data when re-indexing the same file and cleans up deleted files.
    /// </summary>
    private async Task DeleteFileDataAsync(string repositoryName, string branchName, string filePath, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting old data for file {FilePath} in {Repo}/{Branch}", filePath, repositoryName, branchName);

        // Delete symbols for this file (cascading deletes will handle edges via foreign keys)
        await _symbolRepository.DeleteByFileAsync(repositoryName, branchName, filePath, cancellationToken);

        // Delete code chunks for this file (cascading deletes will handle embeddings via foreign keys)
        await _chunkRepository.DeleteByFileAsync(repositoryName, branchName, filePath, cancellationToken);

        // Delete file metadata
        await _fileRepository.DeleteByFilePathAsync(repositoryName, branchName, filePath, cancellationToken);
    }
}

/// <summary>
/// Result of an indexing operation.
/// </summary>
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

