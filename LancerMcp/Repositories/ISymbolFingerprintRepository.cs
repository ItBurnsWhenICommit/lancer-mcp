using LancerMcp.Models;

namespace LancerMcp.Repositories;

public interface ISymbolFingerprintRepository
{
    Task<SymbolFingerprintEntry?> GetBySymbolIdAsync(string symbolId, CancellationToken cancellationToken = default);

    Task<IEnumerable<(string SymbolId, ulong Fingerprint)>> FindCandidatesAsync(
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
        CancellationToken cancellationToken = default);

    Task<int> CreateBatchAsync(IEnumerable<SymbolFingerprintEntry> entries, CancellationToken cancellationToken = default);
}
