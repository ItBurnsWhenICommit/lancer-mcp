using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LancerMcp.Tests;

public sealed class EmbeddingJobWorkerTests
{
    [Fact]
    public async Task ProcessOnce_CompletesJobAndWritesEmbeddings()
    {
        var options = new TestOptionsMonitor(new ServerOptions
        {
            EmbeddingsEnabled = true,
            EmbeddingJobsBatchSize = 10,
            EmbeddingJobsMaxAttempts = 3,
            EmbeddingJobsStaleMinutes = 10
        });
        var jobs = new InMemoryEmbeddingJobRepository();
        var chunks = new InMemoryChunkRepository();
        var embeddings = new InMemoryEmbeddingRepository();
        var provider = new StubEmbeddingProvider(EmbeddingProviderResultFor("chunk1", 2));
        var worker = new EmbeddingJobWorker(
            jobs,
            chunks,
            embeddings,
            provider,
            options,
            NullLogger<EmbeddingJobWorker>.Instance,
            "test:1");

        chunks.Add(CreateChunk("chunk1"));
        jobs.AddPending(CreateJob("chunk1"));

        await worker.ProcessOnceAsync(CancellationToken.None);

        var job = jobs.Jobs.Single();
        Assert.Equal(EmbeddingJobStatus.Completed, job.Status);
        Assert.Null(job.LastError);
        Assert.Equal(2, job.Dims);
        Assert.True(embeddings.Has("chunk1"));
    }

    [Fact]
    public async Task ProcessOnce_MissingChunk_CompletesWithError()
    {
        var options = new TestOptionsMonitor(new ServerOptions
        {
            EmbeddingsEnabled = true,
            EmbeddingJobsBatchSize = 10,
            EmbeddingJobsMaxAttempts = 3,
            EmbeddingJobsStaleMinutes = 10
        });
        var jobs = new InMemoryEmbeddingJobRepository();
        var chunks = new InMemoryChunkRepository();
        var embeddings = new InMemoryEmbeddingRepository();
        var provider = new StubEmbeddingProvider(EmbeddingProviderResultFor("chunk1", 2));
        var worker = new EmbeddingJobWorker(
            jobs,
            chunks,
            embeddings,
            provider,
            options,
            NullLogger<EmbeddingJobWorker>.Instance,
            "test:1");

        jobs.AddPending(CreateJob("missing"));

        await worker.ProcessOnceAsync(CancellationToken.None);

        var job = jobs.Jobs.Single();
        Assert.Equal(EmbeddingJobStatus.Completed, job.Status);
        Assert.Equal(EmbeddingJobErrorCodes.ChunkMissing, job.LastError);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task ProcessOnce_ProviderFailure_RequeuesWithBackoff()
    {
        var options = new TestOptionsMonitor(new ServerOptions
        {
            EmbeddingsEnabled = true,
            EmbeddingJobsBatchSize = 10,
            EmbeddingJobsMaxAttempts = 3,
            EmbeddingJobsStaleMinutes = 10
        });
        var jobs = new InMemoryEmbeddingJobRepository();
        var chunks = new InMemoryChunkRepository();
        var embeddings = new InMemoryEmbeddingRepository();
        var provider = new StubEmbeddingProvider(new EmbeddingProviderResult(
            IsSuccess: false,
            IsTransientFailure: true,
            ErrorCode: EmbeddingJobErrorCodes.ProviderError,
            ErrorMessage: "provider down",
            Dims: null,
            Vector: null,
            Embeddings: Array.Empty<Embedding>()));
        var worker = new EmbeddingJobWorker(
            jobs,
            chunks,
            embeddings,
            provider,
            options,
            NullLogger<EmbeddingJobWorker>.Instance,
            "test:1");

        chunks.Add(CreateChunk("chunk1"));
        jobs.AddPending(CreateJob("chunk1"));

        var before = DateTimeOffset.UtcNow;
        await worker.ProcessOnceAsync(CancellationToken.None);

        var job = jobs.Jobs.Single();
        Assert.Equal(EmbeddingJobStatus.Pending, job.Status);
        Assert.Equal("provider_error", job.LastError);
        Assert.NotNull(job.NextAttemptAt);
        Assert.True(job.NextAttemptAt >= before);
        Assert.Equal(1, provider.Calls);
        Assert.False(embeddings.Has("chunk1"));
    }

    [Fact]
    public async Task ProcessOnce_MaxAttempts_BlocksJob()
    {
        var options = new TestOptionsMonitor(new ServerOptions
        {
            EmbeddingsEnabled = true,
            EmbeddingJobsBatchSize = 10,
            EmbeddingJobsMaxAttempts = 2,
            EmbeddingJobsStaleMinutes = 10
        });
        var jobs = new InMemoryEmbeddingJobRepository();
        var chunks = new InMemoryChunkRepository();
        var embeddings = new InMemoryEmbeddingRepository();
        var provider = new StubEmbeddingProvider(new EmbeddingProviderResult(
            IsSuccess: false,
            IsTransientFailure: true,
            ErrorCode: EmbeddingJobErrorCodes.ProviderError,
            ErrorMessage: "provider down",
            Dims: null,
            Vector: null,
            Embeddings: Array.Empty<Embedding>()));
        var worker = new EmbeddingJobWorker(
            jobs,
            chunks,
            embeddings,
            provider,
            options,
            NullLogger<EmbeddingJobWorker>.Instance,
            "test:1");

        chunks.Add(CreateChunk("chunk1"));
        var job = CreateJob("chunk1");
        job.Attempts = 1;
        jobs.AddPending(job);

        await worker.ProcessOnceAsync(CancellationToken.None);

        var updated = jobs.Jobs.Single();
        Assert.Equal(EmbeddingJobStatus.Blocked, updated.Status);
        Assert.Equal(EmbeddingJobErrorCodes.MaxAttemptsExceeded, updated.LastError);
        Assert.Null(updated.NextAttemptAt);
        Assert.Equal(1, provider.Calls);
        Assert.False(embeddings.Has("chunk1"));
    }

    private static EmbeddingJob CreateJob(string chunkId)
    {
        return new EmbeddingJob
        {
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            TargetKind = "code_chunk",
            TargetId = chunkId,
            Model = "model-a",
            Status = EmbeddingJobStatus.Pending
        };
    }

    private static CodeChunk CreateChunk(string id)
    {
        return new CodeChunk
        {
            Id = id,
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "src/File.cs",
            Language = Language.CSharp,
            Content = "content",
            StartLine = 1,
            EndLine = 1,
            ChunkStartLine = 1,
            ChunkEndLine = 1
        };
    }

    private static EmbeddingProviderResult EmbeddingProviderResultFor(string chunkId, int dims)
    {
        var embedding = new Embedding
        {
            ChunkId = chunkId,
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            Vector = new float[dims],
            Model = "model-a"
        };

        return new EmbeddingProviderResult(
            IsSuccess: true,
            IsTransientFailure: false,
            ErrorCode: null,
            ErrorMessage: null,
            Dims: dims,
            Vector: null,
            Embeddings: new[] { embedding });
    }

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        private readonly EmbeddingProviderResult _result;

        public StubEmbeddingProvider(EmbeddingProviderResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }

        public bool IsAvailable => true;

        public Task<EmbeddingProviderResult> TryGenerateQueryEmbeddingAsync(string input, CancellationToken cancellationToken)
            => Task.FromResult(_result);

        public Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class InMemoryEmbeddingJobRepository : IEmbeddingJobRepository
    {
        public List<EmbeddingJob> Jobs { get; } = new();

        public void AddPending(EmbeddingJob job)
        {
            Jobs.Add(job);
        }

        public Task<int> CreateBatchAsync(IEnumerable<EmbeddingJob> jobs, CancellationToken cancellationToken = default)
        {
            Jobs.AddRange(jobs);
            return Task.FromResult(jobs.Count());
        }

        public Task<IReadOnlyList<EmbeddingJob>> ClaimPendingAsync(int batchSize, string workerId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            var claimed = Jobs
                .Where(job => job.Status == EmbeddingJobStatus.Pending && (!job.NextAttemptAt.HasValue || job.NextAttemptAt <= now))
                .OrderBy(job => job.Id)
                .Take(batchSize)
                .ToList();

            foreach (var job in claimed)
            {
                job.Status = EmbeddingJobStatus.Processing;
                job.Attempts += 1;
            }

            return Task.FromResult<IReadOnlyList<EmbeddingJob>>(claimed);
        }

        public Task MarkCompletedAsync(string jobId, int? dims, string? lastError, CancellationToken cancellationToken = default)
        {
            var job = Jobs.Single(j => j.Id == jobId);
            job.Status = EmbeddingJobStatus.Completed;
            job.Dims = dims ?? job.Dims;
            job.LastError = lastError;
            job.NextAttemptAt = null;
            return Task.CompletedTask;
        }

        public Task MarkBlockedAsync(string jobId, string? lastError, CancellationToken cancellationToken = default)
        {
            var job = Jobs.Single(j => j.Id == jobId);
            job.Status = EmbeddingJobStatus.Blocked;
            job.LastError = lastError;
            job.NextAttemptAt = null;
            return Task.CompletedTask;
        }

        public Task RequeueAsync(string jobId, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken cancellationToken = default)
        {
            var job = Jobs.Single(j => j.Id == jobId);
            job.Status = EmbeddingJobStatus.Pending;
            job.NextAttemptAt = nextAttemptAt;
            job.LastError = lastError;
            return Task.CompletedTask;
        }

        public Task<int> RequeueStaleAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class InMemoryEmbeddingRepository : IEmbeddingRepository
    {
        private readonly Dictionary<string, Embedding> _embeddings = new();

        public bool Has(string chunkId) => _embeddings.ContainsKey(chunkId);

        public Task<int> CreateBatchAsync(IEnumerable<Embedding> embeddings, CancellationToken cancellationToken = default)
        {
            foreach (var embedding in embeddings)
            {
                _embeddings[embedding.ChunkId] = embedding;
            }

            return Task.FromResult(embeddings.Count());
        }

        public Task<Embedding?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Embedding?> GetByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<Embedding>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<(Embedding Embedding, float Distance)>> SearchBySimilarityAsync(float[] queryVector, string? repoId = null, string? branchName = null, int limit = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<(string ChunkId, float Score, float? BM25Score, float? VectorScore)>> HybridSearchAsync(string queryText, float[] queryVector, string? repoId = null, string? branchName = null, float bm25Weight = 0.3f, float vectorWeight = 0.7f, int limit = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Embedding> CreateAsync(Embedding embedding, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ExistsForChunkAsync(string chunkId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class InMemoryChunkRepository : ICodeChunkRepository
    {
        private readonly Dictionary<string, CodeChunk> _chunks = new();

        public void Add(CodeChunk chunk)
        {
            _chunks[chunk.Id] = chunk;
        }

        public Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_chunks.TryGetValue(id, out var chunk) ? chunk : null);

        public Task<IEnumerable<CodeChunk>> SearchFullTextAsync(string repoId, string query, string? branchName = null, Language? language = null, int limit = 50, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IEnumerable<CodeChunk>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetBySymbolAsync(string symbolId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IEnumerable<CodeChunk>> GetByLanguageAsync(string repoId, Language language, int limit = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<CodeChunk> CreateAsync(CodeChunk chunk, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> CreateBatchAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<int> GetCountAsync(string repoId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
