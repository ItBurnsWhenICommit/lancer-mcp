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

        public TestOptionsMonitor(ServerOptions options)
        {
            _options = options;
        }

        public ServerOptions CurrentValue => _options;

        public ServerOptions Get(string? name) => _options;

        public IDisposable? OnChange(Action<ServerOptions, string?> listener) => null;
    }
}
