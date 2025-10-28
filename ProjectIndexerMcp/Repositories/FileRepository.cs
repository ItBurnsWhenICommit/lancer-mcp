using ProjectIndexerMcp.Models;
using ProjectIndexerMcp.Services;

namespace ProjectIndexerMcp.Repositories;

/// <summary>
/// Repository implementation for managing files in the database.
/// </summary>
public sealed class FileRepository : IFileRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<FileRepository> _logger;

    public FileRepository(DatabaseService db, ILogger<FileRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<FileMetadata?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, language, size_bytes AS SizeBytes,
                   line_count AS LineCount, indexed_at AS IndexedAt
            FROM files
            WHERE id = @Id";

        return await _db.QueryFirstOrDefaultAsync<FileMetadata>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<FileMetadata?> GetByPathAsync(string repoId, string branchName, string commitSha, string filePath, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, language, size_bytes AS SizeBytes,
                   line_count AS LineCount, indexed_at AS IndexedAt
            FROM files
            WHERE repo_id = @RepoId AND branch_name = @BranchName
                  AND commit_sha = @CommitSha AND file_path = @FilePath
            LIMIT 1";

        return await _db.QueryFirstOrDefaultAsync<FileMetadata>(sql,
            new { RepoId = repoId, BranchName = branchName, CommitSha = commitSha, FilePath = filePath },
            cancellationToken);
    }

    public async Task<IEnumerable<FileMetadata>> GetByCommitAsync(string repoId, string commitSha, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, language, size_bytes AS SizeBytes,
                   line_count AS LineCount, indexed_at AS IndexedAt
            FROM files
            WHERE repo_id = @RepoId AND commit_sha = @CommitSha
            ORDER BY file_path";

        return await _db.QueryAsync<FileMetadata>(sql, new { RepoId = repoId, CommitSha = commitSha }, cancellationToken);
    }

    public async Task<IEnumerable<FileMetadata>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, language, size_bytes AS SizeBytes,
                   line_count AS LineCount, indexed_at AS IndexedAt
            FROM files
            WHERE repo_id = @RepoId AND branch_name = @BranchName
            ORDER BY file_path";

        return await _db.QueryAsync<FileMetadata>(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
    }

    public async Task<IEnumerable<FileMetadata>> GetByLanguageAsync(string repoId, Language language, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, language, size_bytes AS SizeBytes,
                   line_count AS LineCount, indexed_at AS IndexedAt
            FROM files
            WHERE repo_id = @RepoId AND language = @Language::language
            ORDER BY file_path";

        return await _db.QueryAsync<FileMetadata>(sql, new { RepoId = repoId, Language = language.ToString() }, cancellationToken);
    }

    public async Task<FileMetadata> CreateAsync(FileMetadata file, CancellationToken cancellationToken = default)
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
                indexed_at = EXCLUDED.indexed_at
            RETURNING id, repo_id AS RepoId, branch_name AS BranchName, commit_sha AS CommitSha,
                      file_path AS FilePath, language, size_bytes AS SizeBytes,
                      line_count AS LineCount, indexed_at AS IndexedAt";

        return await _db.QuerySingleAsync<FileMetadata>(sql, new
        {
            file.Id,
            file.RepoId,
            file.BranchName,
            file.CommitSha,
            file.FilePath,
            Language = file.Language.ToString(),
            file.SizeBytes,
            file.LineCount,
            file.IndexedAt
        }, cancellationToken);
    }

    public async Task<int> CreateBatchAsync(IEnumerable<FileMetadata> files, CancellationToken cancellationToken = default)
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

        var rowsAffected = await _db.ExecuteAsync(sql, filesList, cancellationToken);
        _logger.LogInformation("Inserted/updated {Count} files", rowsAffected);
        return rowsAffected;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM files WHERE id = @Id";
        var rowsAffected = await _db.ExecuteAsync(sql, new { Id = id }, cancellationToken);
        return rowsAffected > 0;
    }

    public async Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM files WHERE repo_id = @RepoId";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId }, cancellationToken);
        _logger.LogInformation("Deleted {Count} files for repo {RepoId}", rowsAffected, repoId);
        return rowsAffected;
    }

    public async Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM files WHERE repo_id = @RepoId AND branch_name = @BranchName";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
        _logger.LogInformation("Deleted {Count} files for branch {BranchName}", rowsAffected, branchName);
        return rowsAffected;
    }
}

