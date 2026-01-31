using LancerMcp.Models;

namespace LancerMcp.Repositories;

public interface IEmbeddingJobRepository
{
    Task<int> CreateBatchAsync(IEnumerable<EmbeddingJob> jobs, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmbeddingJob>> ClaimPendingAsync(int batchSize, string workerId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task MarkCompletedAsync(string jobId, int? dims, string? lastError, CancellationToken cancellationToken = default);
    Task MarkBlockedAsync(string jobId, string? lastError, CancellationToken cancellationToken = default);
    Task RequeueAsync(string jobId, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken cancellationToken = default);
    Task<int> RequeueStaleAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken = default);
}
