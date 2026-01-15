namespace LancerMcp.Models;

/// <summary>
/// Represents a symbol fingerprint entry for similarity search.
/// </summary>
public sealed class SymbolFingerprintEntry
{
    public required string SymbolId { get; init; }
    public required string RepositoryName { get; init; }
    public required string BranchName { get; init; }
    public required string CommitSha { get; init; }
    public required string FilePath { get; init; }
    public required Language Language { get; init; }
    public required SymbolKind Kind { get; init; }
    public required string FingerprintKind { get; init; }
    public required ulong Fingerprint { get; init; }
    public required int Band0 { get; init; }
    public required int Band1 { get; init; }
    public required int Band2 { get; init; }
    public required int Band3 { get; init; }
}
