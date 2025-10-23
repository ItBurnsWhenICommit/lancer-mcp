using ProjectIndexerMcp.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables("PROJECT_INDEXER_MCP_")
    .AddCommandLine(args, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["--root"] = nameof(ServerOptions.ProjectRoot),
        ["--apikey"] = nameof(ServerOptions.ApiKey),
        ["--origin"] = $"{nameof(ServerOptions.AllowedOrigins)}:0",
        ["--max-file-bytes"] = nameof(ServerOptions.MaxFileBytes),
        ["--max-results"] = nameof(ServerOptions.MaxResults)
    });

builder.Services
    .AddOptions<ServerOptions>()
    .Bind(builder.Configuration)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ProjectRoot),
        "ProjectRoot must be provided via --root or configuration.")
    .Validate(options => string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Length >= 10,
        "ApiKey must be at least 10 characters when set.")
    .ValidateOnStart();

builder.Services.PostConfigure<ServerOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(options.ProjectRoot) && !Path.IsPathRooted(options.ProjectRoot))
    {
        options.ProjectRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, options.ProjectRoot));
    }
});



var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
