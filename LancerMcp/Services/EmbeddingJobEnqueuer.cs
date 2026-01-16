using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using Microsoft.Extensions.Options;

namespace LancerMcp.Services;

public sealed class EmbeddingJobEnqueuer
{
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly IEmbeddingJobRepository _jobs;

    public EmbeddingJobEnqueuer(IOptionsMonitor<ServerOptions> options, IEmbeddingJobRepository jobs)
    {
        _options = options;
        _jobs = jobs;
    }

    public Task EnqueueAsync(
        string repoId,
        string branchName,
        string commitSha,
        IEnumerable<string> chunkIds,
        CancellationToken cancellationToken = default)
    {
        if (!_options.CurrentValue.EmbeddingsEnabled)
        {
            return Task.CompletedTask;
        }

        var model = _options.CurrentValue.EmbeddingModel?.Trim();
        var normalized = string.IsNullOrWhiteSpace(model) ? "__missing__" : model.ToLowerInvariant();
        var status = string.IsNullOrWhiteSpace(model) ? EmbeddingJobStatus.Blocked : EmbeddingJobStatus.Pending;

        var jobs = chunkIds.Select(chunkId => new EmbeddingJob
        {
            RepositoryName = repoId,
            BranchName = branchName,
            CommitSha = commitSha,
            TargetKind = "code_chunk",
            TargetId = chunkId,
            Model = normalized,
            Status = status
        }).ToList();

        if (jobs.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _jobs.CreateBatchAsync(jobs, cancellationToken);
    }
}
