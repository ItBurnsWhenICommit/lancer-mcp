# Phase 0 Baseline & Benchmark Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a deterministic C# benchmark corpus, payload-size guardrails, and a hybrid benchmark CLI (index in-process, query via CodeIndexTool) without major behavior changes.

**Architecture:** Introduce benchmark-specific models/helpers in `LancerMcp/Benchmarks` (query set loader, response parser, stats/report builder, runner). The new `LancerMcp.Benchmark` console project wires real services and runs `BenchmarkRunner` against `/testdata/csharp` while queries go through `CodeIndexTool` to match MCP payloads. Query responses enforce hard caps for result count, snippet chars, and JSON bytes.

**Tech Stack:** .NET 9, xUnit, System.Text.Json, LibGit2Sharp, Dapper/Npgsql, existing LancerMcp services.

---

### Task 1: Add deterministic C# corpus + benchmark query spec (no tests)

**Files:**
- Create: `testdata/csharp/TestData.csproj`
- Create: `testdata/csharp/src/Models/User.cs`
- Create: `testdata/csharp/src/Interfaces/IUserStore.cs`
- Create: `testdata/csharp/src/Services/UserService.cs`
- Create: `testdata/csharp/src/Services/AuthService.cs`
- Create: `testdata/csharp/src/Security/PasswordHasher.cs`
- Create: `testdata/csharp/src/Security/PasswordPolicy.cs`
- Create: `testdata/csharp/benchmarks.json`

**Step 1: Create minimal C# project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

**Step 2: Add minimal, deterministic source files**

```csharp
// testdata/csharp/src/Models/User.cs
namespace TestData.Models;

public sealed record User(string Id, string Email);
```

```csharp
// testdata/csharp/src/Interfaces/IUserStore.cs
using TestData.Models;

namespace TestData.Interfaces;

public interface IUserStore
{
    User? FindByEmail(string email);
    void Save(User user);
}
```

```csharp
// testdata/csharp/src/Services/UserService.cs
using TestData.Interfaces;
using TestData.Models;

namespace TestData.Services;

public sealed class UserService
{
    private readonly IUserStore _store;

    public UserService(IUserStore store)
    {
        _store = store;
    }

    public User CreateUser(string email)
    {
        var user = new User(Guid.NewGuid().ToString("N"), email);
        _store.Save(user);
        return user;
    }

    public User? FindByEmail(string email)
    {
        return _store.FindByEmail(email);
    }
}
```

```csharp
// testdata/csharp/src/Services/AuthService.cs
using TestData.Models;
using TestData.Security;

namespace TestData.Services;

public sealed class AuthService
{
    private readonly PasswordHasher _hasher;

    public AuthService(PasswordHasher hasher)
    {
        _hasher = hasher;
    }

    public bool Login(User user, string password)
    {
        return _hasher.Verify(password, user.Email);
    }
}
```

```csharp
// testdata/csharp/src/Security/PasswordHasher.cs
namespace TestData.Security;

public sealed class PasswordHasher
{
    public string HashPassword(string password)
    {
        return $"hash:{password}";
    }

    public bool Verify(string password, string salt)
    {
        return HashPassword(password).Contains(salt, StringComparison.Ordinal);
    }
}
```

```csharp
// testdata/csharp/src/Security/PasswordPolicy.cs
namespace TestData.Security;

public static class PasswordPolicy
{
    public static bool IsStrong(string password)
    {
        return password.Length >= 12;
    }
}
```

**Step 3: Add benchmark query spec**

```json
{
  "name": "csharp-minimal",
  "topK": 5,
  "queries": [
    { "query": "find UserService class", "expectedSymbols": ["UserService"] },
    { "query": "show IUserStore interface", "expectedSymbols": ["IUserStore"] },
    { "query": "where is Login method", "expectedSymbols": ["Login"] },
    { "query": "find HashPassword method", "expectedSymbols": ["HashPassword"] },
    { "query": "find PasswordPolicy", "expectedSymbols": ["PasswordPolicy"] }
  ]
}
```

**Step 4: Commit**

```bash
git add testdata/csharp
git commit -m "[phase 0] add deterministic csharp benchmark corpus"
```

---

### Task 2: Add benchmark query set loader (TDD)

**Files:**
- Create: `LancerMcp/Benchmarks/BenchmarkQueryModels.cs`
- Test: `LancerMcp.Tests/BenchmarkQuerySetTests.cs`

**Step 1: Write failing test**

```csharp
using LancerMcp.Benchmarks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkQuerySetTests
{
    [Fact]
    public void FromJson_LoadsQueriesAndTopK()
    {
        var json = @"{\n" +
                   "  \"name\": \"csharp-minimal\",\n" +
                   "  \"topK\": 5,\n" +
                   "  \"queries\": [\n" +
                   "    { \"query\": \"find UserService class\", \"expectedSymbols\": [\"UserService\"] }\n" +
                   "  ]\n" +
                   "}";

        var set = BenchmarkQuerySet.FromJson(json);

        Assert.Equal("csharp-minimal", set.Name);
        Assert.Equal(5, set.TopK);
        Assert.Single(set.Queries);
        Assert.Equal("find UserService class", set.Queries[0].Query);
        Assert.Contains("UserService", set.Queries[0].ExpectedSymbols);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter FullyQualifiedName~BenchmarkQuerySetTests`
Expected: FAIL with missing type `BenchmarkQuerySet`.

**Step 3: Write minimal implementation**

```csharp
using System.Text.Json;

namespace LancerMcp.Benchmarks;

public sealed record BenchmarkQuerySpec(string Query, List<string> ExpectedSymbols);

public sealed class BenchmarkQuerySet
{
    public required string Name { get; init; }
    public int TopK { get; init; } = 5;
    public required List<BenchmarkQuerySpec> Queries { get; init; }

    public static BenchmarkQuerySet FromJson(string json)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var set = JsonSerializer.Deserialize<BenchmarkQuerySet>(json, options);
        if (set == null)
        {
            throw new InvalidOperationException("Invalid benchmark query set JSON");
        }
        return set;
    }

    public static BenchmarkQuerySet FromFile(string path)
    {
        var json = File.ReadAllText(path);
        return FromJson(json);
    }
}
```

**Step 4: Run tests**

Run: `dotnet test LancerMcp.Tests --filter FullyQualifiedName~BenchmarkQuerySetTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Benchmarks/BenchmarkQueryModels.cs LancerMcp.Tests/BenchmarkQuerySetTests.cs
git commit -m "[phase 0] add benchmark query set loader"
```

---

### Task 3: Add benchmark stats, report builder, and runner (TDD)

**Files:**
- Create: `LancerMcp/Benchmarks/BenchmarkStatistics.cs`
- Create: `LancerMcp/Benchmarks/BenchmarkReport.cs`
- Create: `LancerMcp/Benchmarks/BenchmarkRunner.cs`
- Test: `LancerMcp.Tests/BenchmarkStatisticsTests.cs`
- Test: `LancerMcp.Tests/BenchmarkReportTests.cs`

**Step 1: Write failing tests**

```csharp
using LancerMcp.Benchmarks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkStatisticsTests
{
    [Fact]
    public void Percentile_UsesNearestRank()
    {
        var values = new long[] { 10, 20, 30, 40, 50 };

        Assert.Equal(30, BenchmarkStatistics.Percentile(values, 0.50));
        Assert.Equal(50, BenchmarkStatistics.Percentile(values, 0.95));
    }
}
```

```csharp
using LancerMcp.Benchmarks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkReportTests
{
    [Fact]
    public void Build_ComputesHitRateAndLatencies()
    {
        var querySet = new BenchmarkQuerySet
        {
            Name = "csharp-minimal",
            TopK = 5,
            Queries = new List<BenchmarkQuerySpec>
            {
                new("find UserService class", new List<string> { "UserService" }),
                new("find HashPassword method", new List<string> { "HashPassword" })
            }
        };

        var executions = new List<BenchmarkQueryExecution>
        {
            new("find UserService class", elapsedMs: 12, jsonBytes: 1000, snippetChars: 120, resultSymbols: new[] { "UserService" }),
            new("find HashPassword method", elapsedMs: 25, jsonBytes: 900, snippetChars: 80, resultSymbols: new[] { "OtherSymbol" })
        };

        var report = BenchmarkReport.Build(
            indexing: new BenchmarkIndexingStats(elapsedMs: 100, peakWorkingSetBytes: 1000, databaseSizeBytes: 2000, fileCount: 5, symbolCount: 10, chunkCount: 8),
            querySet: querySet,
            executions: executions);

        Assert.Equal(0.5, report.TopKHitRate);
        Assert.Equal(12, report.QueryLatencyP50Ms);
        Assert.Equal(25, report.QueryLatencyP95Ms);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test LancerMcp.Tests --filter FullyQualifiedName~BenchmarkStatisticsTests|FullyQualifiedName~BenchmarkReportTests`
Expected: FAIL with missing types `BenchmarkStatistics`, `BenchmarkReport`, `BenchmarkQueryExecution`, `BenchmarkIndexingStats`.

**Step 3: Write minimal implementation**

```csharp
namespace LancerMcp.Benchmarks;

public static class BenchmarkStatistics
{
    public static long Percentile(IReadOnlyList<long> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var rank = (int)Math.Ceiling(percentile * sorted.Length);
        var index = Math.Clamp(rank - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}
```

```csharp
namespace LancerMcp.Benchmarks;

public sealed record BenchmarkIndexingStats(
    long ElapsedMs,
    long PeakWorkingSetBytes,
    long DatabaseSizeBytes,
    int FileCount,
    int SymbolCount,
    int ChunkCount);

public sealed record BenchmarkQueryExecution(
    string Query,
    long ElapsedMs,
    int JsonBytes,
    int SnippetChars,
    IReadOnlyList<string> ResultSymbols);

public sealed record BenchmarkReport(
    string Dataset,
    int TopK,
    double TopKHitRate,
    long QueryLatencyP50Ms,
    long QueryLatencyP95Ms,
    BenchmarkIndexingStats Indexing)
{
    public static BenchmarkReport Build(
        BenchmarkIndexingStats indexing,
        BenchmarkQuerySet querySet,
        IReadOnlyList<BenchmarkQueryExecution> executions)
    {
        var hits = 0;
        foreach (var spec in querySet.Queries)
        {
            var execution = executions.First(e => e.Query == spec.Query);
            if (spec.ExpectedSymbols.Any(symbol => execution.ResultSymbols.Contains(symbol)))
            {
                hits++;
            }
        }

        var latencies = executions.Select(e => e.ElapsedMs).ToArray();
        var p50 = BenchmarkStatistics.Percentile(latencies, 0.50);
        var p95 = BenchmarkStatistics.Percentile(latencies, 0.95);

        var hitRate = querySet.Queries.Count == 0 ? 0.0 : (double)hits / querySet.Queries.Count;

        return new BenchmarkReport(
            Dataset: querySet.Name,
            TopK: querySet.TopK,
            TopKHitRate: hitRate,
            QueryLatencyP50Ms: p50,
            QueryLatencyP95Ms: p95,
            Indexing: indexing);
    }
}
```

```csharp
namespace LancerMcp.Benchmarks;

public interface IBenchmarkBackend
{
    Task<BenchmarkIndexingStats> IndexAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BenchmarkQueryExecution>> ExecuteQueriesAsync(BenchmarkQuerySet querySet, CancellationToken cancellationToken);
}

public sealed class BenchmarkRunner
{
    private readonly IBenchmarkBackend _backend;

    public BenchmarkRunner(IBenchmarkBackend backend)
    {
        _backend = backend;
    }

    public async Task<BenchmarkReport> RunAsync(BenchmarkQuerySet querySet, CancellationToken cancellationToken)
    {
        var indexing = await _backend.IndexAsync(cancellationToken);
        var executions = await _backend.ExecuteQueriesAsync(querySet, cancellationToken);
        return BenchmarkReport.Build(indexing, querySet, executions);
    }
}
```

**Step 4: Run tests**

Run: `dotnet test LancerMcp.Tests --filter FullyQualifiedName~BenchmarkStatisticsTests|FullyQualifiedName~BenchmarkReportTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Benchmarks/BenchmarkStatistics.cs LancerMcp/Benchmarks/BenchmarkReport.cs LancerMcp/Benchmarks/BenchmarkRunner.cs LancerMcp.Tests/BenchmarkStatisticsTests.cs LancerMcp.Tests/BenchmarkReportTests.cs
git commit -m "[phase 0] add benchmark stats and report builder"
```

---

### Task 4: Add benchmark response parser (TDD)

**Files:**
- Create: `LancerMcp/Benchmarks/BenchmarkResponseParser.cs`
- Test: `LancerMcp.Tests/BenchmarkResponseParserTests.cs`

**Step 1: Write failing test**

```csharp
using LancerMcp.Benchmarks;
using Xunit;

namespace LancerMcp.Tests;

public sealed class BenchmarkResponseParserTests
{
    [Fact]
    public void Parse_ExtractsSymbolsAndSnippetChars()
    {
        var json = "{" +
                   "\"results\":[" +
                   "{\"symbol\":\"UserService\",\"content\":\"public class UserService {}\"}," +
                   "{\"symbol\":\"AuthService\"}" +
                   "]}";

        var parsed = BenchmarkResponseParser.Parse(json);

        Assert.Equal(2, parsed.ResultSymbols.Count);
        Assert.Contains("UserService", parsed.ResultSymbols);
        Assert.Equal("public class UserService {}".Length, parsed.SnippetChars);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter FullyQualifiedName~BenchmarkResponseParserTests`
Expected: FAIL with missing type `BenchmarkResponseParser`.

**Step 3: Write minimal implementation**

```csharp
using System.Text;
using System.Text.Json;

namespace LancerMcp.Benchmarks;

public static class BenchmarkResponseParser
{
    public static BenchmarkQueryExecution Parse(string json, string? query = null, long elapsedMs = 0)
    {
        using var document = JsonDocument.Parse(json);
        var symbols = new List<string>();
        var snippetChars = 0;

        if (document.RootElement.TryGetProperty("results", out var results))
        {
            foreach (var result in results.EnumerateArray())
            {
                if (result.TryGetProperty("symbol", out var symbol))
                {
                    var value = symbol.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        symbols.Add(value);
                    }
                }

                if (result.TryGetProperty("content", out var content))
                {
                    snippetChars += content.GetString()?.Length ?? 0;
                }
            }
        }

        var jsonBytes = Encoding.UTF8.GetByteCount(json);
        return new BenchmarkQueryExecution(query ?? string.Empty, elapsedMs, jsonBytes, snippetChars, symbols);
    }
}
```

**Step 4: Run tests**

Run: `dotnet test LancerMcp.Tests --filter FullyQualifiedName~BenchmarkResponseParserTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Benchmarks/BenchmarkResponseParser.cs LancerMcp.Tests/BenchmarkResponseParserTests.cs
git commit -m "[phase 0] add benchmark response parser"
```

---

### Task 5: Enforce payload size guardrails (TDD)

**Files:**
- Modify: `LancerMcp/Models/QueryModels.cs`
- Modify: `LancerMcp/Configuration/ServerOptions.cs`
- Modify: `LancerMcp/Tools/CodeIndexTool.cs`
- Test: `LancerMcp.Tests/QueryResponseCompactionTests.cs`

**Step 1: Write failing test**

```csharp
using System.Text;
using System.Text.Json;
using LancerMcp.Models;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryResponseCompactionTests
{
    [Fact]
    public void ToOptimizedFormat_EnforcesResultAndPayloadCaps()
    {
        var results = new List<SearchResult>();
        for (var i = 0; i < 25; i++)
        {
            results.Add(new SearchResult
            {
                Id = i.ToString(),
                Type = "code_chunk",
                Repository = "repo",
                Branch = "main",
                FilePath = "src/File.cs",
                Language = Language.CSharp,
                SymbolName = $"Symbol{i}",
                Content = new string('x', 1000),
                StartLine = 1,
                EndLine = 2,
                Score = 0.5f
            });
        }

        var response = new QueryResponse
        {
            Query = "big query",
            Intent = QueryIntent.Search,
            Results = results,
            TotalResults = results.Count,
            ExecutionTimeMs = 1
        };

        var payload = response.ToOptimizedFormat(new QueryResponseCompactionOptions
        {
            MaxResults = 10,
            MaxSnippetChars = 8000,
            MaxJsonBytes = 16384
        });

        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);

        var jsonBytes = Encoding.UTF8.GetByteCount(json);
        Assert.True(jsonBytes <= 16384, $"JSON bytes too large: {jsonBytes}");

        var resultsArray = doc.RootElement.GetProperty("results");
        Assert.True(resultsArray.GetArrayLength() <= 10);

        var snippetTotal = 0;
        foreach (var result in resultsArray.EnumerateArray())
        {
            if (result.TryGetProperty("content", out var content))
            {
                snippetTotal += content.GetString()?.Length ?? 0;
            }
        }

        Assert.True(snippetTotal <= 8000, $"Snippet chars too large: {snippetTotal}");
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests --filter FullyQualifiedName~QueryResponseCompactionTests`
Expected: FAIL with missing `QueryResponseCompactionOptions` or guardrail behavior.

**Step 3: Write minimal implementation**

```csharp
public sealed class QueryResponseCompactionOptions
{
    public int MaxResults { get; init; } = 10;
    public int MaxSnippetChars { get; init; } = 8000;
    public int MaxJsonBytes { get; init; } = 16384;
}
```

```csharp
public object ToOptimizedFormat(QueryResponseCompactionOptions? options = null)
{
    var limits = options ?? new QueryResponseCompactionOptions();
    var repository = Metadata?.TryGetValue("repository", out var repo) == true ? repo?.ToString() : "unknown";
    var branch = Metadata?.TryGetValue("branch", out var br) == true ? br?.ToString() : "unknown";

    var trimmedResults = Results.Take(limits.MaxResults).Select(r => BuildMinimalResult(r)).ToList();
    ApplySnippetBudget(trimmedResults, limits.MaxSnippetChars);

    var payload = BuildPayload(repository, branch, trimmedResults);
    payload = EnsureJsonSize(payload, repository, branch, trimmedResults, limits.MaxJsonBytes);
    return payload;
}
```

```csharp
// CodeIndexTool.cs
var options = _optionsMonitor.CurrentValue;
var optimized = queryResponse.ToOptimizedFormat(new QueryResponseCompactionOptions
{
    MaxResults = options.MaxResponseResults,
    MaxSnippetChars = options.MaxResponseSnippetChars,
    MaxJsonBytes = options.MaxResponseBytes
});
return JsonSerializer.Serialize(optimized);
```

```csharp
// ServerOptions.cs
public int MaxResponseResults { get; set; } = 10;
public int MaxResponseSnippetChars { get; set; } = 8000;
public int MaxResponseBytes { get; set; } = 16384;
```

**Step 4: Run tests**

Run: `dotnet test LancerMcp.Tests --filter FullyQualifiedName~QueryResponseCompactionTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Models/QueryModels.cs LancerMcp/Tools/CodeIndexTool.cs LancerMcp/Configuration/ServerOptions.cs LancerMcp.Tests/QueryResponseCompactionTests.cs
git commit -m "[phase 0] enforce query payload guardrails"
```

---

### Task 6: Add hybrid benchmark CLI project

**Files:**
- Create: `LancerMcp.Benchmark/LancerMcp.Benchmark.csproj`
- Create: `LancerMcp.Benchmark/Program.cs`
- Create: `LancerMcp.Benchmark/BenchmarkBackend.cs`
- Modify: `lancer-mcp.sln`

**Step 1: Create console project and add to solution**

```bash
dotnet new console -n LancerMcp.Benchmark -o LancerMcp.Benchmark

dotnet sln lancer-mcp.sln add LancerMcp.Benchmark/LancerMcp.Benchmark.csproj
```

**Step 2: Add project reference**

```xml
<ItemGroup>
  <ProjectReference Include="../LancerMcp/LancerMcp.csproj" />
</ItemGroup>
```

**Step 3: Implement benchmark backend (hybrid)**

```csharp
using System.Diagnostics;
using System.Text.Json;
using LancerMcp.Benchmarks;
using LancerMcp.Configuration;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tools;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;

namespace LancerMcp.Benchmark;

public sealed class BenchmarkBackend : IBenchmarkBackend
{
    private readonly GitTrackerService _gitTracker;
    private readonly IndexingService _indexingService;
    private readonly CodeIndexTool _codeIndexTool;
    private readonly RepositoryRepository _repositoryRepository;
    private readonly DatabaseService _databaseService;
    private readonly string _repositoryName;
    private readonly string _branchName;

    public BenchmarkBackend(
        GitTrackerService gitTracker,
        IndexingService indexingService,
        CodeIndexTool codeIndexTool,
        RepositoryRepository repositoryRepository,
        DatabaseService databaseService,
        string repositoryName,
        string branchName)
    {
        _gitTracker = gitTracker;
        _indexingService = indexingService;
        _codeIndexTool = codeIndexTool;
        _repositoryRepository = repositoryRepository;
        _databaseService = databaseService;
        _repositoryName = repositoryName;
        _branchName = branchName;
    }

    public async Task<BenchmarkIndexingStats> IndexAsync(CancellationToken cancellationToken)
    {
        var existing = await _repositoryRepository.GetByNameAsync(_repositoryName, cancellationToken);
        if (existing != null)
        {
            await _repositoryRepository.DeleteAsync(existing.Id, cancellationToken);
        }

        await _gitTracker.InitializeAsync(cancellationToken);

        var fileChanges = await _gitTracker.GetFileChangesAsync(_repositoryName, _branchName, cancellationToken);
        var process = Process.GetCurrentProcess();
        var stopwatch = Stopwatch.StartNew();
        var result = await _indexingService.IndexFilesAsync(fileChanges, cancellationToken);
        stopwatch.Stop();

        var dbSize = await _databaseService.ExecuteScalarAsync<long>("SELECT pg_database_size(current_database())", cancellationToken: cancellationToken);

        return new BenchmarkIndexingStats(
            elapsedMs: stopwatch.ElapsedMilliseconds,
            peakWorkingSetBytes: process.PeakWorkingSet64,
            databaseSizeBytes: dbSize,
            fileCount: result.ParsedFiles.Count,
            symbolCount: result.TotalSymbols,
            chunkCount: result.ParsedFiles.Sum(f => f.Chunks.Count));
    }

    public async Task<IReadOnlyList<BenchmarkQueryExecution>> ExecuteQueriesAsync(BenchmarkQuerySet querySet, CancellationToken cancellationToken)
    {
        var executions = new List<BenchmarkQueryExecution>();
        foreach (var spec in querySet.Queries)
        {
            var stopwatch = Stopwatch.StartNew();
            var json = await _codeIndexTool.Query(_repositoryName, spec.Query, _branchName, maxResults: 10, cancellationToken);
            stopwatch.Stop();

            var parsed = BenchmarkResponseParser.Parse(json, spec.Query, stopwatch.ElapsedMilliseconds);
            executions.Add(parsed);
        }

        return executions;
    }
}
```

**Step 4: Implement benchmark program**

```csharp
using LancerMcp.Benchmarks;
using LancerMcp.Configuration;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tools;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;

if (!MSBuildLocator.IsRegistered)
{
    MSBuildLocator.RegisterDefaults();
}

var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
var logger = loggerFactory.CreateLogger("Benchmark");

var repoName = "csharp-testdata";
var branchName = "main";

var testdataRoot = Path.Combine(Directory.GetCurrentDirectory(), "testdata", "csharp");
if (!Directory.Exists(testdataRoot))
{
    throw new DirectoryNotFoundException($"Missing testdata at {testdataRoot}");
}

var workingDirectory = Path.Combine(Path.GetTempPath(), "lancer-benchmark", "workdir");
Directory.CreateDirectory(workingDirectory);

var options = new ServerOptions
{
    WorkingDirectory = workingDirectory,
    Repositories = new[]
    {
        new ServerOptions.RepositoryDescriptor
        {
            Name = repoName,
            RemoteUrl = testdataRoot,
            DefaultBranch = branchName
        }
    },
    EmbeddingServiceUrl = null
};

var optionsMonitor = new TestOptionsMonitor(options);

var databaseService = new DatabaseService(loggerFactory.CreateLogger<DatabaseService>(), optionsMonitor);
var repositoryRepository = new RepositoryRepository(databaseService, loggerFactory.CreateLogger<RepositoryRepository>());
var branchRepository = new BranchRepository(databaseService, loggerFactory.CreateLogger<BranchRepository>());
var commitRepository = new CommitRepository(databaseService, loggerFactory.CreateLogger<CommitRepository>());
var fileRepository = new FileRepository(databaseService, loggerFactory.CreateLogger<FileRepository>());
var symbolRepository = new SymbolRepository(databaseService, loggerFactory.CreateLogger<SymbolRepository>());
var edgeRepository = new EdgeRepository(databaseService, loggerFactory.CreateLogger<EdgeRepository>());
var chunkRepository = new CodeChunkRepository(databaseService, loggerFactory.CreateLogger<CodeChunkRepository>());
var embeddingRepository = new EmbeddingRepository(databaseService, loggerFactory.CreateLogger<EmbeddingRepository>());

var gitTracker = new GitTrackerService(loggerFactory.CreateLogger<GitTrackerService>(), optionsMonitor, repositoryRepository, branchRepository, new WorkspaceLoader(loggerFactory.CreateLogger<WorkspaceLoader>()));
var indexingService = new IndexingService(
    loggerFactory.CreateLogger<IndexingService>(),
    optionsMonitor,
    databaseService,
    gitTracker,
    new LanguageDetectionService(),
    new RoslynParserService(loggerFactory.CreateLogger<RoslynParserService>()),
    new BasicParserService(loggerFactory.CreateLogger<BasicParserService>()),
    new ChunkingService(loggerFactory.CreateLogger<ChunkingService>(), optionsMonitor),
    new EmbeddingService(new HttpClient(), optionsMonitor, loggerFactory.CreateLogger<EmbeddingService>()),
    new WorkspaceLoader(loggerFactory.CreateLogger<WorkspaceLoader>()),
    new PersistenceService(databaseService, loggerFactory.CreateLogger<PersistenceService>()),
    new EdgeResolutionService(loggerFactory.CreateLogger<EdgeResolutionService>(), symbolRepository, edgeRepository));

var queryOrchestrator = new QueryOrchestrator(
    loggerFactory.CreateLogger<QueryOrchestrator>(),
    chunkRepository,
    embeddingRepository,
    symbolRepository,
    edgeRepository,
    new EmbeddingService(new HttpClient(), optionsMonitor, loggerFactory.CreateLogger<EmbeddingService>()));

var codeIndexTool = new CodeIndexTool(gitTracker, indexingService, queryOrchestrator, loggerFactory.CreateLogger<CodeIndexTool>());

var querySet = BenchmarkQuerySet.FromFile(Path.Combine(testdataRoot, "benchmarks.json"));
var backend = new BenchmarkBackend(gitTracker, indexingService, codeIndexTool, repositoryRepository, databaseService, repoName, branchName);
var runner = new BenchmarkRunner(backend);

var report = await runner.RunAsync(querySet, CancellationToken.None);

Console.WriteLine($"Indexing time (ms): {report.Indexing.ElapsedMs}");
Console.WriteLine($"DB size (bytes): {report.Indexing.DatabaseSizeBytes}");
Console.WriteLine($"Query p50/p95 (ms): {report.QueryLatencyP50Ms}/{report.QueryLatencyP95Ms}");
Console.WriteLine($"Top-{report.TopK} hit rate: {report.TopKHitRate:P1}");
```

**Step 5: Commit**

```bash
git add LancerMcp.Benchmark LancerMcp.Benchmark/LancerMcp.Benchmark.csproj lancer-mcp.sln
git commit -m "[phase 0] add hybrid benchmark cli"
```

---

### Task 7: Add docs + changelog (no tests)

**Files:**
- Create: `docs/BENCHMARKS.md`
- Create: `docs/INDEXING_V2_OVERVIEW.md`
- Create: `docs/RETRIEVAL_PROFILES.md`
- Create: `docs/SCHEMA.md`
- Create: `docs/CHANGELOG_REFACTOR.md`

**Step 1: Write docs skeletons and benchmark instructions**

```markdown
# Benchmarks

## Command

```bash
dotnet run --project LancerMcp.Benchmark/LancerMcp.Benchmark.csproj
```

## Metrics
- Indexing time (ms)
- Peak memory (best-effort)
- DB size (bytes)
- Query p50/p95 (ms)
- Top-k hit rate

## Baseline (Phase 0)
- TBD (run benchmark to populate)
```

```markdown
# INDEXING_V2 Overview

## Goals
- Symbol-first indexing for C#
- Sparse retrieval + structural reranking
- Compact, high-signal payloads

## Phases
- Phase 0: benchmarks + safety rails (this document)
- Phase 1: symbol-first C# index
- Phase 2: sparse retrieval + structural reranking
- Phase 3: similarity without embeddings
- Phase 4: optional embeddings
```

```markdown
# Retrieval Profiles

## Fast (default)
- Sparse/BM25 + structural signals
- No embeddings required

## Hybrid
- Fast + optional embeddings

## Semantic
- Embeddings-first, fallback to Fast
```

```markdown
# Schema

## Tables
- repos
- branches
- commits
- files
- symbols
- edges
- code_chunks
- embeddings

## Rationale
(Explain each table and key columns)
```

```markdown
# Refactor Changelog

## [phase 0] baseline & safety rails
- added benchmark CLI + deterministic C# corpus
- added payload guardrails
- added benchmark docs
```

**Step 2: Commit**

```bash
git add docs/BENCHMARKS.md docs/INDEXING_V2_OVERVIEW.md docs/RETRIEVAL_PROFILES.md docs/SCHEMA.md docs/CHANGELOG_REFACTOR.md
git commit -m "[phase 0] add benchmark docs and phase skeletons"
```

---

### Task 8: Verification + baseline numbers

**Step 1: Run unit tests**

Run: `dotnet test LancerMcp.Tests`
Expected: PASS.

**Step 2: Run benchmark**

Run: `dotnet run --project LancerMcp.Benchmark/LancerMcp.Benchmark.csproj`
Expected: metrics printed (indexing time, db size, query p50/p95, hit rate).

**Step 3: Update docs/BENCHMARKS.md with baseline numbers**

**Step 4: Commit baseline update**

```bash
git add docs/BENCHMARKS.md
git commit -m "[phase 0] record benchmark baseline"
```
