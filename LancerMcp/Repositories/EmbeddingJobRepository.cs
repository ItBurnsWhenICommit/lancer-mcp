using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Repositories;

public sealed class EmbeddingJobRepository : IEmbeddingJobRepository
{
    private readonly DatabaseService _db;

    public EmbeddingJobRepository(DatabaseService db)
    {
        _db = db;
    }

    public Task<int> CreateBatchAsync(IEnumerable<EmbeddingJob> jobs, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO embedding_jobs
            (id, repo_id, branch_name, commit_sha, target_kind, target_id, model, dims, status, attempts, next_attempt_at, last_error, created_at, updated_at)
            VALUES
            (@Id, @RepositoryName, @BranchName, @CommitSha, @TargetKind, @TargetId, @Model, @Dims, @Status, @Attempts, @NextAttemptAt, @LastError, NOW(), NOW())
            ON CONFLICT (repo_id, branch_name, target_kind, target_id, model) DO UPDATE
            SET status = EXCLUDED.status,
                updated_at = NOW()";

        var payload = jobs.Select(job => new
        {
            job.Id,
            job.RepositoryName,
            job.BranchName,
            job.CommitSha,
            job.TargetKind,
            job.TargetId,
            job.Model,
            job.Dims,
            Status = job.Status.ToString(),
            job.Attempts,
            job.NextAttemptAt,
            job.LastError
        });

        return _db.ExecuteAsync(sql, payload, cancellationToken);
    }

    public async Task<IReadOnlyList<EmbeddingJob>> ClaimPendingAsync(int batchSize, string workerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            WITH cte AS (
                SELECT id
                FROM embedding_jobs
                WHERE status = 'Pending'
                  AND (next_attempt_at IS NULL OR next_attempt_at <= @Now)
                ORDER BY created_at
                FOR UPDATE SKIP LOCKED
                LIMIT @Limit
            )
            UPDATE embedding_jobs ej
            SET status = 'Processing',
                locked_at = @Now,
                locked_by = @WorkerId,
                attempts = attempts + 1,
                updated_at = @Now
            FROM cte
            WHERE ej.id = cte.id
            RETURNING ej.*;";

        var rows = await _db.QueryAsync<dynamic>(sql, new
        {
            Limit = batchSize,
            WorkerId = workerId,
            Now = now
        }, cancellationToken);

        return rows.Select(Map).ToList();
    }

    public Task MarkCompletedAsync(string jobId, int? dims, string? lastError, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE embedding_jobs
            SET status = 'Completed',
                dims = COALESCE(@Dims, dims),
                last_error = @LastError,
                locked_at = NULL,
                locked_by = NULL,
                updated_at = NOW()
            WHERE id = @Id";

        return _db.ExecuteAsync(sql, new { Id = jobId, Dims = dims, LastError = lastError }, cancellationToken);
    }

    public Task RequeueAsync(string jobId, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE embedding_jobs
            SET status = 'Pending',
                next_attempt_at = @NextAttemptAt,
                last_error = @LastError,
                locked_at = NULL,
                locked_by = NULL,
                updated_at = NOW()
            WHERE id = @Id";

        return _db.ExecuteAsync(sql, new { Id = jobId, NextAttemptAt = nextAttemptAt, LastError = lastError }, cancellationToken);
    }

    public Task<int> RequeueStaleAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE embedding_jobs
            SET status = 'Pending',
                locked_at = NULL,
                locked_by = NULL,
                updated_at = NOW()
            WHERE status = 'Processing'
              AND locked_at < @StaleBefore";

        return _db.ExecuteAsync(sql, new { StaleBefore = staleBefore }, cancellationToken);
    }

    private static EmbeddingJob Map(dynamic row)
    {
        return new EmbeddingJob
        {
            Id = row.id,
            RepositoryName = row.repo_id,
            BranchName = row.branch_name,
            CommitSha = row.commit_sha,
            TargetKind = row.target_kind,
            TargetId = row.target_id,
            Model = row.model,
            Dims = row.dims,
            Status = Enum.Parse<EmbeddingJobStatus>((string)row.status),
            Attempts = row.attempts,
            NextAttemptAt = row.next_attempt_at,
            LastError = row.last_error
        };
    }
}
