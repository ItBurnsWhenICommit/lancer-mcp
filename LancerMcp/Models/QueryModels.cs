namespace LancerMcp.Models;

/// <summary>
/// Represents the intent of a user query.
/// </summary>
public enum QueryIntent
{
    /// <summary>
    /// General code search - find code matching keywords or concepts
    /// </summary>
    Search,

    /// <summary>
    /// Navigate to a specific symbol or definition
    /// </summary>
    Navigation,

    /// <summary>
    /// Find relationships between symbols (calls, references, dependencies)
    /// </summary>
    Relations,

    /// <summary>
    /// Get documentation or explanation of code
    /// </summary>
    Documentation,

    /// <summary>
    /// Find examples or usage patterns
    /// </summary>
    Examples
}

/// <summary>
/// Represents a parsed and analyzed query.
/// </summary>
public sealed class ParsedQuery
{
    /// <summary>
    /// Original query text
    /// </summary>
    public required string OriginalQuery { get; init; }

    /// <summary>
    /// Detected intent of the query
    /// </summary>
    public required QueryIntent Intent { get; init; }

    /// <summary>
    /// Extracted keywords from the query
    /// </summary>
    public required List<string> Keywords { get; init; }

    /// <summary>
    /// Specific symbol names mentioned in the query
    /// </summary>
    public List<string>? SymbolNames { get; init; }

    /// <summary>
    /// Specific file paths mentioned in the query
    /// </summary>
    public List<string>? FilePaths { get; init; }

    /// <summary>
    /// Language filter if specified
    /// </summary>
    public Language? Language { get; init; }

    /// <summary>
    /// Repository filter if specified
    /// </summary>
    public string? RepositoryName { get; init; }

    /// <summary>
    /// Branch filter if specified
    /// </summary>
    public string? BranchName { get; init; }

    /// <summary>
    /// Whether to include related symbols in results
    /// </summary>
    public bool IncludeRelated { get; init; }

    /// <summary>
    /// Maximum number of results to return
    /// </summary>
    public int MaxResults { get; init; } = 50;
}

/// <summary>
/// Represents a single search result with context.
/// </summary>
public sealed class SearchResult
{
    /// <summary>
    /// Unique identifier for this result
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Type of result (chunk, symbol, file)
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Repository name
    /// </summary>
    public required string Repository { get; init; }

    /// <summary>
    /// Branch name
    /// </summary>
    public required string Branch { get; init; }

    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Programming language
    /// </summary>
    public required Language Language { get; init; }

    /// <summary>
    /// Symbol name (if applicable)
    /// </summary>
    public string? SymbolName { get; init; }

    /// <summary>
    /// Symbol kind (if applicable)
    /// </summary>
    public SymbolKind? SymbolKind { get; init; }

    /// <summary>
    /// Code content
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Start line in the file
    /// </summary>
    public int StartLine { get; init; }

    /// <summary>
    /// End line in the file
    /// </summary>
    public int EndLine { get; init; }

    /// <summary>
    /// Relevance score (0-1, higher is better)
    /// </summary>
    public required float Score { get; init; }

    /// <summary>
    /// BM25 score component (if applicable)
    /// </summary>
    public float? BM25Score { get; init; }

    /// <summary>
    /// Vector similarity score component (if applicable)
    /// </summary>
    public float? VectorScore { get; init; }

    /// <summary>
    /// Graph re-ranking score component (if applicable)
    /// </summary>
    public float? GraphScore { get; init; }

    /// <summary>
    /// Symbol signature (if applicable)
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// Documentation (if available)
    /// </summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// Related symbols (references, calls, etc.)
    /// </summary>
    public List<RelatedSymbol>? RelatedSymbols { get; init; }
}

/// <summary>
/// Represents a related symbol in search results.
/// </summary>
public sealed class RelatedSymbol
{
    /// <summary>
    /// Symbol ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Symbol name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Symbol kind
    /// </summary>
    public required SymbolKind Kind { get; init; }

    /// <summary>
    /// Relationship type (calls, references, implements, etc.)
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Line number
    /// </summary>
    public int Line { get; init; }
}

/// <summary>
/// Represents the complete query response with context.
/// </summary>
public sealed class QueryResponse
{
    /// <summary>
    /// Original query
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Detected intent
    /// </summary>
    public required QueryIntent Intent { get; init; }

    /// <summary>
    /// Search results
    /// </summary>
    public required List<SearchResult> Results { get; init; }

    /// <summary>
    /// Total number of results found (before limiting)
    /// </summary>
    public required int TotalResults { get; init; }

    /// <summary>
    /// Time taken to execute the query (milliseconds)
    /// </summary>
    public required long ExecutionTimeMs { get; init; }

    /// <summary>
    /// Suggested follow-up queries
    /// </summary>
    public List<string>? SuggestedQueries { get; init; }

    /// <summary>
    /// Additional context or metadata
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

