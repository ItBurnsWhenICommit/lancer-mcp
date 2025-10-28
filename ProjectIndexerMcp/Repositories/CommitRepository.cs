using ProjectIndexerMcp.Models;
using ProjectIndexerMcp.Services;

namespace ProjectIndexerMcp.Repositories;

/// <summary>
/// Repository implementation for managing commits in the database.
/// </summary>
public sealed class CommitRepository : ICommitRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<CommitRepository> _logger;

    public CommitRepository(DatabaseService db, ILogger<CommitRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Commit?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, sha, branch_name AS BranchName,
                   author_name AS AuthorName, author_email AS AuthorEmail,
                   commit_message AS CommitMessage, committed_at AS CommittedAt,
                   indexed_at AS IndexedAt
            FROM commits
            WHERE id = @Id";

        return await _db.QueryFirstOrDefaultAsync<Commit>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<Commit?> GetByShaAsync(string repoId, string sha, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, sha, branch_name AS BranchName,
                   author_name AS AuthorName, author_email AS AuthorEmail,
                   commit_message AS CommitMessage, committed_at AS CommittedAt,
                   indexed_at AS IndexedAt
            FROM commits
            WHERE repo_id = @RepoId AND sha = @Sha
            LIMIT 1";

        return await _db.QueryFirstOrDefaultAsync<Commit>(sql, new { RepoId = repoId, Sha = sha }, cancellationToken);
    }

    public async Task<IEnumerable<Commit>> GetByRepoIdAsync(string repoId, int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, sha, branch_name AS BranchName,
                   author_name AS AuthorName, author_email AS AuthorEmail,
                   commit_message AS CommitMessage, committed_at AS CommittedAt,
                   indexed_at AS IndexedAt
            FROM commits
            WHERE repo_id = @RepoId
            ORDER BY committed_at DESC
            LIMIT @Limit";

        return await _db.QueryAsync<Commit>(sql, new { RepoId = repoId, Limit = limit }, cancellationToken);
    }

    public async Task<IEnumerable<Commit>> GetByBranchAsync(string repoId, string branchName, int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, sha, branch_name AS BranchName,
                   author_name AS AuthorName, author_email AS AuthorEmail,
                   commit_message AS CommitMessage, committed_at AS CommittedAt,
                   indexed_at AS IndexedAt
            FROM commits
            WHERE repo_id = @RepoId AND branch_name = @BranchName
            ORDER BY committed_at DESC
            LIMIT @Limit";

        return await _db.QueryAsync<Commit>(sql, new { RepoId = repoId, BranchName = branchName, Limit = limit }, cancellationToken);
    }

    public async Task<Commit> CreateAsync(Commit commit, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO commits (id, repo_id, sha, branch_name, author_name, author_email,
                                 commit_message, committed_at, indexed_at)
            VALUES (@Id, @RepoId, @Sha, @BranchName, @AuthorName, @AuthorEmail,
                    @CommitMessage, @CommittedAt, @IndexedAt)
            ON CONFLICT (repo_id, sha, branch_name) DO UPDATE
            SET indexed_at = EXCLUDED.indexed_at
            RETURNING id, repo_id AS RepoId, sha, branch_name AS BranchName,
                      author_name AS AuthorName, author_email AS AuthorEmail,
                      commit_message AS CommitMessage, committed_at AS CommittedAt,
                      indexed_at AS IndexedAt";

        return await _db.QuerySingleAsync<Commit>(sql, new
        {
            commit.Id,
            commit.RepoId,
            commit.Sha,
            commit.BranchName,
            commit.AuthorName,
            commit.AuthorEmail,
            commit.CommitMessage,
            commit.CommittedAt,
            commit.IndexedAt
        }, cancellationToken);
    }

    public async Task<int> CreateBatchAsync(IEnumerable<Commit> commits, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO commits (id, repo_id, sha, branch_name, author_name, author_email,
                                 commit_message, committed_at, indexed_at)
            VALUES (@Id, @RepoId, @Sha, @BranchName, @AuthorName, @AuthorEmail,
                    @CommitMessage, @CommittedAt, @IndexedAt)
            ON CONFLICT (repo_id, sha, branch_name) DO NOTHING";

        var commitsList = commits.ToList();
        var rowsAffected = await _db.ExecuteAsync(sql, commitsList, cancellationToken);

        _logger.LogInformation("Inserted {Count} commits", rowsAffected);
        return rowsAffected;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM commits WHERE id = @Id";
        var rowsAffected = await _db.ExecuteAsync(sql, new { Id = id }, cancellationToken);
        return rowsAffected > 0;
    }

    public async Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM commits WHERE repo_id = @RepoId";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId }, cancellationToken);

        _logger.LogInformation("Deleted {Count} commits for repo {RepoId}", rowsAffected, repoId);
        return rowsAffected;
    }
}

