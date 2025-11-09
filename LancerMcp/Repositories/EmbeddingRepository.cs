using Pgvector;
using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Repositories;

/// <summary>
/// Repository implementation for managing embeddings in the database.
/// </summary>
public sealed class EmbeddingRepository : IEmbeddingRepository
{
    private readonly DatabaseService _db;
    private readonly ILogger<EmbeddingRepository> _logger;

    public EmbeddingRepository(DatabaseService db, ILogger<EmbeddingRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Embedding?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, chunk_id AS ChunkId, repo_id AS RepositoryName, branch_name AS BranchName,
                   commit_sha AS CommitSha, vector::text AS Vector, model, model_version AS ModelVersion,
                   generated_at AS GeneratedAt
            FROM embeddings
            WHERE id = @Id";

        var result = await _db.QueryFirstOrDefaultAsync<dynamic>(sql, new { Id = id }, cancellationToken);
        return result != null ? MapToEmbedding(result) : null;
    }

    public async Task<Embedding?> GetByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, chunk_id AS ChunkId, repo_id AS RepositoryName, branch_name AS BranchName,
                   commit_sha AS CommitSha, vector::text AS Vector, model, model_version AS ModelVersion,
                   generated_at AS GeneratedAt
            FROM embeddings
            WHERE chunk_id = @ChunkId
            LIMIT 1";

        var result = await _db.QueryFirstOrDefaultAsync<dynamic>(sql, new { ChunkId = chunkId }, cancellationToken);
        return result != null ? MapToEmbedding(result) : null;
    }

    public async Task<IEnumerable<Embedding>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT id, chunk_id AS ChunkId, repo_id AS RepositoryName, branch_name AS BranchName,
                   commit_sha AS CommitSha, vector::text AS Vector, model, model_version AS ModelVersion,
                   generated_at AS GeneratedAt
            FROM embeddings
            WHERE repo_id = @RepoId AND branch_name = @BranchName
            LIMIT @Limit";

        var results = await _db.QueryAsync<dynamic>(sql, new { RepoId = repoId, BranchName = branchName, Limit = limit }, cancellationToken);
        return results.Select(MapToEmbedding);
    }

    public async Task<IEnumerable<(Embedding Embedding, float Distance)>> SearchBySimilarityAsync(
        float[] queryVector,
        string? repoId = null,
        string? branchName = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoId))
        {
            throw new ArgumentException("Repository ID is required. Multi-repo queries are not supported.", nameof(repoId));
        }

        var sql = @"
            SELECT id, chunk_id AS ChunkId, repo_id AS RepositoryName, branch_name AS BranchName,
                   commit_sha AS CommitSha, vector::text AS Vector, model, model_version AS ModelVersion,
                   generated_at AS GeneratedAt,
                   vector <=> @QueryVector::vector AS distance
            FROM embeddings
            WHERE repo_id = @RepoId";

        if (!string.IsNullOrEmpty(branchName))
        {
            sql += " AND branch_name = @BranchName";
        }

        sql += @"
            ORDER BY vector <=> @QueryVector::vector
            LIMIT @Limit";

        var vector = new Vector(queryVector);
        var results = await _db.QueryAsync<dynamic>(sql, new
        {
            QueryVector = vector.ToString(),
            RepoId = repoId,
            BranchName = branchName,
            Limit = limit
        }, cancellationToken);

        var resultList = new List<(Embedding, float)>();
        foreach (var r in results)
        {
            var embedding = MapToEmbedding(r);
            var distance = (float)r.distance;
            resultList.Add((embedding, distance));
        }

        return resultList;
    }

    public async Task<IEnumerable<(string ChunkId, float Score, float? BM25Score, float? VectorScore)>> HybridSearchAsync(
        string queryText,
        float[] queryVector,
        string? repoId = null,
        string? branchName = null,
        float bm25Weight = 0.3f,
        float vectorWeight = 0.7f,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repoId))
        {
            throw new ArgumentException("Repository ID is required. Multi-repo queries are not supported.", nameof(repoId));
        }

        const string sql = @"
            SELECT * FROM hybrid_search(
                @QueryText,
                @QueryVector::vector,
                @RepoId,
                @BranchName,
                NULL,
                @BM25Weight,
                @VectorWeight,
                @Limit
            )";

        var vector = new Vector(queryVector);
        var results = await _db.QueryAsync<dynamic>(sql, new
        {
            RepoId = repoId,
            BranchName = branchName,
            QueryText = queryText,
            QueryVector = vector.ToString(),
            BM25Weight = bm25Weight,
            VectorWeight = vectorWeight,
            Limit = limit
        }, cancellationToken);

        return results.Select(r => (
            ChunkId: (string)r.chunk_id,
            Score: (float)r.combined_score,
            BM25Score: r.bm25_score != null ? (float?)r.bm25_score : null,
            VectorScore: r.vector_score != null ? (float?)r.vector_score : null
        ));
    }

    public async Task<Embedding> CreateAsync(Embedding embedding, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO embeddings (id, chunk_id, repo_id, branch_name, commit_sha, vector,
                                    model, model_version, generated_at)
            VALUES (@Id, @ChunkId, @RepositoryName, @BranchName, @CommitSha, @Vector::vector,
                    @Model, @ModelVersion, @GeneratedAt)
            ON CONFLICT (chunk_id) DO UPDATE
            SET vector = EXCLUDED.vector,
                model = EXCLUDED.model,
                model_version = EXCLUDED.model_version,
                generated_at = EXCLUDED.generated_at
            RETURNING id";

        var vector = new Vector(embedding.Vector);
        await _db.ExecuteAsync(sql, new
        {
            embedding.Id,
            embedding.ChunkId,
            embedding.RepositoryName,
            embedding.BranchName,
            embedding.CommitSha,
            Vector = vector.ToString(),
            embedding.Model,
            embedding.ModelVersion,
            embedding.GeneratedAt
        }, cancellationToken);

        return embedding;
    }

    public async Task<int> CreateBatchAsync(IEnumerable<Embedding> embeddings, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO embeddings (id, chunk_id, repo_id, branch_name, commit_sha, vector,
                                    model, model_version, generated_at)
            VALUES (@Id, @ChunkId, @RepositoryName, @BranchName, @CommitSha, @Vector::vector,
                    @Model, @ModelVersion, @GeneratedAt)
            ON CONFLICT (chunk_id) DO UPDATE
            SET vector = EXCLUDED.vector,
                model = EXCLUDED.model,
                model_version = EXCLUDED.model_version,
                generated_at = EXCLUDED.generated_at";

        var embeddingsList = embeddings.Select(e => new
        {
            e.Id,
            e.ChunkId,
            e.RepositoryName,
            e.BranchName,
            e.CommitSha,
            Vector = new Vector(e.Vector).ToString(),
            e.Model,
            e.ModelVersion,
            e.GeneratedAt
        }).ToList();

        var rowsAffected = await _db.ExecuteAsync(sql, embeddingsList, cancellationToken);
        _logger.LogInformation("Inserted/updated {Count} embeddings", rowsAffected);
        return rowsAffected;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM embeddings WHERE id = @Id";
        var rowsAffected = await _db.ExecuteAsync(sql, new { Id = id }, cancellationToken);
        return rowsAffected > 0;
    }

    public async Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM embeddings WHERE repo_id = @RepoId";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId }, cancellationToken);
        _logger.LogInformation("Deleted {Count} embeddings for repo {RepoId}", rowsAffected, repoId);
        return rowsAffected;
    }

    public async Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM embeddings WHERE repo_id = @RepoId AND branch_name = @BranchName";
        var rowsAffected = await _db.ExecuteAsync(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
        _logger.LogInformation("Deleted {Count} embeddings for branch {BranchName}", rowsAffected, branchName);
        return rowsAffected;
    }

    public async Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT COUNT(*) FROM embeddings WHERE repo_id = @RepoId";
        var count = await _db.ExecuteScalarAsync<int?>(sql, new { RepoId = repoId }, cancellationToken);
        return count ?? 0;
    }

    public async Task<bool> ExistsForChunkAsync(string chunkId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT EXISTS(SELECT 1 FROM embeddings WHERE chunk_id = @ChunkId)";
        return await _db.ExecuteScalarAsync<bool>(sql, new { ChunkId = chunkId }, cancellationToken);
    }

    private static Embedding MapToEmbedding(dynamic result)
    {
        // Parse the vector string representation back to float array
        var vectorString = (string)result.Vector;
        var vector = new Vector(vectorString);

        return new Embedding
        {
            Id = result.id,
            ChunkId = result.ChunkId,
            RepositoryName = result.RepositoryName,
            BranchName = result.BranchName,
            CommitSha = result.CommitSha,
            Vector = vector.ToArray(),
            Model = result.model,
            ModelVersion = result.ModelVersion,
            GeneratedAt = result.GeneratedAt
        };
    }
}

