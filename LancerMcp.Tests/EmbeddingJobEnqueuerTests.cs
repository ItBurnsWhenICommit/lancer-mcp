using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class EmbeddingJobEnqueuerTests
{
    [Fact]
    public async Task DisabledEmbeddings_SkipsEnqueue()
    {
        var options = new ServerOptions { EmbeddingsEnabled = false, EmbeddingModel = "model-a" };
        var repo = new FakeEmbeddingJobRepository();
        var enqueuer = new EmbeddingJobEnqueuer(new TestOptionsMonitor(options), repo);

        await enqueuer.EnqueueAsync("repo", "main", "sha", new[] { "chunk1" });

        Assert.Empty(repo.Jobs);
    }

    [Fact]
    public async Task MissingModel_EnqueuesBlockedJobs()
    {
        var options = new ServerOptions { EmbeddingsEnabled = true, EmbeddingModel = "" };
        var repo = new FakeEmbeddingJobRepository();
        var enqueuer = new EmbeddingJobEnqueuer(new TestOptionsMonitor(options), repo);

        await enqueuer.EnqueueAsync("repo", "main", "sha", new[] { "chunk1" });

        Assert.Single(repo.Jobs);
        Assert.Equal(EmbeddingJobStatus.Blocked, repo.Jobs[0].Status);
        Assert.Equal("__missing__", repo.Jobs[0].Model);
    }

    private sealed class FakeEmbeddingJobRepository : IEmbeddingJobRepository
    {
        public List<EmbeddingJob> Jobs { get; } = new();

        public Task<int> CreateBatchAsync(IEnumerable<EmbeddingJob> jobs, CancellationToken cancellationToken = default)
        {
            Jobs.AddRange(jobs);
            return Task.FromResult(jobs.Count());
        }

        public Task<IReadOnlyList<EmbeddingJob>> ClaimPendingAsync(int batchSize, string workerId, System.DateTimeOffset now, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<EmbeddingJob>>(new List<EmbeddingJob>());

        public Task MarkCompletedAsync(string jobId, int? dims, string? lastError, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RequeueAsync(string jobId, System.DateTimeOffset nextAttemptAt, string? lastError, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> RequeueStaleAsync(System.DateTimeOffset staleBefore, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }
}
