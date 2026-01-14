# Phase 2 Fast Symbol Retrieval Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement symbol-only Fast retrieval using a new `symbol_search` table with weighted sparse ranking, structural reranking, and compact "why returned" signals.

**Architecture:** Add a symbol_search index (keyed by symbol_id) built from tokenized symbol fields (name/qualified/signature/docs/literals) with weighted `tsvector`. Query pipeline selects a retrieval profile (default Fast) and runs symbol_search BM25-style ranking, then applies bounded structural boosts (type/member + edge expansion) before returning compact results with minimal snippets.

**Tech Stack:** .NET 9, PostgreSQL `tsvector` + GIN, Roslyn, Dapper.

---

### Task 1: Add Retrieval Profile Plumbing (Fast default)

**Files:**
- Modify: `LancerMcp/Models/QueryModels.cs`
- Modify: `LancerMcp/Configuration/ServerOptions.cs`
- Modify: `LancerMcp/Tools/CodeIndexTool.cs`
- Modify: `LancerMcp/Services/QueryOrchestrator.cs`
- Test: `LancerMcp.Tests/QueryProfileSelectionTests.cs`

**Step 1: Write the failing test (@superpowers:test-driven-development)**

```csharp
using System.Net.Http;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LancerMcp.Tests;

public sealed class QueryProfileSelectionTests
{
    [Fact]
    public async Task QueryAsync_DefaultsToFastProfile()
    {
        var searchRepo = new FakeSymbolSearchRepository();
        var orchestrator = CreateOrchestrator(searchRepo);

        var response = await orchestrator.QueryAsync(
            query: "find UserService",
            repositoryName: "repo",
            branchName: "main");

        Assert.Equal(QueryIntent.Navigation, response.Intent);
        Assert.True(searchRepo.WasCalled);
    }

    private static QueryOrchestrator CreateOrchestrator(FakeSymbolSearchRepository searchRepo)
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Fast };
        var embeddingService = new EmbeddingService(
            new HttpClient(),
            new FakeOptionsMonitor(options),
            NullLogger<EmbeddingService>.Instance);

        return new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new FakeCodeChunkRepository(),
            new FakeEmbeddingRepository(),
            new FakeSymbolRepository(),
            new FakeEdgeRepository(),
            searchRepo,
            embeddingService,
            new FakeOptionsMonitor(options));
    }

    private sealed class FakeSymbolSearchRepository : ISymbolSearchRepository
    {
        public bool WasCalled { get; private set; }

        public Task<IEnumerable<(string SymbolId, float Score)>> SearchAsync(
            string repoId,
            string query,
            string? branchName,
            int limit,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult<IEnumerable<(string, float)>>(new[] { ("sym1", 1.0f) });
        }

        public Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakeSymbolRepository : ISymbolRepository
    {
        public Task<Symbol?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<Symbol?>(new Symbol
            {
                Id = id,
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "UserService.cs",
                Name = "UserService",
                Kind = SymbolKind.Class,
                Language = Language.CSharp,
                StartLine = 1,
                StartColumn = 1,
                EndLine = 2,
                EndColumn = 1
            });

        public Task<IEnumerable<Symbol>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> SearchByNameAsync(string repoId, string query, string? branchName = null, bool fuzzy = false, int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> GetByKindAsync(string repoId, SymbolKind kind, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<Symbol> CreateAsync(Symbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(symbol);

        public Task<int> CreateBatchAsync(IEnumerable<Symbol> symbols, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakeCodeChunkRepository : ICodeChunkRepository
    {
        public Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<CodeChunk?>(null);
        public Task<IEnumerable<CodeChunk>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<IEnumerable<CodeChunk>> GetBySymbolAsync(string symbolId, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<IEnumerable<CodeChunk>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<IEnumerable<CodeChunk>> GetByLanguageAsync(string repoId, Language language, int limit = 1000, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<IEnumerable<CodeChunk>> SearchFullTextAsync(string repoId, string query, string? branchName = null, Language? language = null, int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<CodeChunk> CreateAsync(CodeChunk chunk, CancellationToken cancellationToken = default) => Task.FromResult(chunk);
        public Task<int> CreateBatchAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeEmbeddingRepository : IEmbeddingRepository
    {
        public Task<Embedding?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Embedding?>(null);
        public Task<Embedding?> GetByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default) => Task.FromResult<Embedding?>(null);
        public Task<IEnumerable<Embedding>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Embedding>>(Array.Empty<Embedding>());
        public Task<IEnumerable<(Embedding Embedding, float Distance)>> SearchBySimilarityAsync(float[] queryVector, string? repoId = null, string? branchName = null, int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<(Embedding, float)>>(Array.Empty<(Embedding, float)>());
        public Task<IEnumerable<(string ChunkId, float Score, float? BM25Score, float? VectorScore)>> HybridSearchAsync(string queryText, float[] queryVector, string? repoId = null, string? branchName = null, float bm25Weight = 0.3f, float vectorWeight = 0.7f, int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<(string, float, float?, float?)>>(Array.Empty<(string, float, float?, float?)>());
        public Task<Embedding> CreateAsync(Embedding embedding, CancellationToken cancellationToken = default) => Task.FromResult(embedding);
        public Task<int> CreateBatchAsync(IEnumerable<Embedding> embeddings, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeEdgeRepository : IEdgeRepository
    {
        public Task<SymbolEdge?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<SymbolEdge?>(null);
        public Task<IEnumerable<SymbolEdge>> GetBySourceAsync(string sourceSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<SymbolEdge>>(Array.Empty<SymbolEdge>());
        public Task<IEnumerable<SymbolEdge>> GetByTargetAsync(string targetSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<SymbolEdge>>(Array.Empty<SymbolEdge>());
        public Task<IEnumerable<SymbolEdge>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<SymbolEdge>>(Array.Empty<SymbolEdge>());
        public Task<SymbolEdge> CreateAsync(SymbolEdge edge, CancellationToken cancellationToken = default) => Task.FromResult(edge);
        public Task<int> CreateBatchAsync(IEnumerable<SymbolEdge> edges, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeOptionsMonitor : IOptionsMonitor<ServerOptions>
    {
        private readonly ServerOptions _options;
        public FakeOptionsMonitor(ServerOptions options) => _options = options;
        public ServerOptions CurrentValue => _options;
        public ServerOptions Get(string? name) => _options;
        public IDisposable? OnChange(Action<ServerOptions, string?> listener) => null;
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~QueryProfileSelectionTests`
Expected: FAIL (missing RetrievalProfile plumbing / factory).

**Step 3: Write minimal implementation**

```csharp
public enum RetrievalProfile
{
    Fast,
    Hybrid,
    Semantic
}

public sealed class ParsedQuery
{
    public RetrievalProfile Profile { get; init; } = RetrievalProfile.Fast;
}
```

Add to `ServerOptions`:

```csharp
public RetrievalProfile DefaultRetrievalProfile { get; set; } = RetrievalProfile.Fast;
```

Extend `CodeIndexTool.Query` signature:

```csharp
public async Task<string> Query(
    string repository,
    string query,
    string? branch = null,
    int? maxResults = null,
    string? profile = null,
    CancellationToken cancellationToken = default)
```

Parse profile string -> enum and pass to `QueryOrchestrator.QueryAsync(..., profileOverride: ...)`.
Update `QueryOrchestrator` constructor to accept `ISymbolSearchRepository` and `IOptionsMonitor<ServerOptions>` so it can default to the configured profile.

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~QueryProfileSelectionTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Models/QueryModels.cs LancerMcp/Configuration/ServerOptions.cs LancerMcp/Tools/CodeIndexTool.cs LancerMcp/Services/QueryOrchestrator.cs LancerMcp.Tests/QueryProfileSelectionTests.cs
git commit -m "[phase 2] add retrieval profile plumbing"
```

---

### Task 2: Tokenization Utility for Symbol Search

**Files:**
- Create: `LancerMcp/Services/SymbolTokenization.cs`
- Test: `LancerMcp.Tests/SymbolTokenizationTests.cs`

**Step 1: Write the failing test**

```csharp
using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class SymbolTokenizationTests
{
    [Fact]
    public void TokenizeIdentifier_SplitsCamelCaseAndQualifiedNames()
    {
        var tokens = SymbolTokenization.Tokenize("MyApp.Services.UserService.GetUserById");
        Assert.Contains("my", tokens);
        Assert.Contains("services", tokens);
        Assert.Contains("user", tokens);
        Assert.Contains("service", tokens);
        Assert.Contains("get", tokens);
        Assert.Contains("by", tokens);
        Assert.Contains("id", tokens);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~SymbolTokenizationTests`
Expected: FAIL (SymbolTokenization missing).

**Step 3: Write minimal implementation**

```csharp
public static class SymbolTokenization
{
    private static readonly Regex TokenPattern =
        new(@"[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+", RegexOptions.Compiled);

    public static IReadOnlyList<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var segments = Regex.Split(text, @"[^A-Za-z0-9]+");
        var tokens = new List<string>();

        foreach (var segment in segments)
        {
            foreach (Match match in TokenPattern.Matches(segment))
            {
                var token = match.Value.ToLowerInvariant();
                if (token.Length < 2)
                    continue;
                tokens.Add(token);
            }
        }

        return tokens.Distinct().ToList();
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~SymbolTokenizationTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Services/SymbolTokenization.cs LancerMcp.Tests/SymbolTokenizationTests.cs
git commit -m "[phase 2] add symbol tokenization helper"
```

---

### Task 3: Capture String Literal Tokens in Roslyn Symbols

**Files:**
- Modify: `LancerMcp/Models/Symbol.cs`
- Modify: `LancerMcp/Services/RoslynParserService.cs`
- Test: `LancerMcp.Tests/RoslynLiteralTokenTests.cs`

**Step 1: Write the failing test**

```csharp
using LancerMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LancerMcp.Tests;

public sealed class RoslynLiteralTokenTests
{
    [Fact]
    public async Task ParseFileAsync_CapturesStringLiteralTokensOnMethod()
    {
        var service = new RoslynParserService(NullLogger<RoslynParserService>.Instance);
        var code = @"
namespace Demo;
public class AuthService
{
    public void Login()
    {
        var message = \"Invalid password\";
    }
}";

        var result = await service.ParseFileAsync("repo", "main", "sha", "AuthService.cs", code);
        var login = result.Symbols.First(s => s.Name == "Login");

        Assert.Contains("invalid", login.LiteralTokens ?? Array.Empty<string>());
        Assert.Contains("password", login.LiteralTokens ?? Array.Empty<string>());
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~RoslynLiteralTokenTests`
Expected: FAIL (LiteralTokens missing).

**Step 3: Write minimal implementation**

Add to `Symbol`:

```csharp
public string[]? LiteralTokens { get; init; }
```

Update `RoslynParserService.SymbolExtractorVisitor` to collect string literals in method/constructor bodies, tokenize with `SymbolTokenization`, and assign `LiteralTokens` on the created symbol:

```csharp
private static IReadOnlyList<string> ExtractLiteralTokens(SyntaxNode node)
{
    return node.DescendantNodes()
        .OfType<LiteralExpressionSyntax>()
        .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
        .Select(l => l.Token.ValueText)
        .SelectMany(SymbolTokenization.Tokenize)
        .Distinct()
        .ToList();
}
```

Call `ExtractLiteralTokens(node.Body ?? node)` for methods/constructors (and optionally properties with initializers).

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~RoslynLiteralTokenTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Models/Symbol.cs LancerMcp/Services/RoslynParserService.cs LancerMcp.Tests/RoslynLiteralTokenTests.cs
git commit -m "[phase 2] capture string literal tokens for symbol search"
```

---

### Task 4: Build Symbol Search Entries

**Files:**
- Create: `LancerMcp/Models/SymbolSearchEntry.cs`
- Create: `LancerMcp/Services/SymbolSearchBuilder.cs`
- Test: `LancerMcp.Tests/SymbolSearchBuilderTests.cs`

**Step 1: Write the failing test**

```csharp
using System.Net.Http;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LancerMcp.Tests;

public sealed class SymbolSearchBuilderTests
{
    [Fact]
    public void BuildEntries_IncludesWeightedTokenBucketsAndSnippet()
    {
        var symbol = new Symbol
        {
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "AuthService.cs",
            Name = "AuthService",
            QualifiedName = "Demo.AuthService",
            Kind = SymbolKind.Class,
            Language = Language.CSharp,
            StartLine = 1,
            StartColumn = 1,
            EndLine = 3,
            EndColumn = 1,
            Signature = "AuthService()",
            Documentation = "Handles authentication flows.",
            LiteralTokens = new[] { "invalid", "password" }
        };

        var parsed = new ParsedFile
        {
            RepositoryName = "repo",
            BranchName = "main",
            CommitSha = "sha",
            FilePath = "AuthService.cs",
            Language = Language.CSharp,
            Symbols = new List<Symbol> { symbol },
            SourceText = "public class AuthService { }",
            Success = true
        };

        var entries = SymbolSearchBuilder.BuildEntries(parsed);
        var entry = entries.Single();

        Assert.Contains("auth", entry.NameTokens);
        Assert.Contains("demo", entry.QualifiedTokens);
        Assert.Contains("authentication", entry.DocumentationTokens);
        Assert.Contains("invalid", entry.LiteralTokens);
        Assert.False(string.IsNullOrWhiteSpace(entry.Snippet));
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~SymbolSearchBuilderTests`
Expected: FAIL (SymbolSearchBuilder missing).

**Step 3: Write minimal implementation**

```csharp
public sealed class SymbolSearchEntry
{
    public required string SymbolId { get; init; }
    public required string RepositoryName { get; init; }
    public required string BranchName { get; init; }
    public required string CommitSha { get; init; }
    public required string FilePath { get; init; }
    public required Language Language { get; init; }
    public required SymbolKind Kind { get; init; }
    public required IReadOnlyList<string> NameTokens { get; init; }
    public required IReadOnlyList<string> QualifiedTokens { get; init; }
    public required IReadOnlyList<string> SignatureTokens { get; init; }
    public required IReadOnlyList<string> DocumentationTokens { get; init; }
    public required IReadOnlyList<string> LiteralTokens { get; init; }
    public string? Snippet { get; init; }
}
```

```csharp
public static class SymbolSearchBuilder
{
    public static IReadOnlyList<SymbolSearchEntry> BuildEntries(ParsedFile parsedFile)
    {
        var entries = new List<SymbolSearchEntry>();
        foreach (var symbol in parsedFile.Symbols)
        {
            entries.Add(new SymbolSearchEntry
            {
                SymbolId = symbol.Id,
                RepositoryName = symbol.RepositoryName,
                BranchName = symbol.BranchName,
                CommitSha = symbol.CommitSha,
                FilePath = symbol.FilePath,
                Language = symbol.Language,
                Kind = symbol.Kind,
                NameTokens = SymbolTokenization.Tokenize(symbol.Name),
                QualifiedTokens = SymbolTokenization.Tokenize(symbol.QualifiedName ?? string.Empty),
                SignatureTokens = SymbolTokenization.Tokenize(symbol.Signature ?? string.Empty),
                DocumentationTokens = SymbolTokenization.Tokenize(symbol.Documentation ?? string.Empty),
                LiteralTokens = symbol.LiteralTokens ?? Array.Empty<string>(),
                Snippet = SnippetExtractor.FromSource(parsedFile.SourceText, symbol.StartLine, symbol.EndLine)
            });
        }

        return entries;
    }
}
```

Add a tiny snippet helper (inline or new static class).

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~SymbolSearchBuilderTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Models/SymbolSearchEntry.cs LancerMcp/Services/SymbolSearchBuilder.cs LancerMcp.Tests/SymbolSearchBuilderTests.cs
git commit -m "[phase 2] add symbol search entry builder"
```

---

### Task 5: Persist and Query symbol_search (Schema + Repository)

**Files:**
- Modify: `database/schema/02_tables.sql`
- Modify: `database/schema/06_performance_indexes.sql`
- Create: `LancerMcp/Repositories/ISymbolSearchRepository.cs`
- Create: `LancerMcp/Repositories/SymbolSearchRepository.cs`
- Modify: `LancerMcp/Services/PersistenceService.cs`
- Modify: `LancerMcp/Services/IndexingService.cs`
- Modify: `LancerMcp/Program.cs`
- Test: `tests/LancerMcp.IntegrationTests/SymbolSearchRepositoryTests.cs`

**Step 1: Write the failing test (integration)**

```csharp
using FluentAssertions;
using LancerMcp.Configuration;
using LancerMcp.Repositories;
using LancerMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LancerMcp.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class SymbolSearchRepositoryTests : FixtureTestBase
{
    [Fact]
    public async Task SearchAsync_ReturnsMatchesFromSymbolSearch()
    {
        await ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS symbol_search (
                symbol_id TEXT PRIMARY KEY,
                repo_id TEXT NOT NULL,
                branch_name TEXT NOT NULL,
                commit_sha TEXT NOT NULL,
                file_path TEXT NOT NULL,
                language language NOT NULL,
                kind symbol_kind NOT NULL,
                name_tokens TEXT,
                qualified_tokens TEXT,
                signature_tokens TEXT,
                documentation_tokens TEXT,
                literal_tokens TEXT,
                snippet TEXT,
                search_vector tsvector
            );");

        await ExecuteAsync(@"
            INSERT INTO symbol_search (symbol_id, repo_id, branch_name, commit_sha, file_path, language, kind,
                                       name_tokens, qualified_tokens, signature_tokens, documentation_tokens, literal_tokens, snippet, search_vector)
            VALUES ('sym1', 'lancer-mcp', 'main', 'sha', 'Auth.cs', 'CSharp', 'Class',
                    'auth service', 'demo auth service', 'authservice()', 'authentication', 'invalid password',
                    'public class AuthService { }',
                    setweight(to_tsvector('english','auth service'), 'A'));
        ");

        var serverOptions = new ServerOptions
        {
            DatabaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
            DatabasePort = int.Parse(Environment.GetEnvironmentVariable("DB_PORT") ?? "5432"),
            DatabaseName = TestDatabaseName,
            DatabaseUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
            DatabasePassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "postgres"
        };
        var optionsMonitor = new TestOptionsMonitor(serverOptions);

        var repo = new SymbolSearchRepository(
            new DatabaseService(NullLogger<DatabaseService>.Instance, optionsMonitor),
            NullLogger<SymbolSearchRepository>.Instance);

        var results = await repo.SearchAsync("lancer-mcp", "auth service", "main", 10);

        results.Should().NotBeEmpty();
        results.First().SymbolId.Should().Be("sym1");
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
    {
        private readonly ServerOptions _options;
        public TestOptionsMonitor(ServerOptions options) => _options = options;
        public ServerOptions CurrentValue => _options;
        public ServerOptions Get(string? name) => _options;
        public IDisposable? OnChange(Action<ServerOptions, string?> listener) => null;
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter Category=Integration --filter FullyQualifiedName~SymbolSearchRepositoryTests`
Expected: FAIL (repo/table/repo missing).

**Step 3: Write minimal implementation**

Schema (`database/schema/02_tables.sql`):

```sql
CREATE TABLE symbol_search (
    symbol_id TEXT PRIMARY KEY REFERENCES symbols(id) ON DELETE CASCADE,
    repo_id TEXT NOT NULL,
    branch_name TEXT NOT NULL,
    commit_sha TEXT NOT NULL,
    file_path TEXT NOT NULL,
    language language NOT NULL,
    kind symbol_kind NOT NULL,
    name_tokens TEXT,
    qualified_tokens TEXT,
    signature_tokens TEXT,
    documentation_tokens TEXT,
    literal_tokens TEXT,
    snippet TEXT,
    search_vector tsvector
);
```

Indexes (`database/schema/06_performance_indexes.sql`):

```sql
CREATE INDEX IF NOT EXISTS idx_symbol_search_repo_branch ON symbol_search(repo_id, branch_name);
CREATE INDEX IF NOT EXISTS idx_symbol_search_kind ON symbol_search(kind);
CREATE INDEX IF NOT EXISTS idx_symbol_search_vector ON symbol_search USING GIN (search_vector);
```

Repository:

```csharp
public interface ISymbolSearchRepository
{
    Task<IEnumerable<(string SymbolId, float Score)>> SearchAsync(
        string repoId, string query, string? branchName, int limit, CancellationToken cancellationToken = default);
    Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default);
}
```

Insert uses weighted buckets:

```csharp
setweight(to_tsvector('english', @NameTokens), 'A') ||
setweight(to_tsvector('english', @QualifiedTokens), 'A') ||
setweight(to_tsvector('english', @SignatureTokens), 'B') ||
setweight(to_tsvector('english', @DocumentationTokens), 'C') ||
setweight(to_tsvector('english', @LiteralTokens), 'D')
```

Wire `PersistenceService.CreateSymbolSearchBatchAsync` and call in `IndexingService.PersistToStorageAsync` after symbols.

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter Category=Integration --filter FullyQualifiedName~SymbolSearchRepositoryTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add database/schema/02_tables.sql database/schema/06_performance_indexes.sql LancerMcp/Repositories/ISymbolSearchRepository.cs LancerMcp/Repositories/SymbolSearchRepository.cs LancerMcp/Services/PersistenceService.cs LancerMcp/Services/IndexingService.cs LancerMcp/Program.cs tests/LancerMcp.IntegrationTests/SymbolSearchRepositoryTests.cs
git commit -m "[phase 2] add symbol_search schema and repository"
```

---

### Task 6: Fast Retrieval + Structural Reranking + "Why Returned"

**Files:**
- Modify: `LancerMcp/Services/QueryOrchestrator.cs`
- Modify: `LancerMcp/Models/QueryModels.cs`
- Test: `LancerMcp.Tests/FastRetrievalTests.cs`
- Test: `LancerMcp.Tests/QueryResponseCompactionTests.cs`

**Step 1: Write the failing test**

```csharp
using LancerMcp.Models;
using LancerMcp.Services;
using Xunit;

namespace LancerMcp.Tests;

public sealed class FastRetrievalTests
{
    [Fact]
    public async Task FastProfile_ReturnsSymbolResultsWithWhySignals()
    {
        var searchRepo = new FakeSymbolSearchRepository();
        var orchestrator = CreateOrchestrator(searchRepo);

        var response = await orchestrator.QueryAsync(
            query: "user service login",
            repositoryName: "repo",
            branchName: "main",
            profileOverride: RetrievalProfile.Fast);

        Assert.NotEmpty(response.Results);
        Assert.All(response.Results, r => Assert.Contains("match:", r.Reasons ?? Array.Empty<string>()));
    }

    private static QueryOrchestrator CreateOrchestrator(FakeSymbolSearchRepository searchRepo)
    {
        var options = new ServerOptions { DefaultRetrievalProfile = RetrievalProfile.Fast };
        var embeddingService = new EmbeddingService(
            new HttpClient(),
            new FakeOptionsMonitor(options),
            NullLogger<EmbeddingService>.Instance);

        return new QueryOrchestrator(
            NullLogger<QueryOrchestrator>.Instance,
            new FakeCodeChunkRepository(),
            new FakeEmbeddingRepository(),
            new FakeSymbolRepository(),
            new FakeEdgeRepository(),
            searchRepo,
            embeddingService,
            new FakeOptionsMonitor(options));
    }

    private sealed class FakeSymbolSearchRepository : ISymbolSearchRepository
    {
        public Task<IEnumerable<(string SymbolId, float Score)>> SearchAsync(
            string repoId,
            string query,
            string? branchName,
            int limit,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IEnumerable<(string, float)>>(new[] { ("sym1", 0.9f) });
        }

        public Task<int> CreateBatchAsync(IEnumerable<SymbolSearchEntry> entries, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakeSymbolRepository : ISymbolRepository
    {
        public Task<Symbol?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<Symbol?>(new Symbol
            {
                Id = id,
                RepositoryName = "repo",
                BranchName = "main",
                CommitSha = "sha",
                FilePath = "UserService.cs",
                Name = "UserService",
                QualifiedName = "Demo.UserService",
                Kind = SymbolKind.Class,
                Language = Language.CSharp,
                StartLine = 1,
                StartColumn = 1,
                EndLine = 3,
                EndColumn = 1
            });

        public Task<IEnumerable<Symbol>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> SearchByNameAsync(string repoId, string query, string? branchName = null, bool fuzzy = false, int limit = 50, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<IEnumerable<Symbol>> GetByKindAsync(string repoId, SymbolKind kind, int limit = 100, CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<Symbol>>(Array.Empty<Symbol>());

        public Task<Symbol> CreateAsync(Symbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(symbol);

        public Task<int> CreateBatchAsync(IEnumerable<Symbol> symbols, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class FakeCodeChunkRepository : ICodeChunkRepository
    {
        public Task<CodeChunk?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<CodeChunk?>(null);
        public Task<IEnumerable<CodeChunk>> GetByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<IEnumerable<CodeChunk>> GetBySymbolAsync(string symbolId, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<IEnumerable<CodeChunk>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<IEnumerable<CodeChunk>> GetByLanguageAsync(string repoId, Language language, int limit = 1000, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<IEnumerable<CodeChunk>> SearchFullTextAsync(string repoId, string query, string? branchName = null, Language? language = null, int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<CodeChunk>>(Array.Empty<CodeChunk>());
        public Task<CodeChunk> CreateAsync(CodeChunk chunk, CancellationToken cancellationToken = default) => Task.FromResult(chunk);
        public Task<int> CreateBatchAsync(IEnumerable<CodeChunk> chunks, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByFileAsync(string repoId, string branchName, string filePath, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeEmbeddingRepository : IEmbeddingRepository
    {
        public Task<Embedding?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<Embedding?>(null);
        public Task<Embedding?> GetByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default) => Task.FromResult<Embedding?>(null);
        public Task<IEnumerable<Embedding>> GetByBranchAsync(string repoId, string branchName, int limit = 1000, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<Embedding>>(Array.Empty<Embedding>());
        public Task<IEnumerable<(Embedding Embedding, float Distance)>> SearchBySimilarityAsync(float[] queryVector, string? repoId = null, string? branchName = null, int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<(Embedding, float)>>(Array.Empty<(Embedding, float)>());
        public Task<IEnumerable<(string ChunkId, float Score, float? BM25Score, float? VectorScore)>> HybridSearchAsync(string queryText, float[] queryVector, string? repoId = null, string? branchName = null, float bm25Weight = 0.3f, float vectorWeight = 0.7f, int limit = 50, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<(string, float, float?, float?)>>(Array.Empty<(string, float, float?, float?)>());
        public Task<Embedding> CreateAsync(Embedding embedding, CancellationToken cancellationToken = default) => Task.FromResult(embedding);
        public Task<int> CreateBatchAsync(IEnumerable<Embedding> embeddings, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeEdgeRepository : IEdgeRepository
    {
        public Task<SymbolEdge?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<SymbolEdge?>(null);
        public Task<IEnumerable<SymbolEdge>> GetBySourceAsync(string sourceSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<SymbolEdge>>(Array.Empty<SymbolEdge>());
        public Task<IEnumerable<SymbolEdge>> GetByTargetAsync(string targetSymbolId, EdgeKind? kind = null, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<SymbolEdge>>(Array.Empty<SymbolEdge>());
        public Task<IEnumerable<SymbolEdge>> GetByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => Task.FromResult<IEnumerable<SymbolEdge>>(Array.Empty<SymbolEdge>());
        public Task<SymbolEdge> CreateAsync(SymbolEdge edge, CancellationToken cancellationToken = default) => Task.FromResult(edge);
        public Task<int> CreateBatchAsync(IEnumerable<SymbolEdge> edges, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<int> DeleteByRepoIdAsync(string repoId, CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> DeleteByBranchAsync(string repoId, string branchName, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeOptionsMonitor : IOptionsMonitor<ServerOptions>
    {
        private readonly ServerOptions _options;
        public FakeOptionsMonitor(ServerOptions options) => _options = options;
        public ServerOptions CurrentValue => _options;
        public ServerOptions Get(string? name) => _options;
        public IDisposable? OnChange(Action<ServerOptions, string?> listener) => null;
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~FastRetrievalTests`
Expected: FAIL (Reasons missing / Fast path missing).

**Step 3: Write minimal implementation**

- Add `List<string>? Reasons` to `SearchResult`.
- Update `QueryResponse.ToOptimizedFormat` to include `why` when Reasons present (cap to 3).
- Add a `ExecuteFastSearchAsync` method:
  - query `ISymbolSearchRepository.SearchAsync`
  - fetch symbols via new `ISymbolRepository.GetByIdsAsync` (add to interface + `SymbolRepository`)
  - populate `SearchResult` with minimal snippet and reasons (matched tokens)
  - run structural reranking:
    - boost members of matching types
    - add edge neighbors with capped hops

**Step 4: Run test to verify it passes**

Run: `dotnet test LancerMcp.Tests/LancerMcp.Tests.csproj --filter FullyQualifiedName~FastRetrievalTests`
Expected: PASS.

**Step 5: Commit**

```bash
git add LancerMcp/Services/QueryOrchestrator.cs LancerMcp/Models/QueryModels.cs LancerMcp.Tests/FastRetrievalTests.cs LancerMcp.Tests/QueryResponseCompactionTests.cs
git commit -m "[phase 2] implement fast symbol-only retrieval and why signals"
```

---

### Task 7: Documentation + Changelog

**Files:**
- Modify: `docs/INDEXING_V2_OVERVIEW.md`
- Modify: `docs/RETRIEVAL_PROFILES.md`
- Modify: `docs/SCHEMA.md`
- Modify: `docs/CHANGELOG_REFACTOR.md`

**Step 1: Write the failing test**

Not applicable (docs-only change).

**Step 2: Update docs**

Add `symbol_search` table rationale, Fast profile details, and mention structural reranking + why signals.

**Step 3: Commit**

```bash
git add docs/INDEXING_V2_OVERVIEW.md docs/RETRIEVAL_PROFILES.md docs/SCHEMA.md docs/CHANGELOG_REFACTOR.md
git commit -m "[phase 2] document fast retrieval and symbol_search schema"
```

---

### Final Verification

Run:
- `dotnet test lancer-mcp.sln`
- `dotnet test --filter Category=Integration` (after `scripts/restore-fixtures.sh`)

Expected: all passing.

---

Plan complete and saved to `docs/plans/2026-01-14-phase2-fast-symbol-retrieval.md`. Two execution options:

1. Subagent-Driven (this session) - I dispatch a fresh subagent per task and review between tasks.
2. Parallel Session (separate) - Open a new session with executing-plans and run the plan with checkpoints.

Which approach?
