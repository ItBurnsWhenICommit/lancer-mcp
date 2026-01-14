using LancerMcp.Models;

namespace LancerMcp.Repositories;

/// <summary>
/// Repository interface for managing symbol search entries.
/// </summary>
public interface ISymbolSearchRepository
{
    /// <summary>
    /// Searches symbol entries using full-text search.
    /// </summary>
    Task<IEnumerable<(string SymbolId, float Score)>> SearchAsync(
        string repoId,
        string query,
        string? branchName,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates multiple symbol search entries in a batch.
    /// </summary>
    Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default);
}
