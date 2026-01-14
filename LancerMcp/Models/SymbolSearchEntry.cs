namespace LancerMcp.Models;

/// <summary>
/// Represents a symbol search entry for sparse retrieval.
/// </summary>
public sealed class SymbolSearchEntry
{
    public required string SymbolId { get; init; }
    public required string RepositoryName { get; init; }
    public required string BranchName { get; init; }
    public required string CommitSha { get; init; }
    public required string FilePath { get; init; }
    public required Language Language { get; init; }
    public required SymbolKind Kind { get; init; }
    public required IReadOnlyList<string> NameTokens { get; init; }
    public required IReadOnlyList<string> QualifiedTokens { get; init; }
    public required IReadOnlyList<string> SignatureTokens { get; init; }
    public required IReadOnlyList<string> DocumentationTokens { get; init; }
    public required IReadOnlyList<string> LiteralTokens { get; init; }
    public string? Snippet { get; init; }
}
