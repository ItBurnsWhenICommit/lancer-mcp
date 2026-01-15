using FluentAssertions;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using LancerMcp.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LancerMcp.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class SymbolFingerprintRepositoryTests : FixtureTestBase
{
    [Fact]
    public async Task CreateBatchAndSearchCandidates_WorksForBands()
    {
        await ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS symbol_fingerprints (
                symbol_id TEXT PRIMARY KEY,
                repo_id TEXT NOT NULL,
                branch_name TEXT NOT NULL,
                commit_sha TEXT NOT NULL,
                file_path TEXT NOT NULL,
                language language NOT NULL DEFAULT 'Unknown',
                kind symbol_kind NOT NULL DEFAULT 'Unknown',
                fingerprint_kind TEXT NOT NULL,
                fingerprint BIGINT NOT NULL,
                band0 INTEGER NOT NULL,
                band1 INTEGER NOT NULL,
                band2 INTEGER NOT NULL,
                band3 INTEGER NOT NULL,
                indexed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );");

        var symbol = (await QueryAsync<Symbol>(@"
            SELECT id, repo_id AS RepositoryName, branch_name AS BranchName, commit_sha AS CommitSha,
                   file_path AS FilePath, name, qualified_name AS QualifiedName, kind, language,
                   start_line AS StartLine, start_column AS StartColumn, end_line AS EndLine,
                   end_column AS EndColumn, signature, documentation, modifiers,
                   parent_symbol_id AS ParentSymbolId, indexed_at AS IndexedAt
            FROM symbols
            WHERE language = 'CSharp' AND kind = 'Method'
            LIMIT 1")).FirstOrDefault();

        symbol.Should().NotBeNull();
        await ExecuteAsync("DELETE FROM symbol_fingerprints WHERE symbol_id = @Id", new { symbol!.Id });

        var entry = new SymbolFingerprintEntry
        {
            SymbolId = symbol!.Id,
            RepositoryName = symbol.RepositoryName,
            BranchName = symbol.BranchName,
            CommitSha = symbol.CommitSha,
            FilePath = symbol.FilePath,
            Language = symbol.Language,
            Kind = symbol.Kind,
            FingerprintKind = SimHashService.FingerprintKind,
            Fingerprint = 0x0001_0002_0003_0004UL,
            Band0 = 0x0004,
            Band1 = 0x0003,
            Band2 = 0x0002,
            Band3 = 0x0001
        };

        var serverOptions = new ServerOptions
        {
            DatabaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
            DatabasePort = int.Parse(Environment.GetEnvironmentVariable("DB_PORT") ?? "5432"),
            DatabaseName = TestDatabaseName,
            DatabaseUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
            DatabasePassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "postgres"
        };
        var optionsMonitor = new TestOptionsMonitor(serverOptions);

        var repo = new SymbolFingerprintRepository(
            new DatabaseService(NullLogger<DatabaseService>.Instance, optionsMonitor),
            NullLogger<SymbolFingerprintRepository>.Instance);

        await repo.CreateBatchAsync(new[] { entry });

        var seed = await repo.GetBySymbolIdAsync(symbol.Id);
        seed.Should().NotBeNull();

        var candidates = await repo.FindCandidatesAsync(
            symbol.RepositoryName,
            symbol.BranchName,
            symbol.Language,
            symbol.Kind,
            SimHashService.FingerprintKind,
            entry.Band0,
            entry.Band1,
            entry.Band2,
            entry.Band3,
            limit: 50);

        candidates.Should().Contain(c => c.SymbolId == symbol.Id);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
    {
        private readonly ServerOptions _options;

        public TestOptionsMonitor(ServerOptions options)
        {
            _options = options;
        }

        public ServerOptions CurrentValue => _options;

        public ServerOptions Get(string? name) => _options;

        public IDisposable? OnChange(Action<ServerOptions, string?> listener) => null;
    }
}
