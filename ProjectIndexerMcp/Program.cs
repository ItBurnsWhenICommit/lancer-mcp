using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
using ProjectIndexerMcp.Repositories;
using ProjectIndexerMcp.Services;

var builder = WebApplication.CreateBuilder(args);

// ----- Configuration loading & validation -----
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables("PROJECT_INDEXER_MCP_")
    .AddCommandLine(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["--workdir"] = nameof(ServerOptions.WorkingDirectory),
        ["--apikey"] = nameof(ServerOptions.ApiKey),
        ["--max-file-bytes"] = nameof(ServerOptions.MaxFileBytes),
        ["--max-results"] = nameof(ServerOptions.MaxResults)
    });

builder.Services
    .AddOptions<ServerOptions>()
    .Bind(builder.Configuration)
    .Validate(options => !string.IsNullOrWhiteSpace(options.WorkingDirectory),
        "WorkingDirectory must be provided via --workdir or configuration.")
    .Validate(options => options.Repositories is { Length: > 0 },
        "At least one repository must be configured.")
    .Validate(options => options.Repositories.All(r =>
        !string.IsNullOrWhiteSpace(r.Name) && !string.IsNullOrWhiteSpace(r.RemoteUrl)),
        "Each repository must specify a non-empty name and remote URL.")
    .Validate(options => string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Length >= 12,
        "ApiKey must be at least 12 characters when set.")
    .ValidateOnStart();

builder.Services.PostConfigure<ServerOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(options.WorkingDirectory) && !Path.IsPathRooted(options.WorkingDirectory))
    {
        options.WorkingDirectory = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, options.WorkingDirectory));
    }
});
// ----- End configuration section -----

// ----- Service registration -----
// Database services
builder.Services.AddSingleton<DatabaseService>();

// Repository services
builder.Services.AddSingleton<IRepositoryRepository, RepositoryRepository>();
builder.Services.AddSingleton<IBranchRepository, BranchRepository>();
builder.Services.AddSingleton<ICommitRepository, CommitRepository>();
builder.Services.AddSingleton<IFileRepository, FileRepository>();
builder.Services.AddSingleton<ISymbolRepository, SymbolRepository>();
builder.Services.AddSingleton<IEdgeRepository, EdgeRepository>();
builder.Services.AddSingleton<ICodeChunkRepository, CodeChunkRepository>();
builder.Services.AddSingleton<IEmbeddingRepository, EmbeddingRepository>();

// Application services
builder.Services.AddSingleton<GitTrackerService>();
builder.Services.AddSingleton<LanguageDetectionService>();
builder.Services.AddSingleton<RoslynParserService>();
builder.Services.AddSingleton<BasicParserService>();
builder.Services.AddSingleton<ChunkingService>();
builder.Services.AddSingleton<QueryOrchestrator>();

// Configure HttpClient for EmbeddingService with proper BaseAddress and Timeout
builder.Services.AddHttpClient<EmbeddingService>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptionsMonitor<ServerOptions>>();
    var embeddingUrl = options.CurrentValue.EmbeddingServiceUrl;

    if (!string.IsNullOrEmpty(embeddingUrl))
    {
        client.BaseAddress = new Uri(embeddingUrl);
    }

    client.Timeout = TimeSpan.FromSeconds(options.CurrentValue.EmbeddingTimeoutSeconds);
});

builder.Services.AddSingleton<IndexingService>();
builder.Services.AddHostedService<GitTrackerHostedService>();
builder.Services.AddHostedService<BranchCleanupHostedService>();
// ----- End service registration -----

builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

var optionsMonitor = app.Services.GetRequiredService<IOptionsMonitor<ServerOptions>>(); // Allows settings hot reload

// ----- Authentication check on request -----
app.Use(async (context, next) =>
{
    var opts = optionsMonitor.CurrentValue;

    if (!string.IsNullOrWhiteSpace(opts.ApiKey))
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            !authHeader.ToString().Equals($"Bearer {opts.ApiKey}", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid API key.");
            return;
        }
    }

    await next().ConfigureAwait(false);
});
// ----- End authentication check -----

app.MapMcp();
app.MapGet("/", () => "Project Indexer MCP Server - Use the 'Query' tool to search code.");
app.Run();
