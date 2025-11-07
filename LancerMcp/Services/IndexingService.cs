using Dapper;
using Pgvector;
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
    private readonly DatabaseService _db;
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
        DatabaseService db,
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
        _db = db;
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
    /// All operations are wrapped in a single database transaction to ensure atomicity.
    /// If any step fails, the entire transaction is rolled back, preventing partial writes or data corruption.
    /// </summary>
    private async Task PersistToStorageAsync(List<ParsedFile> parsedFiles, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Persisting {Count} files to storage (transactional)...", parsedFiles.Count);

        // Step 0: Ensure repositories exist in the database (outside transaction)
        var repositoryNames = parsedFiles.Select(f => f.RepositoryName).Distinct().ToList();
        foreach (var repoName in repositoryNames)
        {
            await EnsureRepositoryExistsAsync(repoName, cancellationToken);
        }

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
                await DeleteFileDataTransactionalAsync(connection, transaction, file.RepositoryName, file.BranchName, file.FilePath, cancellationToken);
            }

            // Step 3b: Persist commits
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
                await CreateCommitsBatchTransactionalAsync(connection, transaction, commits, cancellationToken);
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
                SizeBytes = 0, // TODO: Get actual file size from Git
                LineCount = 0, // TODO: Get actual line count from Git
                IndexedAt = DateTimeOffset.UtcNow
            }).ToList();

            if (files.Any())
            {
                await CreateFilesBatchTransactionalAsync(connection, transaction, files, cancellationToken);
                _logger.LogInformation("Persisted {Count} files", files.Count);
            }

            // Step 3d: Persist symbols
            var allSymbols = parsedFiles.SelectMany(f => f.Symbols).ToList();
            if (allSymbols.Any())
            {
                await CreateSymbolsBatchTransactionalAsync(connection, transaction, allSymbols, cancellationToken);
                _logger.LogInformation("Persisted {Count} symbols", allSymbols.Count);
            }

            // Step 3e: Persist edges
            var allEdges = parsedFiles.SelectMany(f => f.Edges).ToList();
            if (allEdges.Any())
            {
                await CreateEdgesBatchTransactionalAsync(connection, transaction, allEdges, cancellationToken);
                _logger.LogInformation("Persisted {Count} edges", allEdges.Count);
            }

            // Step 3f: Persist chunks
            if (allChunks.Any())
            {
                await CreateChunksBatchTransactionalAsync(connection, transaction, allChunks, cancellationToken);
                _logger.LogInformation("Persisted {Count} chunks", allChunks.Count);
            }

            // Step 3g: Persist embeddings
            if (embeddings.Any())
            {
                await CreateEmbeddingsBatchTransactionalAsync(connection, transaction, embeddings, cancellationToken);
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

    // ===== Transactional Helper Methods =====
    // These methods execute database operations within a transaction to ensure atomicity.

    private async Task DeleteFileDataTransactionalAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        string repositoryName,
        string branchName,
        string filePath,
        CancellationToken cancellationToken)
    {
        var param = new { RepoId = repositoryName, BranchName = branchName, FilePath = filePath };

        // Delete symbols (cascading deletes handle edges)
        const string deleteSymbolsSql = "DELETE FROM symbols WHERE repo_id = @RepoId AND branch_name = @BranchName AND file_path = @FilePath";
        var deleteSymbolsCmd = new CommandDefinition(deleteSymbolsSql, param, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(deleteSymbolsCmd);

        // Delete chunks (cascading deletes handle embeddings)
        const string deleteChunksSql = "DELETE FROM code_chunks WHERE repo_id = @RepoId AND branch_name = @BranchName AND file_path = @FilePath";
        var deleteChunksCmd = new CommandDefinition(deleteChunksSql, param, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(deleteChunksCmd);

        // Delete file metadata
        const string deleteFileSql = "DELETE FROM files WHERE repo_id = @RepoId AND branch_name = @BranchName AND file_path = @FilePath";
        var deleteFileCmd = new CommandDefinition(deleteFileSql, param, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(deleteFileCmd);
    }

    private async Task CreateCommitsBatchTransactionalAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<Commit> commits,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO commits (id, repo_id, sha, branch_name, author_name, author_email,
                                 commit_message, committed_at, indexed_at)
            VALUES (@Id, @RepoId, @Sha, @BranchName, @AuthorName, @AuthorEmail,
                    @CommitMessage, @CommittedAt, @IndexedAt)
            ON CONFLICT (repo_id, sha, branch_name) DO NOTHING";

        var command = new CommandDefinition(sql, commits, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private async Task CreateFilesBatchTransactionalAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<FileMetadata> files,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO files (id, repo_id, branch_name, commit_sha, file_path, language,
                               size_bytes, line_count, indexed_at)
            VALUES (@Id, @RepoId, @BranchName, @CommitSha, @FilePath, @Language::language,
                    @SizeBytes, @LineCount, @IndexedAt)
            ON CONFLICT (repo_id, branch_name, commit_sha, file_path) DO UPDATE
            SET language = EXCLUDED.language,
                size_bytes = EXCLUDED.size_bytes,
                line_count = EXCLUDED.line_count,
                indexed_at = EXCLUDED.indexed_at";

        var filesList = files.Select(f => new
        {
            f.Id,
            f.RepoId,
            f.BranchName,
            f.CommitSha,
            f.FilePath,
            Language = f.Language.ToString(),
            f.SizeBytes,
            f.LineCount,
            f.IndexedAt
        }).ToList();

        var command = new CommandDefinition(sql, filesList, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private async Task CreateSymbolsBatchTransactionalAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<Symbol> symbols,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO symbols (id, repo_id, branch_name, commit_sha, file_path, name, qualified_name,
                                 kind, language, start_line, start_column, end_line, end_column,
                                 signature, documentation, modifiers, parent_symbol_id, indexed_at)
            VALUES (@Id, @RepositoryName, @BranchName, @CommitSha, @FilePath, @Name, @QualifiedName,
                    @Kind::symbol_kind, @Language::language, @StartLine, @StartColumn, @EndLine, @EndColumn,
                    @Signature, @Documentation, @Modifiers, @ParentSymbolId, @IndexedAt)
            ON CONFLICT (repo_id, branch_name, file_path, name, start_line, end_line) DO NOTHING";

        var symbolsList = symbols.Select(s => new
        {
            s.Id,
            s.RepositoryName,
            s.BranchName,
            s.CommitSha,
            s.FilePath,
            s.Name,
            s.QualifiedName,
            Kind = s.Kind.ToString(),
            Language = s.Language.ToString(),
            s.StartLine,
            s.StartColumn,
            s.EndLine,
            s.EndColumn,
            s.Signature,
            s.Documentation,
            s.Modifiers,
            s.ParentSymbolId,
            s.IndexedAt
        }).ToList();

        var command = new CommandDefinition(sql, symbolsList, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private async Task CreateEdgesBatchTransactionalAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<SymbolEdge> edges,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO edges (id, source_symbol_id, target_symbol_id, kind, repo_id, branch_name,
                               commit_sha, indexed_at)
            VALUES (@Id, @SourceSymbolId, @TargetSymbolId, @Kind::edge_kind, @RepositoryName,
                    @BranchName, @CommitSha, @IndexedAt)";

        var edgesList = edges.Select(e => new
        {
            e.Id,
            e.SourceSymbolId,
            e.TargetSymbolId,
            Kind = e.Kind.ToString(),
            e.RepositoryName,
            e.BranchName,
            e.CommitSha,
            e.IndexedAt
        }).ToList();

        var command = new CommandDefinition(sql, edgesList, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private async Task CreateChunksBatchTransactionalAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<CodeChunk> chunks,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO code_chunks (id, repo_id, branch_name, commit_sha, file_path, symbol_id,
                                     symbol_name, symbol_kind, language, content, start_line, end_line,
                                     chunk_start_line, chunk_end_line, token_count, parent_symbol_name,
                                     signature, documentation, created_at)
            VALUES (@Id, @RepositoryName, @BranchName, @CommitSha, @FilePath, @SymbolId,
                    @SymbolName, @SymbolKind::symbol_kind, @Language::language, @Content, @StartLine, @EndLine,
                    @ChunkStartLine, @ChunkEndLine, @TokenCount, @ParentSymbolName,
                    @Signature, @Documentation, @CreatedAt)
            ON CONFLICT (repo_id, branch_name, file_path, chunk_start_line, chunk_end_line) DO NOTHING";

        var chunksList = chunks.Select(c => new
        {
            c.Id,
            c.RepositoryName,
            c.BranchName,
            c.CommitSha,
            c.FilePath,
            c.SymbolId,
            c.SymbolName,
            SymbolKind = c.SymbolKind?.ToString(),
            Language = c.Language.ToString(),
            c.Content,
            c.StartLine,
            c.EndLine,
            c.ChunkStartLine,
            c.ChunkEndLine,
            c.TokenCount,
            c.ParentSymbolName,
            c.Signature,
            c.Documentation,
            c.CreatedAt
        }).ToList();

        var command = new CommandDefinition(sql, chunksList, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    private async Task CreateEmbeddingsBatchTransactionalAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<Embedding> embeddings,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO embeddings (id, chunk_id, repo_id, branch_name, commit_sha, vector,
                                    model, model_version, generated_at)
            VALUES (@Id, @ChunkId, @RepositoryName, @BranchName, @CommitSha, @Vector::vector,
                    @Model, @ModelVersion, @GeneratedAt)
            ON CONFLICT (chunk_id) DO UPDATE
            SET vector = EXCLUDED.vector,
                model = EXCLUDED.model,
                model_version = EXCLUDED.model_version,
                generated_at = EXCLUDED.generated_at";

        var embeddingsList = embeddings.Select(e => new
        {
            e.Id,
            e.ChunkId,
            e.RepositoryName,
            e.BranchName,
            e.CommitSha,
            Vector = new Vector(e.Vector).ToString(),
            e.Model,
            e.ModelVersion,
            e.GeneratedAt
        }).ToList();

        var command = new CommandDefinition(sql, embeddingsList, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
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

