using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
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
builder.Services.AddSingleton<GitTrackerService>();
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
