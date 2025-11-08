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
    private readonly WorkspaceLoader _workspaceLoader;
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
        WorkspaceLoader workspaceLoader,
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
        _workspaceLoader = workspaceLoader;
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
                // Ensure the branch is checked out BEFORE loading workspace
                // This is synchronized through GitTrackerService to prevent concurrent checkout operations
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

            // Parse all files in this group with the correct workspace
            foreach (var fileChange in group)
            {
                // Handle deleted files by removing their data from the database
                if (fileChange.ChangeType == ChangeType.Deleted)
                {
                    result.DeletedFiles.Add(fileChange.FilePath);
                    await DeleteFileDataAsync(fileChange.RepositoryName, fileChange.BranchName, fileChange.FilePath, cancellationToken);
                    _logger.LogInformation("Deleted indexed data for removed file: {FilePath}", fileChange.FilePath);
                    continue;
                }

                allTasks.Add(IndexFileAsync(fileChange, workspaceCache, cancellationToken));
            }
        }

        var parsedFiles = await Task.WhenAll(allTasks);

        // Release workspace handles now that parsing is complete
        // This decrements reference counts and allows disposal if workspaces were marked for disposal
        foreach (var handle in workspaceHandles)
        {
            handle.Dispose();
        }

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

            // Cache the source text to avoid re-reading from Git during chunking
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

            // Step 3e: Resolve cross-file edges
            var allEdges = parsedFiles.SelectMany(f => f.Edges).ToList();
            if (allEdges.Any())
            {
                _logger.LogInformation("Resolving cross-file edges for {Count} edges...", allEdges.Count);
                var (resolvedEdges, resolvedCount) = await ResolveCrossFileEdgesAsync(connection, transaction, allEdges, allSymbols, cancellationToken);
                _logger.LogInformation("Resolved {ResolvedCount} cross-file edges out of {TotalCount}", resolvedCount, allEdges.Count);

                await CreateEdgesBatchTransactionalAsync(connection, transaction, resolvedEdges, cancellationToken);
                _logger.LogInformation("Persisted {Count} edges", resolvedEdges.Count);
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

    /// <summary>
    /// Normalizes a qualified name for matching by:
    /// 1. Removing generic type arguments (e.g., "Method<int>" -> "Method<>")
    /// 2. Removing parameter lists (e.g., "Method(int, string)" -> "Method")
    /// 3. Extracting the class.method portion (e.g., "Namespace.Class.Method" -> "Class.Method")
    /// This allows matching partial qualified names from fallback parsing to full qualified names.
    /// </summary>
    private string NormalizeQualifiedName(string qualifiedName)
    {
        // Step 1: Replace generic type arguments with empty brackets
        // E.g., "QueryAsync<CodeChunk>" -> "QueryAsync<>"
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            qualifiedName,
            @"<[^>]+>",
            "<>");

        // Step 2: Remove parameter lists
        // E.g., "QueryAsync<>(string, object?, CancellationToken)" -> "QueryAsync<>"
        var parenIndex = normalized.IndexOf('(');
        if (parenIndex >= 0)
        {
            normalized = normalized.Substring(0, parenIndex);
        }

        // Step 3: Extract the last two parts (Class.Method)
        // E.g., "LancerMcp.Services.DatabaseService.QueryAsync<>" -> "DatabaseService.QueryAsync<>"
        // This allows matching "DatabaseService.QueryAsync" from fallback to full qualified names
        var parts = normalized.Split('.');
        if (parts.Length >= 2)
        {
            normalized = string.Join(".", parts.Skip(parts.Length - 2));
        }

        return normalized;
    }

    /// <summary>
    /// Resolves cross-file edges by looking up target symbols in the database.
    /// Edges with qualified name strings as targets are resolved to actual symbol IDs.
    /// Returns a tuple of (resolved edges, count of resolved edges).
    /// </summary>
    private async Task<(List<SymbolEdge> edges, int resolvedCount)> ResolveCrossFileEdgesAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<SymbolEdge> edges,
        List<Symbol> currentBatchSymbols,
        CancellationToken cancellationToken)
    {
        var resolvedEdges = new List<SymbolEdge>();
        var resolvedCount = 0;

        // Build a lookup of symbols from the current batch
        // We use normalized qualified names to match generic methods with concrete type arguments
        // Note: Multiple symbols can have the same qualified name (e.g., namespaces in different files)
        // We group by qualified name and take the first symbol ID for each
        var currentBatchLookup = currentBatchSymbols
            .Where(s => !string.IsNullOrEmpty(s.QualifiedName))
            .GroupBy(s => NormalizeQualifiedName(s.QualifiedName!))
            .ToDictionary(g => g.Key, g => g.First().Id);

        // Also query existing symbols from the database for cross-file resolution
        var repositoryName = edges.FirstOrDefault()?.RepositoryName;
        var branchName = edges.FirstOrDefault()?.BranchName;

        Dictionary<string, string> databaseLookup = new();
        if (!string.IsNullOrEmpty(repositoryName) && !string.IsNullOrEmpty(branchName))
        {
            var sql = @"
                SELECT id, qualified_name
                FROM symbols
                WHERE repo_id = @RepoId
                  AND branch_name = @BranchName
                  AND qualified_name IS NOT NULL";

            var command = new Dapper.CommandDefinition(
                sql,
                new { RepoId = repositoryName, BranchName = branchName },
                transaction,
                cancellationToken: cancellationToken);

            var dbSymbols = await connection.QueryAsync<(string id, string qualified_name)>(command);
            // Note: Multiple symbols can have the same qualified name (e.g., namespaces in different files)
            // We group by qualified name and take the first symbol ID for each
            databaseLookup = dbSymbols
                .GroupBy(s => NormalizeQualifiedName(s.qualified_name))
                .ToDictionary(g => g.Key, g => g.First().id);
        }

        foreach (var edge in edges)
        {
            var targetId = edge.TargetSymbolId;
            var wasResolved = false;

            // Check if target is a GUID (already resolved)
            if (!Guid.TryParse(targetId, out _))
            {
                // Target is a qualified name string, try to resolve it
                var normalizedTarget = NormalizeQualifiedName(targetId);

                // First check current batch
                if (currentBatchLookup.TryGetValue(normalizedTarget, out var resolvedId))
                {
                    targetId = resolvedId;
                    wasResolved = true;
                    _logger.LogDebug("Resolved edge target '{OriginalTarget}' -> '{NormalizedTarget}' to symbol ID {SymbolId} (current batch)",
                        edge.TargetSymbolId, normalizedTarget, resolvedId);
                }
                // Then check database
                else if (databaseLookup.TryGetValue(normalizedTarget, out resolvedId))
                {
                    targetId = resolvedId;
                    wasResolved = true;
                    _logger.LogDebug("Resolved edge target '{OriginalTarget}' -> '{NormalizedTarget}' to symbol ID {SymbolId} (database)",
                        edge.TargetSymbolId, normalizedTarget, resolvedId);
                }
                // If still not resolved, skip this edge (external reference)
                else
                {
                    _logger.LogDebug("Could not resolve edge target '{OriginalTarget}' -> '{NormalizedTarget}' (external reference or missing symbol)",
                        edge.TargetSymbolId, normalizedTarget);
                    // Skip edges to external symbols (framework types, etc.)
                    continue;
                }
            }

            if (wasResolved)
            {
                resolvedCount++;
            }

            resolvedEdges.Add(new SymbolEdge
            {
                Id = edge.Id,
                SourceSymbolId = edge.SourceSymbolId,
                TargetSymbolId = targetId,
                Kind = edge.Kind,
                RepositoryName = edge.RepositoryName,
                BranchName = edge.BranchName,
                CommitSha = edge.CommitSha,
                IndexedAt = edge.IndexedAt
            });
        }

        return (resolvedEdges, resolvedCount);
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

