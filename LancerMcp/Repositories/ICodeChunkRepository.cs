using LancerMcp.Models;

namespace LancerMcp.Repositories;

/// <summary>
/// Repository interface for managing code chunks in the database.
/// </summary>
public interface ICodeChunkRepository
{
    /// <summary>
    /// Gets a code chunk by ID.
    /// </summary>
    Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all code chunks for a file.
    /// </summary>
    Task<IEnumerable<CodeChunk>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all code chunks for a symbol.
    /// </summary>
    Task<IEnumerable<CodeChunk>> GetBySymbolAsync(string symbolId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all code chunks for a branch.
    /// </summary>
    Task<IEnumerable<CodeChunk>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all code chunks by language.
    /// </summary>
    Task<IEnumerable<CodeChunk>> GetByLanguageAsync(string repoId, Language language, int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches code chunks using full-text search.
    /// </summary>
    Task<IEnumerable<CodeChunk>> SearchFullTextAsync(string repoId, string query, string? branchName = null, Language? language = null, int limit = 50, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new code chunk.
    /// </summary>
    Task<CodeChunk> CreateAsync(CodeChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates multiple code chunks in a batch.
    /// </summary>
    Task<int> CreateBatchAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a code chunk by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all code chunks for a repository.
    /// </summary>
    Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all code chunks for a branch.
    /// </summary>
    Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all code chunks for a specific file.
    /// </summary>
    Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of code chunks for a repository.
    /// </summary>
    Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository interface for managing embeddings in the database.
/// </summary>
public interface IEmbeddingRepository
{
    /// <summary>
    /// Gets an embedding by ID.
    /// </summary>
    Task<Embedding?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an embedding by chunk ID.
    /// </summary>
    Task<Embedding?> GetByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all embeddings for a branch.
    /// </summary>
    Task<IEnumerable<Embedding>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches embeddings using vector similarity (cosine distance).
    /// </summary>
    Task<IEnumerable<(Embedding Embedding, float Distance)>> SearchBySimilarityAsync(
        float[] queryVector,
        string? repoId = null,
        string? branchName = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs hybrid search combining full-text and vector similarity.
    /// </summary>
    Task<IEnumerable<(string ChunkId, float Score, float? BM25Score, float? VectorScore)>> HybridSearchAsync(
        string queryText,
        float[] queryVector,
        string? repoId = null,
        string? branchName = null,
        float bm25Weight = 0.3f,
        float vectorWeight = 0.7f,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new embedding.
    /// </summary>
    Task<Embedding> CreateAsync(Embedding embedding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates multiple embeddings in a batch.
    /// </summary>
    Task<int> CreateBatchAsync(IEnumerable<Embedding> embeddings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an embedding by ID.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all embeddings for a repository.
    /// </summary>
    Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all embeddings for a branch.
    /// </summary>
    Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of embeddings for a repository.
    /// </summary>
    Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an embedding exists for a chunk.
    /// </summary>
    Task<bool> ExistsForChunkAsync(string chunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets distinct embedding models for a repository/branch.
    /// </summary>
    Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets embedding dimensions for a repository/branch and model.
    /// </summary>
    Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if any embeddings exist for a repository/branch and model.
    /// </summary>
    Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default);
}
