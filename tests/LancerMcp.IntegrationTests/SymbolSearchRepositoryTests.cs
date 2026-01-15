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
        const string uniqueToken = "symbolsearch_unique_token_20260114";

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

        await ExecuteAsync("DELETE FROM symbol_search WHERE symbol_id = 'sym1';");

        await ExecuteAsync(@"
            INSERT INTO symbol_search (symbol_id, repo_id, branch_name, commit_sha, file_path, language, kind,
                                       name_tokens, qualified_tokens, signature_tokens, documentation_tokens, literal_tokens, snippet, search_vector)
            VALUES ('sym1', 'lancer-mcp', 'main', 'sha', 'Auth.cs', 'CSharp', 'Class',
                    @Token, @Token, 'authservice()', 'authentication', 'invalid password',
                    'public class AuthService { }',
                    setweight(to_tsvector('english', @Token), 'A'));
        ", new { Token = uniqueToken });

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

        var results = await repo.SearchAsync("lancer-mcp", uniqueToken, "main", 10);

        results.Should().Contain(r => r.SymbolId == "sym1");
    }

    [Fact]
    public async Task GetSnippetsBySymbolIdsAsync_ReturnsSnippets()
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

        await ExecuteAsync("DELETE FROM symbol_search WHERE symbol_id = 'sym_snippet';");

        await ExecuteAsync(@"
            INSERT INTO symbol_search (symbol_id, repo_id, branch_name, commit_sha, file_path, language, kind,
                                       name_tokens, qualified_tokens, signature_tokens, documentation_tokens, literal_tokens, snippet, search_vector)
            VALUES ('sym_snippet', 'lancer-mcp', 'main', 'sha', 'Snippet.cs', 'CSharp', 'Method',
                    'snippet', 'snippet', 'snippet()', 'snippet docs', 'snippet literal',
                    'public void Snippet() { }',
                    setweight(to_tsvector('english', 'snippet'), 'A'));
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

        var snippets = await repo.GetSnippetsBySymbolIdsAsync(new[] { "sym_snippet" });

        snippets.Should().ContainKey("sym_snippet");
        snippets["sym_snippet"].Should().Be("public void Snippet() { }");
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
