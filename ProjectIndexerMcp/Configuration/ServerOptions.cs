namespace ProjectIndexerMcp.Configuration;

/// <summary>
/// Configuration knobs for the MCP server.
/// Values can be supplied via appsettings, environment variables, or command line arguments.
/// </summary>
public sealed class ServerOptions
{
    /// <summary>
    /// Root directory where remote repositories are cloned and indexed.
    /// </summary>
    public string WorkingDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "repositories");

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
    public string MdnsServiceName { get; set; } = "ProjectIndexerMcp";

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
