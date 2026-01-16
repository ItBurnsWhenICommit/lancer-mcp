using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using Microsoft.Extensions.Options;

namespace LancerMcp.Services;

public sealed class EmbeddingJobWorker
{
    private const string TargetKindCodeChunk = "code_chunk";
    private const int BackoffBaseSeconds = 30;
    private const int BackoffMaxSeconds = 3600;

    private readonly IEmbeddingJobRepository _jobs;
    private readonly ICodeChunkRepository _chunks;
    private readonly IEmbeddingRepository _embeddings;
    private readonly IEmbeddingProvider _provider;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ILogger<EmbeddingJobWorker> _logger;
    private readonly string _workerId;

    public EmbeddingJobWorker(
        IEmbeddingJobRepository jobs,
        ICodeChunkRepository chunks,
        IEmbeddingRepository embeddings,
        IEmbeddingProvider provider,
        IOptionsMonitor<ServerOptions> options,
        ILogger<EmbeddingJobWorker> logger,
        string workerId)
    {
        _jobs = jobs;
        _chunks = chunks;
        _embeddings = embeddings;
        _provider = provider;
        _options = options;
        _logger = logger;
        _workerId = string.IsNullOrWhiteSpace(workerId) ? "unknown" : workerId;
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.EmbeddingsEnabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var staleMinutes = Math.Max(1, _options.CurrentValue.EmbeddingJobsStaleMinutes);
        var staleBefore = now.AddMinutes(-staleMinutes);

        await _jobs.RequeueStaleAsync(staleBefore, cancellationToken);

        var batchSize = Math.Max(1, _options.CurrentValue.EmbeddingJobsBatchSize);
        var batch = await _jobs.ClaimPendingAsync(batchSize, _workerId, now, cancellationToken);
        if (batch.Count == 0)
        {
            return;
        }

        var chunkList = new List<CodeChunk>(batch.Count);
        var jobByChunkId = new Dictionary<string, EmbeddingJob>(StringComparer.Ordinal);

        foreach (var job in batch)
        {
            if (!string.Equals(job.TargetKind, TargetKindCodeChunk, StringComparison.Ordinal))
            {
                await _jobs.MarkBlockedAsync(job.Id, EmbeddingJobErrorCodes.UnsupportedTarget, cancellationToken);
                continue;
            }

            var chunk = await _chunks.GetByIdAsync(job.TargetId, cancellationToken);
            if (chunk == null)
            {
                await _jobs.MarkCompletedAsync(job.Id, null, EmbeddingJobErrorCodes.ChunkMissing, cancellationToken);
                continue;
            }

            chunkList.Add(chunk);
            jobByChunkId[chunk.Id] = job;
        }

        if (chunkList.Count == 0)
        {
            return;
        }

        if (!_provider.IsAvailable)
        {
            await HandleProviderFailureAsync(jobByChunkId.Values, now, EmbeddingJobErrorCodes.ProviderError, cancellationToken);
            return;
        }

        var result = await _provider.TryGenerateEmbeddingsAsync(chunkList, cancellationToken);
        if (!result.IsSuccess)
        {
            var errorCode = string.IsNullOrWhiteSpace(result.ErrorCode)
                ? EmbeddingJobErrorCodes.ProviderError
                : result.ErrorCode;
            await HandleProviderFailureAsync(jobByChunkId.Values, now, errorCode, cancellationToken);
            return;
        }

        if (result.Embeddings.Count == 0)
        {
            await HandleProviderFailureAsync(jobByChunkId.Values, now, EmbeddingJobErrorCodes.ProviderError, cancellationToken);
            return;
        }

        var consistentDims = GetConsistentDims(result.Embeddings);
        if (consistentDims == null)
        {
            await BlockJobsAsync(jobByChunkId.Values, EmbeddingJobErrorCodes.DimsMismatch, cancellationToken);
            return;
        }

        var embeddingsByChunk = result.Embeddings
            .GroupBy(embedding => embedding.ChunkId)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var (chunkId, job) in jobByChunkId)
        {
            if (!embeddingsByChunk.TryGetValue(chunkId, out var embedding))
            {
                await HandleProviderFailureAsync(jobByChunkId.Values, now, EmbeddingJobErrorCodes.ProviderError, cancellationToken);
                return;
            }

            if (job.Dims.HasValue && job.Dims.Value != embedding.Vector.Length)
            {
                await BlockJobsAsync(jobByChunkId.Values, EmbeddingJobErrorCodes.DimsMismatch, cancellationToken);
                return;
            }
        }

        await _embeddings.CreateBatchAsync(embeddingsByChunk.Values, cancellationToken);

        foreach (var embedding in embeddingsByChunk.Values)
        {
            var job = jobByChunkId[embedding.ChunkId];
            await _jobs.MarkCompletedAsync(job.Id, embedding.Vector.Length, null, cancellationToken);
        }
    }

    private async Task HandleProviderFailureAsync(
        IEnumerable<EmbeddingJob> jobs,
        DateTimeOffset now,
        string errorCode,
        CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.CurrentValue.EmbeddingJobsMaxAttempts);

        foreach (var job in jobs)
        {
            if (job.Attempts >= maxAttempts)
            {
                await _jobs.MarkBlockedAsync(job.Id, EmbeddingJobErrorCodes.MaxAttemptsExceeded, cancellationToken);
                continue;
            }

            var delay = ComputeBackoff(job.Attempts);
            await _jobs.RequeueAsync(job.Id, now.Add(delay), errorCode, cancellationToken);
        }
    }

    private async Task BlockJobsAsync(IEnumerable<EmbeddingJob> jobs, string errorCode, CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            await _jobs.MarkBlockedAsync(job.Id, errorCode, cancellationToken);
        }
    }

    private static int? GetConsistentDims(IEnumerable<Embedding> embeddings)
    {
        int? dims = null;
        foreach (var embedding in embeddings)
        {
            if (embedding.Vector.Length == 0)
            {
                return null;
            }

            if (dims == null)
            {
                dims = embedding.Vector.Length;
                continue;
            }

            if (dims.Value != embedding.Vector.Length)
            {
                return null;
            }
        }

        return dims;
    }

    private static TimeSpan ComputeBackoff(int attempts)
    {
        var exponent = Math.Max(1, attempts);
        var seconds = Math.Min(BackoffMaxSeconds, BackoffBaseSeconds * Math.Pow(2, exponent - 1));
        return TimeSpan.FromSeconds(seconds);
    }
}
