using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Repositories;

/// <summary>
/// Repository implementation for managing symbol edges in the database.
/// </summary>
public sealed class EdgeRepository : IEdgeRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<EdgeRepository> _logger;

    public EdgeRepository(DatabaseService db, ILogger<EdgeRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SymbolEdge?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, source_symbol_id AS SourceSymbolId, target_symbol_id AS TargetSymbolId,
                   kind, repo_id AS RepositoryName, branch_name AS BranchName,
                   commit_sha AS CommitSha, indexed_at AS IndexedAt
            FROM edges
            WHERE id = @Id";

        return await _db.QueryFirstOrDefaultAsync<SymbolEdge>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<IEnumerable<SymbolEdge>> GetBySourceAsync(string sourceSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default)
    {
        string sql;
        object param;

        if (kind.HasValue)
        {
            sql = @"
                SELECT id, source_symbol_id AS SourceSymbolId, target_symbol_id AS TargetSymbolId,
                       kind, repo_id AS RepositoryName, branch_name AS BranchName,
                       commit_sha AS CommitSha, indexed_at AS IndexedAt
                FROM edges
                WHERE source_symbol_id = @SourceSymbolId AND kind = @Kind::edge_kind";
            param = new { SourceSymbolId = sourceSymbolId, Kind = kind.Value.ToString() };
        }
        else
        {
            sql = @"
                SELECT id, source_symbol_id AS SourceSymbolId, target_symbol_id AS TargetSymbolId,
                       kind, repo_id AS RepositoryName, branch_name AS BranchName,
                       commit_sha AS CommitSha, indexed_at AS IndexedAt
                FROM edges
                WHERE source_symbol_id = @SourceSymbolId";
            param = new { SourceSymbolId = sourceSymbolId };
        }

        return await _db.QueryAsync<SymbolEdge>(sql, param, cancellationToken);
    }

    public async Task<IEnumerable<SymbolEdge>> GetByTargetAsync(string targetSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default)
    {
        string sql;
        object param;

        if (kind.HasValue)
        {
            sql = @"
                SELECT id, source_symbol_id AS SourceSymbolId, target_symbol_id AS TargetSymbolId,
                       kind, repo_id AS RepositoryName, branch_name AS BranchName,
                       commit_sha AS CommitSha, indexed_at AS IndexedAt
                FROM edges
                WHERE target_symbol_id = @TargetSymbolId AND kind = @Kind::edge_kind";
            param = new { TargetSymbolId = targetSymbolId, Kind = kind.Value.ToString() };
        }
        else
        {
            sql = @"
                SELECT id, source_symbol_id AS SourceSymbolId, target_symbol_id AS TargetSymbolId,
                       kind, repo_id AS RepositoryName, branch_name AS BranchName,
                       commit_sha AS CommitSha, indexed_at AS IndexedAt
                FROM edges
                WHERE target_symbol_id = @TargetSymbolId";
            param = new { TargetSymbolId = targetSymbolId };
        }

        return await _db.QueryAsync<SymbolEdge>(sql, param, cancellationToken);
    }

    public async Task<IEnumerable<SymbolEdge>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, source_symbol_id AS SourceSymbolId, target_symbol_id AS TargetSymbolId,
                   kind, repo_id AS RepositoryName, branch_name AS BranchName,
                   commit_sha AS CommitSha, indexed_at AS IndexedAt
            FROM edges
            WHERE repo_id = @RepoId AND branch_name = @BranchName";

        return await _db.QueryAsync<SymbolEdge>(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
    }

    public async Task<SymbolEdge> CreateAsync(SymbolEdge edge, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO edges (id, source_symbol_id, target_symbol_id, kind, repo_id, branch_name,
                               commit_sha, indexed_at)
            VALUES (@Id, @SourceSymbolId, @TargetSymbolId, @Kind::edge_kind, @RepositoryName,
                    @BranchName, @CommitSha, @IndexedAt)
            RETURNING id, source_symbol_id AS SourceSymbolId, target_symbol_id AS TargetSymbolId,
                      kind, repo_id AS RepositoryName, branch_name AS BranchName,
                      commit_sha AS CommitSha, indexed_at AS IndexedAt";

        return await _db.QuerySingleAsync<SymbolEdge>(sql, new
        {
            edge.Id,
            edge.SourceSymbolId,
            edge.TargetSymbolId,
            Kind = edge.Kind.ToString(),
            edge.RepositoryName,
            edge.BranchName,
            edge.CommitSha,
            edge.IndexedAt
        }, cancellationToken);
    }

    public async Task<int> CreateBatchAsync(IEnumerable<SymbolEdge> edges, CancellationToken cancellationToken = default)
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

        var rowsAffected = await _db.ExecuteAsync(sql, edgesList, cancellationToken);
        _logger.LogInformation("Inserted {Count} edges", rowsAffected);
        return rowsAffected;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM edges WHERE id = @Id";
        var rowsAffected = await _db.ExecuteAsync(sql, new { Id = id }, cancellationToken);
        return rowsAffected > 0;
    }

    public async Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM edges WHERE repo_id = @RepoId";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId }, cancellationToken);
        _logger.LogInformation("Deleted {Count} edges for repo {RepoId}", rowsAffected, repoId);
        return rowsAffected;
    }

    public async Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM edges WHERE repo_id = @RepoId AND branch_name = @BranchName";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
        _logger.LogInformation("Deleted {Count} edges for branch {BranchName}", rowsAffected, branchName);
        return rowsAffected;
    }
}

