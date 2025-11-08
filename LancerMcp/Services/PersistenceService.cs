using Dapper;
using Pgvector;
using LancerMcp.Models;
using LancerMcp.Repositories;

namespace LancerMcp.Services;

/// <summary>
/// Handles all database persistence operations for the indexing pipeline.
/// Provides transactional batch operations for commits, files, symbols, edges, chunks, and embeddings.
/// </summary>
public sealed class PersistenceService
{
    private readonly ILogger<PersistenceService> _logger;
    private readonly IFileRepository _fileRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ICodeChunkRepository _chunkRepository;

    public PersistenceService(
        ILogger<PersistenceService> logger,
        IFileRepository fileRepository,
        ISymbolRepository symbolRepository,
        ICodeChunkRepository chunkRepository)
    {
        _logger = logger;
        _fileRepository = fileRepository;
        _symbolRepository = symbolRepository;
        _chunkRepository = chunkRepository;
    }

    /// <summary>
    /// Deletes all data for a specific file (symbols, chunks, embeddings, file metadata).
    /// Supports both transactional and non-transactional operations.
    /// </summary>
    public async Task DeleteFileDataAsync(
        string repositoryName,
        string branchName,
        string filePath,
        CancellationToken cancellationToken,
        Npgsql.NpgsqlConnection? connection = null,
        Npgsql.NpgsqlTransaction? transaction = null)
    {
        _logger.LogDebug("Deleting old data for file {FilePath} in {Repo}/{Branch}", filePath, repositoryName, branchName);

        // If connection and transaction are provided, use raw SQL for transactional operations
        if (connection != null && transaction != null)
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
        else
        {
            // Use repository methods for non-transactional operations
            // Delete symbols for this file (cascading deletes will handle edges via foreign keys)
            await _symbolRepository.DeleteByFileAsync(repositoryName, branchName, filePath, cancellationToken);

            // Delete code chunks for this file (cascading deletes will handle embeddings via foreign keys)
            await _chunkRepository.DeleteByFileAsync(repositoryName, branchName, filePath, cancellationToken);

            // Delete file metadata
            await _fileRepository.DeleteByFilePathAsync(repositoryName, branchName, filePath, cancellationToken);
        }
    }

    /// <summary>
    /// Persists commits in a batch within a transaction.
    /// </summary>
    public async Task CreateCommitsBatchAsync(
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

    /// <summary>
    /// Persists files in a batch within a transaction.
    /// </summary>
    public async Task CreateFilesBatchAsync(
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
            ON CONFLICT (repo_id, branch_name, file_path) DO UPDATE
            SET commit_sha = EXCLUDED.commit_sha,
                language = EXCLUDED.language,
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

    /// <summary>
    /// Persists symbols in a batch within a transaction.
    /// </summary>
    public async Task CreateSymbolsBatchAsync(
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

    /// <summary>
    /// Persists edges in a batch within a transaction.
    /// </summary>
    public async Task CreateEdgesBatchAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<SymbolEdge> edges,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO symbol_edges (id, source_symbol_id, target_symbol_id, kind, repo_id,
                                      branch_name, commit_sha, indexed_at)
            VALUES (@Id, @SourceSymbolId, @TargetSymbolId, @Kind::edge_kind, @RepositoryName,
                    @BranchName, @CommitSha, @IndexedAt)
            ON CONFLICT (source_symbol_id, target_symbol_id, kind) DO NOTHING";

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

    /// <summary>
    /// Persists code chunks in a batch within a transaction.
    /// </summary>
    public async Task CreateChunksBatchAsync(
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

    /// <summary>
    /// Persists embeddings in a batch within a transaction.
    /// </summary>
    public async Task CreateEmbeddingsBatchAsync(
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
            Vector = new Vector(e.Vector),
            e.Model,
            e.ModelVersion,
            e.GeneratedAt
        }).ToList();

        var command = new CommandDefinition(sql, embeddingsList, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }
}

