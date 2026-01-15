using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Repositories;

public sealed class SymbolFingerprintRepository : ISymbolFingerprintRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<SymbolFingerprintRepository> _logger;

    public SymbolFingerprintRepository(DatabaseService db, ILogger<SymbolFingerprintRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SymbolFingerprintEntry?> GetBySymbolIdAsync(string symbolId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT symbol_id AS SymbolId,
                   repo_id AS RepositoryName,
                   branch_name AS BranchName,
                   commit_sha AS CommitSha,
                   file_path AS FilePath,
                   language,
                   kind,
                   fingerprint_kind AS FingerprintKind,
                   fingerprint AS Fingerprint,
                   band0,
                   band1,
                   band2,
                   band3
            FROM symbol_fingerprints
            WHERE symbol_id = @SymbolId";

        var row = await _db.QueryFirstOrDefaultAsync<FingerprintRow>(sql, new { SymbolId = symbolId }, cancellationToken);
        return row == null ? null : ToEntry(row);
    }

    public async Task<IEnumerable<(string SymbolId, ulong Fingerprint)>> FindCandidatesAsync(
        string repoId,
        string branchName,
        Language language,
        SymbolKind kind,
        string fingerprintKind,
        int band0,
        int band1,
        int band2,
        int band3,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoId))
        {
            throw new ArgumentException("Repository ID is required. Multi-repo queries are not supported.", nameof(repoId));
        }

        const string sql = @"
            SELECT symbol_id AS SymbolId,
                   fingerprint AS Fingerprint
            FROM symbol_fingerprints
            WHERE repo_id = @RepoId
              AND branch_name = @BranchName
              AND language = @Language::language
              AND kind = @Kind::symbol_kind
              AND fingerprint_kind = @FingerprintKind
              AND (band0 = @Band0 OR band1 = @Band1 OR band2 = @Band2 OR band3 = @Band3)
            LIMIT @Limit";

        var rows = await _db.QueryAsync<CandidateRow>(sql, new
        {
            RepoId = repoId,
            BranchName = branchName,
            Language = language.ToString(),
            Kind = kind.ToString(),
            FingerprintKind = fingerprintKind,
            Band0 = band0,
            Band1 = band1,
            Band2 = band2,
            Band3 = band3,
            Limit = limit
        }, cancellationToken);

        return rows.Select(row => (row.SymbolId, unchecked((ulong)row.Fingerprint)));
    }

    public async Task<int> CreateBatchAsync(IEnumerable<SymbolFingerprintEntry> entries, CancellationToken cancellationToken = default)
    {
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            return 0;
        }

        const string sql = @"
            INSERT INTO symbol_fingerprints (symbol_id, repo_id, branch_name, commit_sha, file_path, language, kind,
                                             fingerprint_kind, fingerprint, band0, band1, band2, band3)
            VALUES (@SymbolId, @RepositoryName, @BranchName, @CommitSha, @FilePath, @Language::language, @Kind::symbol_kind,
                    @FingerprintKind, @Fingerprint, @Band0, @Band1, @Band2, @Band3)
            ON CONFLICT (symbol_id) DO UPDATE
            SET repo_id = EXCLUDED.repo_id,
                branch_name = EXCLUDED.branch_name,
                commit_sha = EXCLUDED.commit_sha,
                file_path = EXCLUDED.file_path,
                language = EXCLUDED.language,
                kind = EXCLUDED.kind,
                fingerprint_kind = EXCLUDED.fingerprint_kind,
                fingerprint = EXCLUDED.fingerprint,
                band0 = EXCLUDED.band0,
                band1 = EXCLUDED.band1,
                band2 = EXCLUDED.band2,
                band3 = EXCLUDED.band3";

        var rows = entryList.Select(entry => new
        {
            entry.SymbolId,
            entry.RepositoryName,
            entry.BranchName,
            entry.CommitSha,
            entry.FilePath,
            Language = entry.Language.ToString(),
            Kind = entry.Kind.ToString(),
            entry.FingerprintKind,
            Fingerprint = unchecked((long)entry.Fingerprint),
            entry.Band0,
            entry.Band1,
            entry.Band2,
            entry.Band3
        }).ToList();

        var rowsAffected = await _db.ExecuteAsync(sql, rows, cancellationToken);
        _logger.LogInformation("Inserted/updated {Count} symbol fingerprints", rowsAffected);
        return rowsAffected;
    }

    private static SymbolFingerprintEntry ToEntry(FingerprintRow row)
        => new()
        {
            SymbolId = row.SymbolId,
            RepositoryName = row.RepositoryName,
            BranchName = row.BranchName,
            CommitSha = row.CommitSha,
            FilePath = row.FilePath,
            Language = row.Language,
            Kind = row.Kind,
            FingerprintKind = row.FingerprintKind,
            Fingerprint = unchecked((ulong)row.Fingerprint),
            Band0 = row.Band0,
            Band1 = row.Band1,
            Band2 = row.Band2,
            Band3 = row.Band3
        };

    private sealed record FingerprintRow(
        string SymbolId,
        string RepositoryName,
        string BranchName,
        string CommitSha,
        string FilePath,
        Language Language,
        SymbolKind Kind,
        string FingerprintKind,
        long Fingerprint,
        int Band0,
        int Band1,
        int Band2,
        int Band3);

    private sealed record CandidateRow(string SymbolId, long Fingerprint);
}
