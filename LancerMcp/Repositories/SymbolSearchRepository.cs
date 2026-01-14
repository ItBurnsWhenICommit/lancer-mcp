using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Repositories;

/// <summary>
/// Repository implementation for managing symbol search entries in the database.
/// </summary>
public sealed class SymbolSearchRepository : ISymbolSearchRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<SymbolSearchRepository> _logger;

    public SymbolSearchRepository(DatabaseService db, ILogger<SymbolSearchRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IEnumerable<(string SymbolId, float Score, string? Snippet)>> SearchAsync(
        string repoId,
        string query,
        string? branchName,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoId))
        {
            throw new ArgumentException("Repository ID is required. Multi-repo queries are not supported.", nameof(repoId));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<(string, float, string?)>();
        }

        var sql = @"
            SELECT symbol_id AS SymbolId,
                   snippet AS Snippet,
                   COALESCE(ts_rank(search_vector, websearch_to_tsquery('english', @Query)), 0) AS Score
            FROM symbol_search
            WHERE repo_id = @RepoId
              AND (@BranchName IS NULL OR branch_name = @BranchName)
              AND search_vector @@ websearch_to_tsquery('english', @Query)
            ORDER BY Score DESC
            LIMIT @Limit";

        var results = await _db.QueryAsync<SearchRow>(sql, new
        {
            RepoId = repoId,
            BranchName = branchName,
            Query = query,
            Limit = limit
        }, cancellationToken);

        return results.Select(r => (r.SymbolId, r.Score, r.Snippet));
    }

    public async Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default)
    {
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return 0;
        }

        const string sql = @"
            INSERT INTO symbol_search (symbol_id, repo_id, branch_name, commit_sha, file_path, language, kind,
                                       name_tokens, qualified_tokens, signature_tokens, documentation_tokens, literal_tokens,
                                       snippet, search_vector)
            VALUES (@SymbolId, @RepositoryName, @BranchName, @CommitSha, @FilePath, @Language::language, @Kind::symbol_kind,
                    @NameTokens, @QualifiedTokens, @SignatureTokens, @DocumentationTokens, @LiteralTokens,
                    @Snippet,
                    setweight(to_tsvector('english', @NameTokens), 'A') ||
                    setweight(to_tsvector('english', @QualifiedTokens), 'A') ||
                    setweight(to_tsvector('english', @SignatureTokens), 'B') ||
                    setweight(to_tsvector('english', @DocumentationTokens), 'C') ||
                    setweight(to_tsvector('english', @LiteralTokens), 'D'))
            ON CONFLICT (symbol_id) DO UPDATE
            SET repo_id = EXCLUDED.repo_id,
                branch_name = EXCLUDED.branch_name,
                commit_sha = EXCLUDED.commit_sha,
                file_path = EXCLUDED.file_path,
                language = EXCLUDED.language,
                kind = EXCLUDED.kind,
                name_tokens = EXCLUDED.name_tokens,
                qualified_tokens = EXCLUDED.qualified_tokens,
                signature_tokens = EXCLUDED.signature_tokens,
                documentation_tokens = EXCLUDED.documentation_tokens,
                literal_tokens = EXCLUDED.literal_tokens,
                snippet = EXCLUDED.snippet,
                search_vector = EXCLUDED.search_vector";

        var rowsAffected = await _db.ExecuteAsync(sql, entryList.Select(entry => new
        {
            entry.SymbolId,
            entry.RepositoryName,
            entry.BranchName,
            entry.CommitSha,
            entry.FilePath,
            Language = entry.Language.ToString(),
            Kind = entry.Kind.ToString(),
            NameTokens = JoinTokens(entry.NameTokens),
            QualifiedTokens = JoinTokens(entry.QualifiedTokens),
            SignatureTokens = JoinTokens(entry.SignatureTokens),
            DocumentationTokens = JoinTokens(entry.DocumentationTokens),
            LiteralTokens = JoinTokens(entry.LiteralTokens),
            entry.Snippet
        }).ToList(), cancellationToken);

        _logger.LogInformation("Inserted/updated {Count} symbol search entries", rowsAffected);
        return rowsAffected;
    }

    private static string JoinTokens(IReadOnlyList<string> tokens)
    {
        return tokens.Count == 0 ? string.Empty : string.Join(' ', tokens);
    }

    private sealed record SearchRow(string SymbolId, string? Snippet, float Score);
}
