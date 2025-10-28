namespace ProjectIndexerMcp.Models;

/// <summary>
/// Represents a chunk of code for embedding and semantic search.
/// Chunks are created at function/class granularity with context overlap.
/// </summary>
public sealed class CodeChunk
{
    /// <summary>
    /// Unique identifier for this chunk.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Repository name.
    /// </summary>
    public required string RepositoryName { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit SHA where this chunk was created.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    /// File path relative to repository root.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Symbol ID that this chunk represents (if chunk is for a specific symbol).
    /// </summary>
    public string? SymbolId { get; init; }

    /// <summary>
    /// Symbol name (e.g., "UserService.Login").
    /// </summary>
    public string? SymbolName { get; init; }

    /// <summary>
    /// Symbol kind (e.g., Method, Class).
    /// </summary>
    public SymbolKind? SymbolKind { get; init; }

    /// <summary>
    /// Programming language.
    /// </summary>
    public required Language Language { get; init; }

    /// <summary>
    /// The actual code content of this chunk (including context overlap).
    /// This is what gets embedded.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Start line of the primary symbol (1-based, excluding context).
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// End line of the primary symbol (1-based, excluding context).
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Start line including context overlap (1-based).
    /// </summary>
    public required int ChunkStartLine { get; init; }

    /// <summary>
    /// End line including context overlap (1-based).
    /// </summary>
    public required int ChunkEndLine { get; init; }

    /// <summary>
    /// Number of tokens in this chunk (approximate).
    /// Used to ensure chunks fit within embedding model context window.
    /// </summary>
    public int TokenCount { get; init; }

    /// <summary>
    /// Optional parent symbol name (e.g., class name for a method).
    /// </summary>
    public string? ParentSymbolName { get; init; }

    /// <summary>
    /// Optional signature for methods/functions.
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// Optional documentation/comments.
    /// </summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// When this chunk was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents an embedding vector for a code chunk.
/// </summary>
public sealed class Embedding
{
    /// <summary>
    /// Unique identifier for this embedding.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Code chunk ID that this embedding represents.
    /// </summary>
    public required string ChunkId { get; init; }

    /// <summary>
    /// Repository name.
    /// </summary>
    public required string RepositoryName { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit SHA.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    /// The embedding vector (typically 768 dimensions for jina-embeddings-v2-base-code).
    /// </summary>
    public required float[] Vector { get; init; }

    /// <summary>
    /// Model used to generate this embedding.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Model version.
    /// </summary>
    public string? ModelVersion { get; init; }

    /// <summary>
    /// When this embedding was generated.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Result of chunking a parsed file.
/// </summary>
public sealed class ChunkedFile
{
    /// <summary>
    /// Repository name.
    /// </summary>
    public required string RepositoryName { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit SHA.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    /// File path relative to repository root.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Programming language.
    /// </summary>
    public required Language Language { get; init; }

    /// <summary>
    /// Code chunks extracted from this file.
    /// </summary>
    public List<CodeChunk> Chunks { get; init; } = new();

    /// <summary>
    /// Whether chunking was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if chunking failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// When this file was chunked.
    /// </summary>
    public DateTimeOffset ChunkedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Total number of chunks created from this file.
    /// </summary>
    public int TotalChunks => Chunks.Count;

    /// <summary>
    /// Total number of tokens across all chunks in this file.
    /// </summary>
    public int TotalTokens => Chunks.Sum(c => c.TokenCount);
}

