using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Repositories;

/// <summary>
/// Repository implementation for managing symbols in the database.
/// </summary>
public sealed class SymbolRepository : ISymbolRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<SymbolRepository> _logger;

    public SymbolRepository(DatabaseService db, ILogger<SymbolRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Symbol?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE id = @Id";

        return await _db.QueryFirstOrDefaultAsync<Symbol>(sql, new { Id = id }, cancellationToken);
    }

    public async Task<IEnumerable<Symbol>> GetByIdsAsync(IEnumerable<string> symbolIds, CancellationToken cancellationToken = default)
    {
        var ids = symbolIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<Symbol>();
        }

        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE id = ANY(@Ids)";

        return await _db.QueryAsync<Symbol>(sql, new { Ids = ids }, cancellationToken);
    }

    public async Task<IEnumerable<Symbol>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE repo_id = @RepoId AND branch_name = @BranchName AND file_path = @FilePath
            ORDER BY start_line, start_column";

        return await _db.QueryAsync<Symbol>(sql, new { RepoId = repoId, BranchName = branchName, FilePath = filePath }, cancellationToken);
    }

    public async Task<IEnumerable<Symbol>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE repo_id = @RepoId AND branch_name = @BranchName
            ORDER BY file_path, start_line";

        return await _db.QueryAsync<Symbol>(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
    }

    public async Task<IEnumerable<Symbol>> SearchByNameAsync(string repoId, string query, string? branchName = null, bool fuzzy = false, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoId))
        {
            throw new ArgumentException("Repository ID is required. Multi-repo queries are not supported.", nameof(repoId));
        }

        string sql;
        if (fuzzy)
        {
            // Use trigram similarity for fuzzy search
            sql = @"
                SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                       file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                       start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                       end_column AS EndColumn, signature, documentation, modifiers,
                       parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
                FROM symbols
                WHERE repo_id = @RepoId
                  AND (@BranchName IS NULL OR branch_name = @BranchName)
                  AND (name % @Query OR qualified_name % @Query)
                ORDER BY GREATEST(similarity(name, @Query), similarity(COALESCE(qualified_name, ''), @Query)) DESC
                LIMIT @Limit";
        }
        else
        {
            // Use exact or prefix match
            sql = @"
                SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                       file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                       start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                       end_column AS EndColumn, signature, documentation, modifiers,
                       parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
                FROM symbols
                WHERE repo_id = @RepoId
                  AND (@BranchName IS NULL OR branch_name = @BranchName)
                  AND (name ILIKE @Query || '%' OR qualified_name ILIKE @Query || '%')
                ORDER BY name
                LIMIT @Limit";
        }

        return await _db.QueryAsync<Symbol>(sql, new { RepoId = repoId, BranchName = branchName, Query = query, Limit = limit }, cancellationToken);
    }

    public async Task<IEnumerable<Symbol>> GetByKindAsync(string repoId, SymbolKind kind, int limit = 100, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE repo_id = @RepoId AND kind = @Kind::symbol_kind
            ORDER BY name
            LIMIT @Limit";

        return await _db.QueryAsync<Symbol>(sql, new { RepoId = repoId, Kind = kind.ToString(), Limit = limit }, cancellationToken);
    }

    public async Task<Symbol> CreateAsync(Symbol symbol, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO symbols (id, repo_id, branch_name, commit_sha, file_path, name, qualified_name,
                                 kind, language, start_line, start_column, end_line, end_column,
                                 signature, documentation, modifiers, parent_symbol_id, indexed_at)
            VALUES (@Id, @RepositoryName, @BranchName, @CommitSha, @FilePath, @Name, @QualifiedName,
                    @Kind::symbol_kind, @Language::language, @StartLine, @StartColumn, @EndLine, @EndColumn,
                    @Signature, @Documentation, @Modifiers, @ParentSymbolId, @IndexedAt)
            RETURNING id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                      file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                      start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                      end_column AS EndColumn, signature, documentation, modifiers,
                      parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt";

        return await _db.QuerySingleAsync<Symbol>(sql, new
        {
            symbol.Id,
            symbol.RepositoryName,
            symbol.BranchName,
            symbol.CommitSha,
            symbol.FilePath,
            symbol.Name,
            symbol.QualifiedName,
            Kind = symbol.Kind.ToString(),
            Language = symbol.Language.ToString(),
            symbol.StartLine,
            symbol.StartColumn,
            symbol.EndLine,
            symbol.EndColumn,
            symbol.Signature,
            symbol.Documentation,
            symbol.Modifiers,
            symbol.ParentSymbolId,
            symbol.IndexedAt
        }, cancellationToken);
    }

    public async Task<int> CreateBatchAsync(IEnumerable<Symbol> symbols, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO symbols (id, repo_id, branch_name, commit_sha, file_path, name, qualified_name,
                                 kind, language, start_line, start_column, end_line, end_column,
                                 signature, documentation, modifiers, parent_symbol_id, indexed_at)
            VALUES (@Id, @RepositoryName, @BranchName, @CommitSha, @FilePath, @Name, @QualifiedName,
                    @Kind::symbol_kind, @Language::language, @StartLine, @StartColumn, @EndLine, @EndColumn,
                    @Signature, @Documentation, @Modifiers, @ParentSymbolId, @IndexedAt)";

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

        var rowsAffected = await _db.ExecuteAsync(sql, symbolsList, cancellationToken);
        _logger.LogInformation("Inserted {Count} symbols", rowsAffected);
        return rowsAffected;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM symbols WHERE id = @Id";
        var rowsAffected = await _db.ExecuteAsync(sql, new { Id = id }, cancellationToken);
        return rowsAffected > 0;
    }

    public async Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM symbols WHERE repo_id = @RepoId";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId }, cancellationToken);
        _logger.LogInformation("Deleted {Count} symbols for repo {RepoId}", rowsAffected, repoId);
        return rowsAffected;
    }

    public async Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM symbols WHERE repo_id = @RepoId AND branch_name = @BranchName";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
        _logger.LogInformation("Deleted {Count} symbols for branch {BranchName}", rowsAffected, branchName);
        return rowsAffected;
    }

    public async Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM symbols WHERE repo_id = @RepoId AND branch_name = @BranchName AND file_path = @FilePath";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId, BranchName = branchName, FilePath = filePath }, cancellationToken);
        _logger.LogDebug("Deleted {Count} symbols for file {FilePath}", rowsAffected, filePath);
        return rowsAffected;
    }
}
