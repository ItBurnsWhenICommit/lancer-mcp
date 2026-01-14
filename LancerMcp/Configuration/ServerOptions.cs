namespace LancerMcp.Configuration;

/// <summary>
/// Configuration knobs for the MCP server.
/// Values can be supplied via appsettings, environment variables, or command line arguments.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>
    /// Root directory where remote repositories are cloned and indexed.
    /// </summary>
    public string WorkingDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "lancer-mcp-repositories");

    /// <summary>
    /// Repositories that can be mirrored and indexed by this server.
    /// </summary>
    public RepositoryDescriptor[] Repositories { get; set; } = Array.Empty<RepositoryDescriptor>();

    /// <summary>
    /// API key that clients must present via the Authorization header (Bearer scheme).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional name advertised over mDNS when LAN discovery is enabled.
    /// </summary>
    public string MdnsServiceName { get; set; } = "LancerMcp";

    /// <summary>
    /// Whether mDNS advertising is enabled.
    /// </summary>
    public bool EnableMdns { get; set; }

    /// <summary>
    /// Optional maximum size (in bytes) for files to index.
    /// Defaults to 1.5 MB when not set.
    /// </summary>
    public long MaxFileBytes { get; set; } = 1_500_000;

    /// <summary>
    /// Optional maximum size for tool responses.
    /// </summary>
    public int MaxResults { get; set; } = 50;

    /// <summary>
    /// Maximum number of results in query responses.
    /// </summary>
    public int MaxResponseResults { get; set; } = 10;

    /// <summary>
    /// Maximum total snippet characters across all query results.
    /// </summary>
    public int MaxResponseSnippetChars { get; set; } = 8000;

    /// <summary>
    /// Maximum serialized JSON response size in bytes.
    /// </summary>
    public int MaxResponseBytes { get; set; } = 16384;

    /// <summary>
    /// Optional override for the HTTP port. The ASP.NET host configuration controls the actual binding.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Number of concurrent file reads allowed during indexing.
    /// </summary>
    public int FileReadConcurrency { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Number of days after which unaccessed branches are considered stale and eligible for cleanup.
    /// Default is 14 days.
    /// </summary>
    public int StaleBranchDays { get; set; } = 14;

    /// <summary>
    /// Optional explicit extensions that should be indexed even if they appear binary.
    /// </summary>
    public string[] IncludeExtensions { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Folder names (relative or ancestor matches) that should be excluded from indexing.
    /// </summary>
    public string[] ExcludeFolders { get; set; } =
    {
        ".git",
        "node_modules",
        "bin",
        "obj",
        "dist",
        "build"
    };

    /// <summary>
    /// File names (relative or ancestor matches) that should be excluded from indexing.
    /// </summary>
    public string[] ExcludeFileNames { get; set; } =
    {
        "license",
        "readme"
    };

    /// <summary>
    /// File extensions (with or without leading dot) that should be excluded from indexing.
    /// </summary>
    public string[] ExcludeExtensions { get; set; } =
    {
        ".env",
        ".md",
        ".csproj",
        ".cache",
        ".nuget"
    };

    /// <summary>
    /// URL of the Text Embeddings Inference (TEI) service for generating embeddings.
    /// Example: "http://localhost:8080"
    /// </summary>
    public string? EmbeddingServiceUrl { get; set; }

    /// <summary>
    /// Model name for embeddings (informational, TEI is configured with the model at startup).
    /// </summary>
    public string EmbeddingModel { get; set; } = "jinaai/jina-embeddings-v2-base-code";

    /// <summary>
    /// Batch size for embedding generation requests.
    /// </summary>
    public int EmbeddingBatchSize { get; set; } = 32;

    /// <summary>
    /// Timeout for embedding service requests in seconds.
    /// </summary>
    public int EmbeddingTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Number of lines of context to include before each symbol when chunking.
    /// Set to 0 to disable context before symbols.
    /// Default is 5 lines (~30-60 tokens depending on line length).
    /// </summary>
    public int ChunkContextLinesBefore { get; set; } = 5;

    /// <summary>
    /// Number of lines of context to include after each symbol when chunking.
    /// Set to 0 to disable context after symbols.
    /// Default is 5 lines (~30-60 tokens depending on line length).
    /// </summary>
    public int ChunkContextLinesAfter { get; set; } = 5;

    /// <summary>
    /// Maximum chunk size in characters to stay within embedding model context window.
    /// Default is 30,000 characters (~7,500 tokens with conservative 4 chars/token estimate).
    /// Chunks exceeding this size will be truncated to fit within the 8k token limit.
    /// </summary>
    public int MaxChunkChars { get; set; } = 30_000;

    /// <summary>
    /// PostgreSQL connection string.
    /// Example: "Host=localhost;Port=5432;Database=lancer;Username=postgres;Password=postgres"
    /// </summary>
    public string? DatabaseConnectionString { get; set; }

    /// <summary>
    /// Database host (alternative to connection string).
    /// </summary>
    public string DatabaseHost { get; set; } = "localhost";

    /// <summary>
    /// Database port (alternative to connection string).
    /// </summary>
    public int DatabasePort { get; set; } = 5432;

    /// <summary>
    /// Database name (alternative to connection string).
    /// </summary>
    public string DatabaseName { get; set; } = "lancer";

    /// <summary>
    /// Database username (alternative to connection string).
    /// </summary>
    public string DatabaseUser { get; set; } = "postgres";

    /// <summary>
    /// Database password (alternative to connection string).
    /// </summary>
    public string? DatabasePassword { get; set; }

    /// <summary>
    /// Maximum number of database connections in the pool.
    /// </summary>
    public int DatabaseMaxPoolSize { get; set; } = 100;

    /// <summary>
    /// Minimum number of database connections in the pool.
    /// </summary>
    public int DatabaseMinPoolSize { get; set; } = 10;

    /// <summary>
    /// Database command timeout in seconds.
    /// </summary>
    public int DatabaseCommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets the effective connection string, either from DatabaseConnectionString or built from individual properties.
    /// </summary>
    public string GetDatabaseConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(DatabaseConnectionString))
        {
            return DatabaseConnectionString;
        }

        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = DatabaseHost,
            Port = DatabasePort,
            Database = DatabaseName,
            Username = DatabaseUser,
            Password = DatabasePassword ?? "postgres",
            MaxPoolSize = DatabaseMaxPoolSize,
            MinPoolSize = DatabaseMinPoolSize,
            CommandTimeout = DatabaseCommandTimeoutSeconds,
            Pooling = true,
            IncludeErrorDetail = true
        };

        return builder.ToString();
    }

    public sealed class RepositoryDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public string RemoteUrl { get; set; } = string.Empty;
        public string DefaultBranch { get; set; } = "main";
    }

    /// <summary>
    /// Combines folder, file, extension, and glob-based exclusions into glob patterns understood by the ignore matcher.
    /// </summary>
    public IReadOnlyList<string> ResolveExcludeGlobs()
    {
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in ExcludeFolders ?? Array.Empty<string>())
        {
            var normalized = NormalizeFolderGlob(folder);
            if (!string.IsNullOrEmpty(normalized))
            {
                patterns.Add(normalized);
            }
        }

        foreach (var fileName in ExcludeFileNames ?? Array.Empty<string>())
        {
            var normalized = NormalizeFileGlob(fileName);
            if (!string.IsNullOrEmpty(normalized))
            {
                patterns.Add(normalized);
            }
        }

        foreach (var extension in ExcludeExtensions ?? Array.Empty<string>())
        {
            var normalized = NormalizeExtensionGlob(extension);
            if (!string.IsNullOrEmpty(normalized))
            {
                patterns.Add(normalized);
            }
        }

        return patterns.ToArray();
    }

    private static string? NormalizeFolderGlob(string? value)
    {
        var trimmed = NormalizeGlob(value);
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        trimmed = trimmed.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(trimmed) ? null : $"**/{trimmed}/**";
    }

    private static string? NormalizeFileGlob(string? value)
    {
        var trimmed = NormalizeGlob(value);
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        trimmed = trimmed.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(trimmed) ? null : $"**/{trimmed}";
    }

    private static string? NormalizeExtensionGlob(string? value)
    {
        var trimmed = NormalizeGlob(value);
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        trimmed = trimmed.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : $"**/*.{trimmed}";
    }

    private static string? NormalizeGlob(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
