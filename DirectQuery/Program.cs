using LancerMcp.Configuration;
using LancerMcp.Repositories;
using LancerMcp.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DirectQuery;

/// <summary>
/// Direct query tool that bypasses MCP protocol and calls QueryOrchestrator directly.
/// Outputs raw JSON response that the MCP server would send.
/// </summary>
class Program
{
    // ANSI color codes
    private const string ColorReset = "\x1b[0m";
    private const string ColorRed = "\x1b[31m";
    private const string ColorYellow = "\x1b[33m";
    private const string ColorGreen = "\x1b[32m";
    private const string ColorCyan = "\x1b[36m";
    private const string ColorGray = "\x1b[90m";

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("Lancer MCP - Direct Query Tool");
        Console.WriteLine("===============================");
        Console.WriteLine();

        // Parse arguments
        var query = args.Length > 0 ? string.Join(" ", args) : "Where is the QueryOrchestrator class?";
        var repository = "lancer-mcp";
        var maxResults = 50;

        WriteInfo($"Query: {query}");
        WriteInfo($"Repository: {repository}");
        WriteInfo($"Max Results: {maxResults}");
        Console.WriteLine();

        try
        {
            // Setup configuration
            var serverOptions = new ServerOptions
            {
                DatabaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
                DatabasePort = int.Parse(Environment.GetEnvironmentVariable("DB_PORT") ?? "5432"),
                DatabaseName = Environment.GetEnvironmentVariable("DB_NAME") ?? "lancer",
                DatabaseUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
                DatabasePassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "postgres",
                EmbeddingServiceUrl = Environment.GetEnvironmentVariable("EMBEDDING_SERVICE_URL") ?? "http://localhost:8080",
                EmbeddingBatchSize = 32,
                EmbeddingTimeoutSeconds = 60,
                MaxResults = maxResults
            };

            var optionsMonitor = new TestOptionsMonitor(serverOptions);

            // Setup logging
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning); // Only show warnings and errors
            });

            WriteInfo("Initializing services...");

            // Create database service
            var databaseService = new DatabaseService(
                loggerFactory.CreateLogger<DatabaseService>(),
                optionsMonitor
            );

            // Create repositories (DatabaseService first, then Logger)
            var repositoryRepository = new RepositoryRepository(
                databaseService,
                loggerFactory.CreateLogger<RepositoryRepository>()
            );

            var symbolRepository = new SymbolRepository(
                databaseService,
                loggerFactory.CreateLogger<SymbolRepository>()
            );

            var symbolSearchRepository = new SymbolSearchRepository(
                databaseService,
                loggerFactory.CreateLogger<SymbolSearchRepository>()
            );

            var edgeRepository = new EdgeRepository(
                databaseService,
                loggerFactory.CreateLogger<EdgeRepository>()
            );

            var chunkRepository = new CodeChunkRepository(
                databaseService,
                loggerFactory.CreateLogger<CodeChunkRepository>()
            );

            var embeddingRepository = new EmbeddingRepository(
                databaseService,
                loggerFactory.CreateLogger<EmbeddingRepository>()
            );

            // Create HTTP client for embedding service
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(serverOptions.EmbeddingServiceUrl),
                Timeout = TimeSpan.FromSeconds(serverOptions.EmbeddingTimeoutSeconds)
            };

            var embeddingService = new EmbeddingService(
                httpClient,
                optionsMonitor,
                loggerFactory.CreateLogger<EmbeddingService>()
            );

            // Create QueryOrchestrator
            var queryOrchestrator = new QueryOrchestrator(
                loggerFactory.CreateLogger<QueryOrchestrator>(),
                chunkRepository,
                embeddingRepository,
                symbolRepository,
                symbolSearchRepository,
                edgeRepository,
                embeddingService,
                optionsMonitor
            );

            WriteSuccess("Services initialized");
            Console.WriteLine();

            // Get repository to determine default branch
            var repo = await repositoryRepository.GetByNameAsync(repository, CancellationToken.None);
            if (repo == null)
            {
                WriteError($"Repository '{repository}' not found in database");
                return 1;
            }

            var branchName = repo.DefaultBranch;
            WriteInfo($"Using branch: {branchName}");
            Console.WriteLine();
            WriteInfo("Executing query...");
            Console.WriteLine();

            // Execute query
            var response = await queryOrchestrator.QueryAsync(
                query,
                repository,
                branchName: branchName,
                maxResults: maxResults,
                cancellationToken: CancellationToken.None
            );

            // Output optimized JSON response (what MCP server would send)
            Console.WriteLine();
            WriteSuccess("Query completed successfully");
            Console.WriteLine();
            WriteInfo("Optimized JSON Response:");
            Console.WriteLine("========================");
            Console.WriteLine();

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Use the same optimized format as the MCP tool
            var optimizedResponse = response.ToOptimizedFormat();
            var json = JsonSerializer.Serialize(optimizedResponse, jsonOptions);
            Console.WriteLine(json);
            Console.WriteLine();

            if (response.Results.Count == 0)
            {
                WriteWarning("No results found");
                Console.WriteLine();
                WriteInfo("Possible reasons:");
                Console.WriteLine("  1. The repository hasn't been indexed yet");
                Console.WriteLine("  2. The query doesn't match any indexed code");
                Console.WriteLine("  3. The database is empty");
                Console.WriteLine();
                WriteInfo("To index the repository, run the MCP server:");
                Console.WriteLine("  dotnet run --project LancerMcp/LancerMcp.csproj");
                return 1;
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            WriteError("Error executing query:");
            WriteError($"  {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine();

            if (ex.InnerException != null)
            {
                WriteError("Inner exception:");
                WriteError($"  {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Console.WriteLine();
            }

            WriteError("Stack trace:");
            Console.WriteLine(ex.StackTrace);
            Console.WriteLine();

            WriteWarning("Troubleshooting:");
            Console.WriteLine("  1. Is PostgreSQL running?");
            Console.WriteLine("     docker compose up -d");
            Console.WriteLine("  2. Is the database initialized?");
            Console.WriteLine("     Check that the 'lancer' database exists");
            Console.WriteLine("  3. Is the embedding service running?");
            Console.WriteLine("     docker compose up -d");
            Console.WriteLine();

            return 1;
        }
    }

    private static void WriteInfo(string message)
    {
        Console.WriteLine($"{ColorCyan}{message}{ColorReset}");
    }

    private static void WriteSuccess(string message)
    {
        Console.WriteLine($"{ColorGreen}{message}{ColorReset}");
    }

    private static void WriteWarning(string message)
    {
        Console.WriteLine($"{ColorYellow}{message}{ColorReset}");
    }

    private static void WriteError(string message)
    {
        Console.WriteLine($"{ColorRed}{message}{ColorReset}");
    }
}

/// <summary>
/// Simple IOptionsMonitor implementation for testing.
/// </summary>
class TestOptionsMonitor : IOptionsMonitor<ServerOptions>
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
