using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Repositories;

/// <summary>
/// Repository implementation for managing branches in the database.
/// </summary>
public sealed class BranchRepository : IBranchRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<BranchRepository> _logger;

    public BranchRepository(DatabaseService db, ILogger<BranchRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Branch?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, name, head_commit_sha AS HeadCommitSha,
                   index_state AS IndexState, indexed_commit_sha AS IndexedCommitSha,
                   last_indexed_at AS LastIndexedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM branches
            WHERE id = @Id";

        return await _db.QueryFirstOrDefaultAsync<Branch>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<Branch?> GetByRepoAndNameAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, name, head_commit_sha AS HeadCommitSha,
                   index_state AS IndexState, indexed_commit_sha AS IndexedCommitSha,
                   last_indexed_at AS LastIndexedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM branches
            WHERE repo_id = @RepoId AND name = @BranchName";

        return await _db.QueryFirstOrDefaultAsync<Branch>(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
    }

    public async Task<IEnumerable<Branch>> GetByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, name, head_commit_sha AS HeadCommitSha,
                   index_state AS IndexState, indexed_commit_sha AS IndexedCommitSha,
                   last_indexed_at AS LastIndexedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM branches
            WHERE repo_id = @RepoId
            ORDER BY name";

        return await _db.QueryAsync<Branch>(sql, new { RepoId = repoId }, cancellationToken);
    }

    public async Task<IEnumerable<Branch>> GetByIndexStateAsync(IndexState state, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepoId, name, head_commit_sha AS HeadCommitSha,
                   index_state AS IndexState, indexed_commit_sha AS IndexedCommitSha,
                   last_indexed_at AS LastIndexedAt, created_at AS CreatedAt, updated_at AS UpdatedAt
            FROM branches
            WHERE index_state = @State::index_state
            ORDER BY updated_at DESC";

        return await _db.QueryAsync<Branch>(sql, new { State = state.ToString() }, cancellationToken);
    }

    public async Task<Branch> CreateAsync(Branch branch, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO branches (id, repo_id, name, head_commit_sha, index_state, indexed_commit_sha,
                                  last_indexed_at, created_at, updated_at)
            VALUES (@Id, @RepoId, @Name, @HeadCommitSha, @IndexState::index_state, @IndexedCommitSha,
                    @LastIndexedAt, @CreatedAt, @UpdatedAt)
            RETURNING id, repo_id AS RepoId, name, head_commit_sha AS HeadCommitSha,
                      index_state AS IndexState, indexed_commit_sha AS IndexedCommitSha,
                      last_indexed_at AS LastIndexedAt, created_at AS CreatedAt, updated_at AS UpdatedAt";

        var result = await _db.QuerySingleAsync<Branch>(sql, new
        {
            branch.Id,
            branch.RepoId,
            branch.Name,
            branch.HeadCommitSha,
            IndexState = branch.IndexState.ToString(),
            branch.IndexedCommitSha,
            branch.LastIndexedAt,
            branch.CreatedAt,
            branch.UpdatedAt
        }, cancellationToken);

        _logger.LogInformation("Created branch {Name} for repo {RepoId}", branch.Name, branch.RepoId);
        return result;
    }

    public async Task<Branch> UpdateAsync(Branch branch, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE branches
            SET name = @Name,
                head_commit_sha = @HeadCommitSha,
                index_state = @IndexState::index_state,
                indexed_commit_sha = @IndexedCommitSha,
                last_indexed_at = @LastIndexedAt,
                updated_at = @UpdatedAt
            WHERE id = @Id
            RETURNING id, repo_id AS RepoId, name, head_commit_sha AS HeadCommitSha,
                      index_state AS IndexState, indexed_commit_sha AS IndexedCommitSha,
                      last_indexed_at AS LastIndexedAt, created_at AS CreatedAt, updated_at AS UpdatedAt";

        var result = await _db.QuerySingleAsync<Branch>(sql, new
        {
            branch.Id,
            branch.Name,
            branch.HeadCommitSha,
            IndexState = branch.IndexState.ToString(),
            branch.IndexedCommitSha,
            branch.LastIndexedAt,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);

        _logger.LogInformation("Updated branch {Name} for repo {RepoId}", branch.Name, branch.RepoId);
        return result;
    }

    public async Task UpdateIndexStateAsync(string id, IndexState state, string? indexedCommitSha = null, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE branches
            SET index_state = @State::index_state,
                indexed_commit_sha = COALESCE(@IndexedCommitSha, indexed_commit_sha),
                last_indexed_at = CASE WHEN @State = 'Completed' THEN NOW() ELSE last_indexed_at END,
                updated_at = NOW()
            WHERE id = @Id";

        await _db.ExecuteAsync(sql, new { Id = id, State = state.ToString(), IndexedCommitSha = indexedCommitSha }, cancellationToken);
        _logger.LogInformation("Updated index state for branch {Id} to {State}", id, state);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM branches WHERE id = @Id";
        var rowsAffected = await _db.ExecuteAsync(sql, new { Id = id }, cancellationToken);

        if (rowsAffected > 0)
        {
            _logger.LogInformation("Deleted branch with ID {Id}", id);
        }

        return rowsAffected > 0;
    }

    public async Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM branches WHERE repo_id = @RepoId";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId }, cancellationToken);

        _logger.LogInformation("Deleted {Count} branches for repo {RepoId}", rowsAffected, repoId);
        return rowsAffected;
    }
}

