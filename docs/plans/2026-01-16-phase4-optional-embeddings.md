# Phase 4 Optional Embeddings Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add optional embeddings (jobs + query rerank) without breaking Fast defaults or requiring embeddings for tests/indexing.

**Architecture:** Introduce durable `embedding_jobs` with a background worker, accept client-supplied query embeddings, and gate Hybrid/Semantic rerank on embedding availability with explicit metadata fallbacks.

**Tech Stack:** C# (.NET), PostgreSQL (pgvector), xUnit, Dapper, BackgroundService.

---

### Task 0: Baseline Unit-Test Gate

**Files:** none

**Step 1: Run baseline unit tests**

Run: `dotnet test LancerMcp.Tests`
Expected: PASS (unit tests only)

---

### Task 1: Query Embedding Parsing + Validation (RED)

**Files:**
- Create: `LancerMcp/Models/QueryEmbeddingInput.cs`
- Create: `LancerMcp/Services/QueryEmbeddingParser.cs`
- Test: `LancerMcp.Tests/QueryEmbeddingParserTests.cs`

**Step 1: Write the failing tests**

```csharp
using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryEmbeddingParserTests
{
    [Fact]
    public void Parse_InvalidBase64_ReturnsError()
    {
        var result = QueryEmbeddingParser.TryParse("not-base64", null, null, 4096);
        Assert.False(result.Success);
        Assert.Equal("invalid_query_embedding", result.ErrorCode);
    }

    [Fact]
    public void Parse_DimsMismatch_ReturnsError()
    {
        var bytes = new byte[8]; // 2 floats
        var base64 = Convert.ToBase64String(bytes);
        var result = QueryEmbeddingParser.TryParse(base64, 3, null, 4096);
        Assert.False(result.Success);
        Assert.Equal("invalid_query_embedding_dims", result.ErrorCode);
    }

    [Fact]
    public void Parse_ValidEmbedding_ReturnsVector()
    {
        var bytes = new byte[4]; // 1 float = 0
        var base64 = Convert.ToBase64String(bytes);
        var result = QueryEmbeddingParser.TryParse(base64, 1, "Model-A", 4096);
        Assert.True(result.Success);
        Assert.Single(result.Vector!);
        Assert.Equal("model-a", result.Model);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter QueryEmbeddingParserTests`
Expected: FAIL with "QueryEmbeddingParser does not exist"

**Step 3: Write minimal implementation**

```csharp
namespace LancerMcp.Models;

public sealed class QueryEmbeddingInput
{
    public float[] Vector { get; init; } = Array.Empty<float>();
    public int Dims => Vector.Length;
    public string? Model { get; init; }
}
```

```csharp
using System.Buffers.Binary;

namespace LancerMcp.Services;

public sealed record QueryEmbeddingParseResult(
    bool Success,
    string? ErrorCode,
    string? Error,
    float[]? Vector,
    string? Model);

public static class QueryEmbeddingParser
{
    public static QueryEmbeddingParseResult TryParse(
        string? base64,
        int? dims,
        string? model,
        int maxDims)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return new QueryEmbeddingParseResult(false, "missing_query_embedding", "Query embedding not provided.", null, null);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return new QueryEmbeddingParseResult(false, "invalid_query_embedding", "Query embedding is not valid base64.", null, null);
        }

        if (bytes.Length == 0 || bytes.Length % 4 != 0)
        {
            return new QueryEmbeddingParseResult(false, "invalid_query_embedding", "Query embedding length is invalid.", null, null);
        }

        var inferredDims = bytes.Length / 4;
        if (dims.HasValue && dims.Value != inferredDims)
        {
            return new QueryEmbeddingParseResult(false, "invalid_query_embedding_dims", "Query embedding dims mismatch.", null, null);
        }

        if (inferredDims <= 0 || inferredDims > maxDims)
        {
            return new QueryEmbeddingParseResult(false, "invalid_query_embedding_dims", "Query embedding dims out of range.", null, null);
        }

        var vector = new float[inferredDims];
        for (var i = 0; i < inferredDims; i++)
        {
            var offset = i * 4;
            var value = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset, 4));
            vector[i] = value;
        }

        return new QueryEmbeddingParseResult(true, null, null, vector, model?.Trim().ToLowerInvariant());
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests --filter QueryEmbeddingParserTests`
Expected: PASS

**Step 5: Commit**

```bash
git add LancerMcp/Models/QueryEmbeddingInput.cs LancerMcp/Services/QueryEmbeddingParser.cs LancerMcp.Tests/QueryEmbeddingParserTests.cs
git commit -m "[tests] add query embedding parser tests"
```

---

### Task 2: Query Fallback Metadata (Hybrid + Semantic) (RED)

**Files:**
- Modify: `LancerMcp/Services/QueryOrchestrator.cs`
- Modify: `LancerMcp/Models/QueryModels.cs`
- Test: `LancerMcp.Tests/QueryEmbeddingFallbackTests.cs`

**Step 1: Write the failing tests**

```csharp
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryEmbeddingFallbackTests
{
    [Fact]
    public async Task Query_DoesNotInvokeEmbeddingProvider()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Hybrid };
        var provider = new SpyEmbeddingProvider();
        var orchestrator = TestOrchestratorFactory.Create(options, new FakeChunkRepository(), new ThrowingEmbeddingRepository(), provider);

        await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid);

        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Hybrid_NoEmbedding_UsesSparseWithMetadata()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Hybrid };
        var orchestrator = TestOrchestratorFactory.Create(
            options,
            new FakeChunkRepository(),
            new ThrowingEmbeddingRepository(),
            new SpyEmbeddingProvider());

        var response = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid);

        Assert.NotEmpty(response.Results);
        Assert.Equal("hybrid->fast", response.Metadata?["fallback"]);
        Assert.Equal(false, response.Metadata?["embeddingUsed"]);
    }

    [Fact]
    public async Task Semantic_NoEmbedding_FallsBackHybridFast()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Semantic };
        var orchestrator = TestOrchestratorFactory.Create(
            options,
            new FakeChunkRepository(),
            new ThrowingEmbeddingRepository(),
            new SpyEmbeddingProvider());

        var response = await orchestrator.QueryAsync(
            query: "database connection",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Semantic);

        Assert.NotEmpty(response.Results);
        Assert.Equal("semantic->hybrid->fast", response.Metadata?["fallback"]);
        Assert.Equal(false, response.Metadata?["embeddingUsed"]);
    }
}
```

```csharp
// TestOrchestratorFactory helper (add to test file or TestUtilities.cs)
private static class TestOrchestratorFactory
{
    public static QueryOrchestrator Create(
        ServerOptions options,
        ICodeChunkRepository chunkRepo,
        IEmbeddingRepository embeddingRepo,
        IEmbeddingProvider provider)
    {
        var optionsMonitor = new TestOptionsMonitor(options);
        return new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            chunkRepo,
            embeddingRepo,
            new FakeSymbolRepository(),
            new ThrowingSymbolSearchRepository(),
            new ThrowingEdgeRepository(),
            new ThrowingSymbolFingerprintRepository(),
            provider,
            optionsMonitor);
    }
}
```

```csharp
private sealed class FakeChunkRepository : ICodeChunkRepository
{
    public Task<IEnumerable<CodeChunk>> SearchFullTextAsync(string repoId, string query, string? branchName = null, Language? language = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<CodeChunk>>(new[]
        {
            new CodeChunk
            {
                Id = "chunk1",
                RepositoryName = repoId,
                BranchName = branchName ?? "main",
                CommitSha = "sha",
                FilePath = "src/Db.cs",
                Language = Language.CSharp,
                Content = "database connection",
                StartLine = 1,
                EndLine = 2,
                ChunkStartLine = 1,
                ChunkEndLine = 2
            },
            new CodeChunk
            {
                Id = "chunk2",
                RepositoryName = repoId,
                BranchName = branchName ?? "main",
                CommitSha = "sha",
                FilePath = "src/Db2.cs",
                Language = Language.CSharp,
                Content = "database connection",
                StartLine = 3,
                EndLine = 4,
                ChunkStartLine = 3,
                ChunkEndLine = 4
            }
        });
    }

    // Other members throw NotImplementedException.
}

private sealed class SpyEmbeddingProvider : IEmbeddingProvider
{
    public int Calls { get; private set; }

    public Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new EmbeddingProviderResult(false, false, "should not be called", Array.Empty<Embedding>()));
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter QueryEmbeddingFallbackTests`
Expected: FAIL (missing metadata/fallback behavior)

**Step 3: Write minimal implementation**

Add fallback metadata fields and hybrid/semantic handling, and extend `QueryAsync` to accept query embeddings:

```csharp
// In QueryOrchestrator.QueryAsync metadata
metadata["embeddingUsed"] = false;
metadata["fallback"] = parsedQuery.Profile == RetrievalProfile.Semantic ? "semantic->hybrid->fast" : "hybrid->fast";
```

```csharp
public Task<QueryResponse> QueryAsync(
    string query,
    string repositoryName,
    string? branchName = null,
    Language? language = null,
    int maxResults = 50,
    RetrievalProfile? profileOverride = null,
    string? queryEmbeddingBase64 = null,
    int? queryEmbeddingDims = null,
    string? queryEmbeddingModel = null,
    CancellationToken cancellationToken = default)
```

```csharp
// Parse query embedding near the start of QueryAsync
var embeddingParse = QueryEmbeddingParser.TryParse(
    queryEmbeddingBase64,
    queryEmbeddingDims,
    queryEmbeddingModel,
    maxDims: 4096);
```

```csharp
// Store parse errors in metadata when needed
if (!embeddingParse.Success && parsedQuery.Profile != RetrievalProfile.Fast)
{
    metadata["errorCode"] = embeddingParse.ErrorCode!;
    metadata["error"] = embeddingParse.Error!;
}
```

```csharp
// In QueryModels.BuildPayload include embedding metadata
if (Metadata?.TryGetValue("embeddingUsed", out var embeddingUsed) == true)
{
    payload["embeddingUsed"] = embeddingUsed;
}
if (Metadata?.TryGetValue("embeddingModel", out var embeddingModel) == true)
{
    payload["embeddingModel"] = embeddingModel;
}
if (Metadata?.TryGetValue("fallback", out var fallback) == true)
{
    payload["fallback"] = fallback;
}
```

Also update `CodeIndexTool.Query` to accept and forward:

```csharp
public async Task<string> Query(
    string repository,
    string query,
    string? branch = null,
    int? maxResults = null,
    string? profile = null,
    string? queryEmbeddingBase64 = null,
    int? queryEmbeddingDims = null,
    string? queryEmbeddingModel = null,
    CancellationToken cancellationToken = default)
```

```csharp
var queryResponse = await _queryOrchestrator.QueryAsync(
    query: query,
    repositoryName: repository,
    branchName: targetBranch,
    language: null,
    maxResults: maxResults ?? 50,
    profileOverride: profileOverride,
    queryEmbeddingBase64: queryEmbeddingBase64,
    queryEmbeddingDims: queryEmbeddingDims,
    queryEmbeddingModel: queryEmbeddingModel,
    cancellationToken: cancellationToken);
```

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests --filter QueryEmbeddingFallbackTests`
Expected: PASS

**Step 5: Commit**

```bash
git add LancerMcp/Services/QueryOrchestrator.cs LancerMcp/Models/QueryModels.cs LancerMcp.Tests/QueryEmbeddingFallbackTests.cs
git commit -m "[tests] cover hybrid/semantic embedding fallbacks"
```

---

### Task 3: Model Resolution + Dims Mismatch (RED)

**Files:**
- Modify: `LancerMcp/Services/QueryOrchestrator.cs`
- Modify: `LancerMcp/Repositories/IEmbeddingRepository.cs`
- Modify: `LancerMcp/Repositories/EmbeddingRepository.cs`
- Test: `LancerMcp.Tests/QueryEmbeddingModelResolutionTests.cs`

**Step 1: Write the failing tests**

```csharp
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryEmbeddingModelResolutionTests
{
    [Fact]
    public async Task MissingModel_UsesDefaultModel()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Hybrid, EmbeddingModel = "model-a" };
        var orchestrator = TestOrchestratorFactory.Create(
            options,
            new FakeChunkRepository(),
            new FakeEmbeddingRepository("model-a", 2),
            new SpyEmbeddingProvider());

        var response = await orchestrator.QueryAsync(
            query: "db",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: Convert.ToBase64String(new byte[8]),
            queryEmbeddingDims: 2,
            queryEmbeddingModel: null);

        Assert.Equal("model-a", response.Metadata?["embeddingModel"]);
    }

    [Fact]
    public async Task AmbiguousModel_FallsBackWithError()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Hybrid, EmbeddingModel = "" };
        var orchestrator = TestOrchestratorFactory.Create(
            options,
            new FakeChunkRepository(),
            new FakeEmbeddingRepository("model-a", 2, "model-b"),
            new SpyEmbeddingProvider());

        var response = await orchestrator.QueryAsync(
            query: "db",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: Convert.ToBase64String(new byte[8]),
            queryEmbeddingDims: 2,
            queryEmbeddingModel: null);

        Assert.Equal("embedding_model_ambiguous", response.Metadata?["errorCode"]);
    }
}

private sealed class FakeEmbeddingRepository : IEmbeddingRepository
{
    private readonly List<string> _models;
    private readonly int _dims;
    private readonly bool _withSimilarity;

    public FakeEmbeddingRepository(string primaryModel, int dims, string? secondaryModel = null, bool withSimilarity = false)
    {
        _models = new List<string> { primaryModel };
        if (!string.IsNullOrWhiteSpace(secondaryModel))
        {
            _models.Add(secondaryModel);
        }
        _dims = dims;
        _withSimilarity = withSimilarity;
    }

    public Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<string>>(_models);

    public Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
        => Task.FromResult<int?>(_dims);

    public Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
        => Task.FromResult(_models.Contains(model));

    public Task<IEnumerable<(Embedding Embedding, float Distance)>> SearchBySimilarityAsync(float[] queryVector, string? repoId = null, string? branchName = null, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (!_withSimilarity)
        {
            return Task.FromResult<IEnumerable<(Embedding, float)>>(Array.Empty<(Embedding, float)>());
        }

        var e1 = new Embedding { ChunkId = "chunk1", RepositoryName = "repo", BranchName = "main", CommitSha = "sha", Vector = new float[_dims], Model = _models[0] };
        var e2 = new Embedding { ChunkId = "chunk2", RepositoryName = "repo", BranchName = "main", CommitSha = "sha", Vector = new float[_dims], Model = _models[0] };
        return Task.FromResult<IEnumerable<(Embedding, float)>>(new[] { (e1, 0.9f), (e2, 0.1f) });
    }

    // Other members throw NotImplementedException.
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter QueryEmbeddingModelResolutionTests`
Expected: FAIL (missing model resolution)

**Step 3: Write minimal implementation**

Add to `IEmbeddingRepository`:

```csharp
Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default);
Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default);
Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default);
```

Implement in `EmbeddingRepository` with SQL that filters by `model` and returns distinct models / dims:

```csharp
public async Task<IReadOnlyList<string>> GetModelsAsync(string repoId, string? branchName = null, CancellationToken cancellationToken = default)
{
    const string sql = @"
        SELECT DISTINCT model
        FROM embeddings
        WHERE repo_id = @RepoId
          AND (@BranchName IS NULL OR branch_name = @BranchName)";

    var rows = await _db.QueryAsync<string>(sql, new { RepoId = repoId, BranchName = branchName }, cancellationToken);
    return rows.Select(m => m.ToLowerInvariant()).Distinct().ToList();
}

public async Task<int?> GetModelDimsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
{
    const string sql = @"
        SELECT dims
        FROM embeddings
        WHERE repo_id = @RepoId
          AND model = @Model
          AND (@BranchName IS NULL OR branch_name = @BranchName)
        LIMIT 1";

    return await _db.ExecuteScalarAsync<int?>(sql, new { RepoId = repoId, BranchName = branchName, Model = model }, cancellationToken);
}

public async Task<bool> HasAnyEmbeddingsAsync(string repoId, string? branchName, string model, CancellationToken cancellationToken = default)
{
    const string sql = @"
        SELECT EXISTS(
            SELECT 1
            FROM embeddings
            WHERE repo_id = @RepoId
              AND model = @Model
              AND (@BranchName IS NULL OR branch_name = @BranchName)
        )";

    return await _db.ExecuteScalarAsync<bool>(sql, new { RepoId = repoId, BranchName = branchName, Model = model }, cancellationToken);
}
```

Update `QueryOrchestrator` to:
- Resolve model (explicit -> default -> single model -> ambiguous).
- Check dims mismatch and fallback with `embedding_dims_mismatch`.
- Gate rerank on `HasAnyEmbeddingsAsync`.

```csharp
// Model resolution sketch inside QueryAsync
string? resolvedModel = null;
if (!string.IsNullOrWhiteSpace(queryEmbeddingModel))
{
    resolvedModel = queryEmbeddingModel.Trim().ToLowerInvariant();
}
else if (!string.IsNullOrWhiteSpace(_options.CurrentValue.EmbeddingModel))
{
    resolvedModel = _options.CurrentValue.EmbeddingModel.Trim().ToLowerInvariant();
}
else
{
    var models = await _embeddingRepository.GetModelsAsync(repositoryName, branchName, cancellationToken);
    if (models.Count == 1)
    {
        resolvedModel = models[0];
    }
    else
    {
        metadata["errorCode"] = "embedding_model_ambiguous";
        metadata["error"] = "Multiple embedding models found; specify queryEmbeddingModel.";
        metadata["fallback"] = parsedQuery.Profile == RetrievalProfile.Semantic ? "semantic->hybrid->fast" : "hybrid->fast";
        return sparseResults;
    }
}

var modelDims = await _embeddingRepository.GetModelDimsAsync(repositoryName, branchName, resolvedModel, cancellationToken);
if (modelDims.HasValue && modelDims.Value != queryEmbedding.Dims)
{
    metadata["errorCode"] = "embedding_dims_mismatch";
    metadata["error"] = "Query embedding dims do not match stored embeddings.";
    metadata["fallback"] = parsedQuery.Profile == RetrievalProfile.Semantic ? "semantic->hybrid->fast" : "hybrid->fast";
    return sparseResults;
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests --filter QueryEmbeddingModelResolutionTests`
Expected: PASS

**Step 5: Commit**

```bash
git add LancerMcp/Services/QueryOrchestrator.cs LancerMcp/Repositories/IEmbeddingRepository.cs LancerMcp/Repositories/EmbeddingRepository.cs LancerMcp.Tests/QueryEmbeddingModelResolutionTests.cs
git commit -m "[phase 4] add embedding model resolution"
```

---

### Task 4: Embedding Jobs Model + Enqueue Logic (RED)

**Files:**
- Create: `LancerMcp/Models/EmbeddingJob.cs`
- Create: `LancerMcp/Repositories/IEmbeddingJobRepository.cs`
- Create: `LancerMcp/Repositories/EmbeddingJobRepository.cs`
- Create: `LancerMcp/Services/EmbeddingJobEnqueuer.cs`
- Modify: `LancerMcp/Services/IndexingService.cs`
- Modify: `LancerMcp/Configuration/ServerOptions.cs`
- Test: `LancerMcp.Tests/EmbeddingJobEnqueuerTests.cs`

**Step 1: Write the failing tests**

```csharp
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Services;
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

        Assert.Equal(EmbeddingJobStatus.Blocked, repo.Jobs[0].Status);
        Assert.Equal("__missing__", repo.Jobs[0].Model);
    }
}

private sealed class FakeEmbeddingJobRepository : IEmbeddingJobRepository
{
    public List<EmbeddingJob> Jobs { get; } = new();

    public Task<int> CreateBatchAsync(IEnumerable<EmbeddingJob> jobs, CancellationToken cancellationToken = default)
    {
        Jobs.AddRange(jobs);
        return Task.FromResult(jobs.Count());
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter EmbeddingJobEnqueuerTests`
Expected: FAIL (missing classes)

**Step 3: Write minimal implementation**

```csharp
namespace LancerMcp.Models;

public enum EmbeddingJobStatus
{
    Pending,
    Processing,
    Completed,
    Blocked
}

public sealed class EmbeddingJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string RepositoryName { get; init; }
    public required string BranchName { get; init; }
    public required string CommitSha { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetId { get; init; }
    public required string Model { get; init; }
    public int? Dims { get; set; }
    public EmbeddingJobStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
```

```csharp
public interface IEmbeddingJobRepository
{
    Task<int> CreateBatchAsync(IEnumerable<EmbeddingJob> jobs, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmbeddingJob>> ClaimPendingAsync(int batchSize, string workerId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task MarkCompletedAsync(string jobId, int? dims, string? lastError, CancellationToken cancellationToken = default);
    Task RequeueAsync(string jobId, DateTimeOffset nextAttemptAt, string? lastError, CancellationToken cancellationToken = default);
    Task<int> RequeueStaleAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken = default);
}
```

```csharp
public sealed class EmbeddingJobEnqueuer
{
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly IEmbeddingJobRepository _jobs;

    public EmbeddingJobEnqueuer(IOptionsMonitor<ServerOptions> options, IEmbeddingJobRepository jobs)
    {
        _options = options;
        _jobs = jobs;
    }

    public Task EnqueueAsync(string repoId, string branchName, string commitSha, IEnumerable<string> chunkIds, CancellationToken cancellationToken = default)
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
        });

        return _jobs.CreateBatchAsync(jobs, cancellationToken);
    }
}
```

Add repository implementation for PostgreSQL:

```csharp
public sealed class EmbeddingJobRepository : IEmbeddingJobRepository
{
    private readonly DatabaseService _db;

    public EmbeddingJobRepository(DatabaseService db) => _db = db;

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

        return _db.ExecuteAsync(sql, jobs.Select(j => new
        {
            j.Id,
            j.RepositoryName,
            j.BranchName,
            j.CommitSha,
            j.TargetKind,
            j.TargetId,
            j.Model,
            j.Dims,
            Status = j.Status.ToString(),
            j.Attempts,
            j.NextAttemptAt,
            j.LastError
        }), cancellationToken);
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

        var rows = await _db.QueryAsync<dynamic>(sql, new { Limit = batchSize, WorkerId = workerId, Now = now }, cancellationToken);
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
```

Add new options to `ServerOptions`:

```csharp
public bool EmbeddingsEnabled { get; set; } = false;
public int EmbeddingJobsBatchSize { get; set; } = 64;
public int EmbeddingJobsMaxAttempts { get; set; } = 10;
public int EmbeddingJobsStaleMinutes { get; set; } = 10;
public int EmbeddingJobsPurgeDays { get; set; } = 7;
```

Update `IndexingService` to call `EmbeddingJobEnqueuer.EnqueueAsync` after chunk persistence:

```csharp
if (allChunks.Any())
{
    var chunkIds = allChunks.Select(c => c.Id).ToList();
    await _embeddingJobEnqueuer.EnqueueAsync(parsedFiles[0].RepositoryName, parsedFiles[0].BranchName, parsedFiles[0].CommitSha, chunkIds, cancellationToken);
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests --filter EmbeddingJobEnqueuerTests`
Expected: PASS

**Step 5: Commit**

```bash
git add LancerMcp/Models/EmbeddingJob.cs LancerMcp/Repositories/IEmbeddingJobRepository.cs LancerMcp/Repositories/EmbeddingJobRepository.cs LancerMcp/Services/EmbeddingJobEnqueuer.cs LancerMcp/Services/IndexingService.cs LancerMcp/Configuration/ServerOptions.cs LancerMcp.Tests/EmbeddingJobEnqueuerTests.cs
git commit -m "[phase 4] enqueue embedding jobs during indexing"
```

---

### Task 5: Embedding Job Worker (RED)

**Files:**
- Create: `LancerMcp/Services/EmbeddingJobWorker.cs`
- Create: `LancerMcp/Services/IEmbeddingProvider.cs`
- Modify: `LancerMcp/Services/EmbeddingService.cs`
- Modify: `LancerMcp/Program.cs`
- Test: `LancerMcp.Tests/EmbeddingJobWorkerTests.cs`

**Step 1: Write the failing tests**

```csharp
using LancerMcp.Models;
using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class EmbeddingJobWorkerTests
{
    [Fact]
    public async Task Worker_CompletesJobsAndWritesEmbeddings()
    {
        var jobs = new InMemoryEmbeddingJobRepository();
        var embeddings = new InMemoryEmbeddingRepository();
        var chunks = new InMemoryChunkRepository();
        chunks.Add("chunk1", "repo", "main", "sha", "content");

        jobs.AddPending("repo", "main", "sha", "chunk1", "model-a");

        var provider = new FakeEmbeddingProvider(2);
        var worker = new EmbeddingJobWorker(jobs, chunks, embeddings, provider, 10, "test:1");

        await worker.ProcessOnceAsync(CancellationToken.None);

        Assert.Equal(EmbeddingJobStatus.Completed, jobs.Jobs[0].Status);
        Assert.True(embeddings.Has("chunk1"));
    }
}

private sealed class InMemoryEmbeddingJobRepository : IEmbeddingJobRepository
{
    public List<EmbeddingJob> Jobs { get; } = new();

    public void AddPending(string repo, string branch, string sha, string chunkId, string model)
    {
        Jobs.Add(new EmbeddingJob
        {
            RepositoryName = repo,
            BranchName = branch,
            CommitSha = sha,
            TargetKind = "code_chunk",
            TargetId = chunkId,
            Model = model,
            Status = EmbeddingJobStatus.Pending
        });
    }

    public Task<int> CreateBatchAsync(IEnumerable<EmbeddingJob> jobs, CancellationToken cancellationToken = default)
    {
        Jobs.AddRange(jobs);
        return Task.FromResult(jobs.Count());
    }

    public Task<IReadOnlyList<EmbeddingJob>> ClaimPendingAsync(int batchSize, string workerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var claimed = Jobs.Where(j => j.Status == EmbeddingJobStatus.Pending).Take(batchSize).ToList();
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
        job.Dims = dims;
        job.LastError = lastError;
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

    // Other members throw NotImplementedException.
}

private sealed class InMemoryChunkRepository : ICodeChunkRepository
{
    private readonly Dictionary<string, CodeChunk> _chunks = new();

    public void Add(string id, string repo, string branch, string sha, string content)
    {
        _chunks[id] = new CodeChunk
        {
            Id = id,
            RepositoryName = repo,
            BranchName = branch,
            CommitSha = sha,
            FilePath = "src/File.cs",
            Language = Language.CSharp,
            Content = content,
            StartLine = 1,
            EndLine = 1,
            ChunkStartLine = 1,
            ChunkEndLine = 1
        };
    }

    public Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_chunks.GetValueOrDefault(id));

    // Other members throw NotImplementedException.
}

private sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    private readonly int _dims;

    public FakeEmbeddingProvider(int dims) => _dims = dims;

    public Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken)
    {
        var embeddings = chunks.Select(chunk => new Embedding
        {
            ChunkId = chunk.Id,
            RepositoryName = chunk.RepositoryName,
            BranchName = chunk.BranchName,
            CommitSha = chunk.CommitSha,
            Vector = new float[_dims],
            Model = "model-a"
        }).ToList();

        return Task.FromResult(new EmbeddingProviderResult(true, false, null, embeddings));
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter EmbeddingJobWorkerTests`
Expected: FAIL (worker + interfaces missing)

**Step 3: Write minimal implementation**

```csharp
public interface IEmbeddingProvider
{
    Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken);
}

public sealed record EmbeddingProviderResult(bool Success, bool IsTransient, string? Error, IReadOnlyList<Embedding> Embeddings);
```

```csharp
public sealed class EmbeddingService : IDisposable, IEmbeddingProvider
{
    // Existing constructor and GenerateEmbeddingsAsync(...) remain.

    public async Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken)
    {
        try
        {
            var embeddings = await GenerateEmbeddingsAsync(chunks, cancellationToken);
            if (embeddings.Count == 0)
            {
                return new EmbeddingProviderResult(false, true, "no_embeddings_generated", Array.Empty<Embedding>());
            }

            return new EmbeddingProviderResult(true, false, null, embeddings);
        }
        catch (Exception ex)
        {
            return new EmbeddingProviderResult(false, true, ex.Message, Array.Empty<Embedding>());
        }
    }
}
```

```csharp
public sealed class EmbeddingJobWorker
{
    private readonly IEmbeddingJobRepository _jobs;
    private readonly ICodeChunkRepository _chunks;
    private readonly IEmbeddingRepository _embeddings;
    private readonly IEmbeddingProvider _provider;
    private readonly int _maxAttempts;
    private readonly string _workerId;

    public EmbeddingJobWorker(
        IEmbeddingJobRepository jobs,
        ICodeChunkRepository chunks,
        IEmbeddingRepository embeddings,
        IEmbeddingProvider provider,
        int maxAttempts,
        string workerId)
    {
        _jobs = jobs;
        _chunks = chunks;
        _embeddings = embeddings;
        _provider = provider;
        _maxAttempts = maxAttempts;
        _workerId = workerId;
    }

    public async Task ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var batch = await _jobs.ClaimPendingAsync(32, _workerId, now, cancellationToken);
        if (batch.Count == 0)
        {
            return;
        }

        var chunkList = new List<CodeChunk>();
        var jobByChunk = new Dictionary<string, EmbeddingJob>();

        foreach (var job in batch)
        {
            var chunk = await _chunks.GetByIdAsync(job.TargetId, cancellationToken);
            if (chunk == null)
            {
                await _jobs.MarkCompletedAsync(job.Id, null, "chunk_missing", cancellationToken);
                continue;
            }

            chunkList.Add(chunk);
            jobByChunk[chunk.Id] = job;
        }

        if (chunkList.Count == 0)
        {
            return;
        }

        var result = await _provider.TryGenerateEmbeddingsAsync(chunkList, cancellationToken);
        if (!result.Success)
        {
            foreach (var job in jobByChunk.Values)
            {
                if (job.Attempts >= _maxAttempts)
                {
                    await _jobs.MarkCompletedAsync(job.Id, job.Dims, result.Error, cancellationToken);
                }
                else
                {
                    var delay = TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, job.Attempts) * 30));
                    await _jobs.RequeueAsync(job.Id, now.Add(delay), result.Error, cancellationToken);
                }
            }
            return;
        }

        await _embeddings.CreateBatchAsync(result.Embeddings, cancellationToken);
        foreach (var embedding in result.Embeddings)
        {
            var job = jobByChunk[embedding.ChunkId];
            await _jobs.MarkCompletedAsync(job.Id, embedding.Vector.Length, null, cancellationToken);
        }
    }
}
```

Add hosted service wrapper:

```csharp
public sealed class EmbeddingJobWorkerHostedService : BackgroundService
{
    private readonly EmbeddingJobWorker _worker;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    public EmbeddingJobWorkerHostedService(EmbeddingJobWorker worker) => _worker = worker;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _worker.ProcessOnceAsync(stoppingToken);
            await Task.Delay(_pollInterval, stoppingToken);
        }
    }
}
```

Update DI registrations in `Program.cs`:

```csharp
builder.Services.AddSingleton<IEmbeddingJobRepository, EmbeddingJobRepository>();
builder.Services.AddSingleton<EmbeddingJobEnqueuer>();

builder.Services.AddHttpClient<IEmbeddingProvider, EmbeddingService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptionsMonitor<ServerOptions>>();
    var embeddingUrl = options.CurrentValue.EmbeddingServiceUrl;
    if (!string.IsNullOrEmpty(embeddingUrl))
    {
        client.BaseAddress = new Uri(embeddingUrl);
    }
    client.Timeout = TimeSpan.FromSeconds(options.CurrentValue.EmbeddingTimeoutSeconds);
});

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptionsMonitor<ServerOptions>>();
    var workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
    return new EmbeddingJobWorker(
        sp.GetRequiredService<IEmbeddingJobRepository>(),
        sp.GetRequiredService<ICodeChunkRepository>(),
        sp.GetRequiredService<IEmbeddingRepository>(),
        sp.GetRequiredService<IEmbeddingProvider>(),
        options.CurrentValue.EmbeddingJobsMaxAttempts,
        workerId);
});

builder.Services.AddHostedService<EmbeddingJobWorkerHostedService>();
```

Modify `EmbeddingService` to implement `IEmbeddingProvider` and return `EmbeddingProviderResult` instead of throwing.

Register worker in `Program.cs` as `AddHostedService<EmbeddingJobWorkerHostedService>()` or use `EmbeddingJobWorker` inside a hosted service.

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests --filter EmbeddingJobWorkerTests`
Expected: PASS

**Step 5: Commit**

```bash
git add LancerMcp/Services/IEmbeddingProvider.cs LancerMcp/Services/EmbeddingJobWorker.cs LancerMcp/Services/EmbeddingService.cs LancerMcp/Program.cs LancerMcp.Tests/EmbeddingJobWorkerTests.cs
git commit -m "[phase 4] add embedding job worker"
```

---

### Task 6: Hybrid/Semantic Rerank Uses Query Embedding (RED)

**Files:**
- Modify: `LancerMcp/Services/QueryOrchestrator.cs`
- Test: `LancerMcp.Tests/QueryEmbeddingRerankTests.cs`

**Step 1: Write the failing tests**

```csharp
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tests.Mocks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryEmbeddingRerankTests
{
    [Fact]
    public async Task Rerank_ChangesOrdering_WhenEmbeddingsExist()
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Hybrid, EmbeddingModel = "model-a" };
        var embeddingRepo = new FakeEmbeddingRepository("model-a", 2, withSimilarity: true);
        var chunkRepo = new FakeChunkRepository();
        var orchestrator = TestOrchestratorFactory.Create(options, chunkRepo, embeddingRepo, new SpyEmbeddingProvider());

        var response = await orchestrator.QueryAsync(
            query: "db",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Hybrid,
            queryEmbeddingBase64: Convert.ToBase64String(new byte[8]),
            queryEmbeddingDims: 2,
            queryEmbeddingModel: "model-a");

        Assert.Equal("chunk2", response.Results.First().Id);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter QueryEmbeddingRerankTests`
Expected: FAIL (no rerank)

**Step 3: Write minimal implementation**

Update `ExecuteHybridSearchAsync` to:
- Run sparse search first.
- If query embedding valid + embeddings exist, fetch vector similarities and rerank results.

```csharp
var sparseResults = await ExecuteFullTextSearchOnlyAsync(parsedQuery, cancellationToken);
if (queryEmbedding == null || string.IsNullOrWhiteSpace(resolvedModel))
{
    metadata["embeddingUsed"] = false;
    return sparseResults;
}

var hasEmbeddings = await _embeddingRepository.HasAnyEmbeddingsAsync(parsedQuery.RepositoryName!, parsedQuery.BranchName, resolvedModel, cancellationToken);
if (!hasEmbeddings)
{
    metadata["embeddingUsed"] = false;
    return sparseResults;
}

var vectorResults = await _embeddingRepository.SearchBySimilarityAsync(
    queryEmbedding.Vector,
    repoId: parsedQuery.RepositoryName,
    branchName: parsedQuery.BranchName,
    limit: parsedQuery.MaxResults * 2,
    cancellationToken);

var vectorScoreByChunk = vectorResults.ToDictionary(v => v.Embedding.ChunkId, v => 1f - v.Distance);
foreach (var result in sparseResults)
{
    if (vectorScoreByChunk.TryGetValue(result.Id, out var vectorScore))
    {
        result.VectorScore = vectorScore;
        result.Score = (result.Score * 0.3f) + (vectorScore * 0.7f);
    }
}

metadata["embeddingUsed"] = vectorScoreByChunk.Count > 0;
metadata["embeddingModel"] = resolvedModel;
metadata["embeddingCandidateCount"] = vectorScoreByChunk.Count;
return sparseResults.OrderByDescending(r => r.Score).ToList();
```

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests --filter QueryEmbeddingRerankTests`
Expected: PASS

**Step 5: Commit**

```bash
git add LancerMcp/Services/QueryOrchestrator.cs LancerMcp.Tests/QueryEmbeddingRerankTests.cs
git commit -m "[phase 4] rerank hybrid results with query embeddings"
```

---

### Task 7: Schema + Repository Updates

**Files:**
- Modify: `database/schema/02_tables.sql`
- Modify: `database/schema/04_functions.sql`
- Modify: `docs/SCHEMA.md`
- Modify: `database/README.md`

**Step 1: Write schema updates**

Add `embedding_jobs` table + indexes + enums/checks. Add `dims` to `embeddings`. Update `hybrid_search` to filter by model.

```sql
CREATE TABLE embedding_jobs (
    id TEXT PRIMARY KEY,
    repo_id TEXT NOT NULL REFERENCES repos(id) ON DELETE CASCADE,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    target_kind TEXT NOT NULL,
    target_id TEXT NOT NULL,
    model TEXT NOT NULL,
    dims INTEGER,
    status TEXT NOT NULL CHECK (status IN ('Pending','Processing','Completed','Blocked')),
    attempts INTEGER NOT NULL DEFAULT 0,
    next_attempt_at TIMESTAMPTZ,
    last_error TEXT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    locked_at TIMESTAMPTZ,
    locked_by TEXT,
    UNIQUE (repo_id, branch_name, target_kind, target_id, model)
);

CREATE INDEX idx_embedding_jobs_status ON embedding_jobs(status);
CREATE INDEX idx_embedding_jobs_next_attempt ON embedding_jobs(next_attempt_at);
CREATE INDEX idx_embedding_jobs_repo_branch ON embedding_jobs(repo_id, branch_name);
```

```sql
ALTER TABLE embeddings ADD COLUMN IF NOT EXISTS dims INTEGER;
UPDATE embeddings SET dims = 768 WHERE dims IS NULL;
```

```sql
CREATE OR REPLACE FUNCTION hybrid_search(
    query_text TEXT,
    query_vector vector(768) DEFAULT NULL,
    repo_filter TEXT DEFAULT NULL,
    branch_filter TEXT DEFAULT NULL,
    language_filter language DEFAULT NULL,
    model_filter TEXT DEFAULT NULL,
    bm25_weight REAL DEFAULT 0.3,
    vector_weight REAL DEFAULT 0.7,
    limit_count INTEGER DEFAULT 10
)
RETURNS TABLE (
    chunk_id TEXT,
    repo_id TEXT,
    branch_name TEXT,
    file_path TEXT,
    symbol_name TEXT,
    symbol_kind symbol_kind,
    content TEXT,
    combined_score REAL,
    bm25_score REAL,
    vector_score REAL
) AS $$
BEGIN
    RETURN QUERY
    WITH bm25_results AS (
        SELECT
            c.id AS chunk_id,
            ts_rank_cd(to_tsvector('english', c.content), plainto_tsquery('english', query_text)) AS score
        FROM code_chunks c
        WHERE
            to_tsvector('english', c.content) @@ plainto_tsquery('english', query_text)
            AND (repo_filter IS NULL OR c.repo_id = repo_filter)
            AND (branch_filter IS NULL OR c.branch_name = branch_filter)
            AND (language_filter IS NULL OR c.language = language_filter)
    ),
    vector_results AS (
        SELECT
            e.chunk_id,
            (1 - (e.vector <=> query_vector))::REAL AS score
        FROM embeddings e
        JOIN code_chunks c ON e.chunk_id = c.id
        WHERE
            query_vector IS NOT NULL
            AND (repo_filter IS NULL OR c.repo_id = repo_filter)
            AND (branch_filter IS NULL OR c.branch_name = branch_filter)
            AND (language_filter IS NULL OR c.language = language_filter)
            AND (model_filter IS NULL OR e.model = model_filter)
        ORDER BY e.vector <=> query_vector
        LIMIT limit_count * 2
    ),
    combined AS (
        SELECT
            COALESCE(b.chunk_id, v.chunk_id) AS chunk_id,
            COALESCE(b.score, 0) * bm25_weight + COALESCE(v.score, 0) * vector_weight AS combined_score,
            COALESCE(b.score, 0) AS bm25_score,
            COALESCE(v.score, 0) AS vector_score
        FROM bm25_results b
        FULL OUTER JOIN vector_results v ON b.chunk_id = v.chunk_id
    )
    SELECT
        c.id,
        c.repo_id,
        c.branch_name,
        c.file_path,
        c.symbol_name,
        c.symbol_kind,
        c.content,
        comb.combined_score,
        comb.bm25_score,
        comb.vector_score
    FROM combined comb
    JOIN code_chunks c ON comb.chunk_id = c.id
    ORDER BY comb.combined_score DESC
    LIMIT limit_count;
END;
$$ LANGUAGE plpgsql;
```

**Step 2: Update docs**

Document `embedding_jobs` + `embeddings.dims`.

**Step 3: Commit**

```bash
git add database/schema/02_tables.sql database/schema/04_functions.sql docs/SCHEMA.md database/README.md
git commit -m "[phase 4] add embedding_jobs schema"
```

---

### Task 8: Docs + Changelog

**Files:**
- Modify: `docs/RETRIEVAL_PROFILES.md`
- Modify: `docs/INDEXING_V2_OVERVIEW.md`
- Modify: `docs/BENCHMARKS.md`
- Modify: `docs/CHANGELOG_REFACTOR.md`

**Step 1: Update docs for Phase 4**
- Profiles: fallback chains + metadata fields.
- Indexing overview: job queue + worker, query embedding input.
- Benchmarks: note embeddings off vs on.
- Changelog: append Phase 4 entry.

**Step 2: Commit**

```bash
git add docs/RETRIEVAL_PROFILES.md docs/INDEXING_V2_OVERVIEW.md docs/BENCHMARKS.md docs/CHANGELOG_REFACTOR.md
git commit -m "[phase 4] document optional embeddings behavior"
```

---

### Task 9: Fast Gate Re-Run

**Files:** none

**Step 1: Run unit tests**

Run: `dotnet test LancerMcp.Tests`
Expected: PASS

---

### Task 10: Full Gate (after fixtures restored)

**Files:** none

**Step 1: Restore fixtures**

Run: `./scripts/refresh-fixtures.sh`

**Step 2: Run full gate**

Run: `dotnet test lancer-mcp.sln`
Expected: PASS

**Step 3: Run benchmark**

Run: `dotnet run --project LancerMcp.Benchmark/LancerMcp.Benchmark.csproj`
Expected: report includes indexing time, p50/p95 latency, DB size, top-k metric
