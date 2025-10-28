using ProjectIndexerMcp.Models;
using ProjectIndexerMcp.Services;

namespace ProjectIndexerMcp.Repositories;

/// <summary>
/// Repository implementation for managing code chunks in the database.
/// </summary>
public sealed class CodeChunkRepository : ICodeChunkRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<CodeChunkRepository> _logger;

    public CodeChunkRepository(DatabaseService db, ILogger<CodeChunkRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, symbol_id AS SymbolId, symbol_name AS SymbolName,
                   symbol_kind AS SymbolKind, language, content, start_line AS StartLine,
                   end_line AS EndLine, chunk_start_line AS ChunkStartLine, chunk_end_line AS ChunkEndLine,
                   token_count AS TokenCount, parent_symbol_name AS ParentSymbolName, signature,
                   documentation, created_at AS CreatedAt
            FROM code_chunks
            WHERE id = @Id";

        return await _db.QueryFirstOrDefaultAsync<CodeChunk>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<IEnumerable<CodeChunk>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, symbol_id AS SymbolId, symbol_name AS SymbolName,
                   symbol_kind AS SymbolKind, language, content, start_line AS StartLine,
                   end_line AS EndLine, chunk_start_line AS ChunkStartLine, chunk_end_line AS ChunkEndLine,
                   token_count AS TokenCount, parent_symbol_name AS ParentSymbolName, signature,
                   documentation, created_at AS CreatedAt
            FROM code_chunks
            WHERE repo_id = @RepoId AND branch_name = @BranchName AND file_path = @FilePath
            ORDER BY chunk_start_line";

        return await _db.QueryAsync<CodeChunk>(sql, new { RepoId = repoId, BranchName = branchName, FilePath = filePath }, cancellationToken);
    }

    public async Task<IEnumerable<CodeChunk>> GetBySymbolAsync(string symbolId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, symbol_id AS SymbolId, symbol_name AS SymbolName,
                   symbol_kind AS SymbolKind, language, content, start_line AS StartLine,
                   end_line AS EndLine, chunk_start_line AS ChunkStartLine, chunk_end_line AS ChunkEndLine,
                   token_count AS TokenCount, parent_symbol_name AS ParentSymbolName, signature,
                   documentation, created_at AS CreatedAt
            FROM code_chunks
            WHERE symbol_id = @SymbolId";

        return await _db.QueryAsync<CodeChunk>(sql, new { SymbolId = symbolId }, cancellationToken);
    }

    public async Task<IEnumerable<CodeChunk>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, symbol_id AS SymbolId, symbol_name AS SymbolName,
                   symbol_kind AS SymbolKind, language, content, start_line AS StartLine,
                   end_line AS EndLine, chunk_start_line AS ChunkStartLine, chunk_end_line AS ChunkEndLine,
                   token_count AS TokenCount, parent_symbol_name AS ParentSymbolName, signature,
                   documentation, created_at AS CreatedAt
            FROM code_chunks
            WHERE repo_id = @RepoId AND branch_name = @BranchName
            ORDER BY file_path, chunk_start_line
            LIMIT @Limit";

        return await _db.QueryAsync<CodeChunk>(sql, new { RepoId = repoId, BranchName = branchName, Limit = limit }, cancellationToken);
    }

    public async Task<IEnumerable<CodeChunk>> GetByLanguageAsync(string repoId, Language language, int limit = 1000, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, symbol_id AS SymbolId, symbol_name AS SymbolName,
                   symbol_kind AS SymbolKind, language, content, start_line AS StartLine,
                   end_line AS EndLine, chunk_start_line AS ChunkStartLine, chunk_end_line AS ChunkEndLine,
                   token_count AS TokenCount, parent_symbol_name AS ParentSymbolName, signature,
                   documentation, created_at AS CreatedAt
            FROM code_chunks
            WHERE repo_id = @RepoId AND language = @Language::language
            ORDER BY file_path, chunk_start_line
            LIMIT @Limit";

        return await _db.QueryAsync<CodeChunk>(sql, new { RepoId = repoId, Language = language.ToString(), Limit = limit }, cancellationToken);
    }

    public async Task<IEnumerable<CodeChunk>> SearchFullTextAsync(string repoId, string query, string? branchName = null, Language? language = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        var sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, symbol_id AS SymbolId, symbol_name AS SymbolName,
                   symbol_kind AS SymbolKind, language, content, start_line AS StartLine,
                   end_line AS EndLine, chunk_start_line AS ChunkStartLine, chunk_end_line AS ChunkEndLine,
                   token_count AS TokenCount, parent_symbol_name AS ParentSymbolName, signature,
                   documentation, created_at AS CreatedAt,
                   ts_rank(content_tsv, websearch_to_tsquery('english', @Query)) AS rank
            FROM code_chunks
            WHERE repo_id = @RepoId
                  AND content_tsv @@ websearch_to_tsquery('english', @Query)";

        if (!string.IsNullOrEmpty(branchName))
        {
            sql += " AND branch_name = @BranchName";
        }

        if (language.HasValue)
        {
            sql += " AND language = @Language::language";
        }

        sql += @"
            ORDER BY rank DESC
            LIMIT @Limit";

        return await _db.QueryAsync<CodeChunk>(sql, new
        {
            RepoId = repoId,
            Query = query,
            BranchName = branchName,
            Language = language?.ToString(),
            Limit = limit
        }, cancellationToken);
    }

    public async Task<CodeChunk> CreateAsync(CodeChunk chunk, CancellationToken cancellationToken = default)
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
            RETURNING id";

        await _db.ExecuteAsync(sql, new
        {
            chunk.Id,
            chunk.RepositoryName,
            chunk.BranchName,
            chunk.CommitSha,
            chunk.FilePath,
            chunk.SymbolId,
            chunk.SymbolName,
            SymbolKind = chunk.SymbolKind?.ToString(),
            Language = chunk.Language.ToString(),
            chunk.Content,
            chunk.StartLine,
            chunk.EndLine,
            chunk.ChunkStartLine,
            chunk.ChunkEndLine,
            chunk.TokenCount,
            chunk.ParentSymbolName,
            chunk.Signature,
            chunk.Documentation,
            chunk.CreatedAt
        }, cancellationToken);

        return chunk;
    }

    public async Task<int> CreateBatchAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO code_chunks (id, repo_id, branch_name, commit_sha, file_path, symbol_id,
                                     symbol_name, symbol_kind, language, content, start_line, end_line,
                                     chunk_start_line, chunk_end_line, token_count, parent_symbol_name,
                                     signature, documentation, created_at)
            VALUES (@Id, @RepositoryName, @BranchName, @CommitSha, @FilePath, @SymbolId,
                    @SymbolName, @SymbolKind::symbol_kind, @Language::language, @Content, @StartLine, @EndLine,
                    @ChunkStartLine, @ChunkEndLine, @TokenCount, @ParentSymbolName,
                    @Signature, @Documentation, @CreatedAt)";

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

        var rowsAffected = await _db.ExecuteAsync(sql, chunksList, cancellationToken);
        _logger.LogInformation("Inserted {Count} code chunks", rowsAffected);
        return rowsAffected;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM code_chunks WHERE id = @Id";
        var rowsAffected = await _db.ExecuteAsync(sql, new { Id = id }, cancellationToken);
        return rowsAffected > 0;
    }

    public async Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM code_chunks WHERE repo_id = @RepoId";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId }, cancellationToken);
        _logger.LogInformation("Deleted {Count} code chunks for repo {RepoId}", rowsAffected, repoId);
        return rowsAffected;
    }

    public async Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM code_chunks WHERE repo_id = @RepoId AND branch_name = @BranchName";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
        _logger.LogInformation("Deleted {Count} code chunks for branch {BranchName}", rowsAffected, branchName);
        return rowsAffected;
    }

    public async Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM code_chunks WHERE repo_id = @RepoId";
        return await _db.ExecuteScalarAsync<int>(sql, new { RepoId = repoId }, cancellationToken) ?? 0;
    }
}

