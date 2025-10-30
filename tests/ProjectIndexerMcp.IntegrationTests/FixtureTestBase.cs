using System.Diagnostics;
using Dapper;
using Npgsql;
using ProjectIndexerMcp.Services;

namespace ProjectIndexerMcp.IntegrationTests;

/// <summary>
/// Base class for integration tests that use pre-indexed fixtures.
/// Provides helper methods for database access and verification.
/// </summary>
public abstract class FixtureTestBase : IDisposable
{
    protected string TestDatabaseName { get; }
    protected string TestWorkingDirectory { get; }
    protected string ConnectionString { get; }
    
    private readonly string _masterConnectionString;
    private bool _disposed;

    protected FixtureTestBase()
    {
        // Read from environment variables (set by restore-fixtures.sh)
        TestDatabaseName = Environment.GetEnvironmentVariable("TEST_DB_NAME") 
            ?? "project_indexer_test";
        
        TestWorkingDirectory = Environment.GetEnvironmentVariable("TEST_WORKING_DIR") 
            ?? throw new InvalidOperationException(
                "TEST_WORKING_DIR environment variable not set. Run scripts/restore-fixtures.sh first.");

        var dbHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost";
        var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
        var dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres";
        var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "postgres";

        ConnectionString = $"Host={dbHost};Port={dbPort};Database={TestDatabaseName};Username={dbUser};Password={dbPassword}";
        _masterConnectionString = $"Host={dbHost};Port={dbPort};Database=postgres;Username={dbUser};Password={dbPassword}";
    }

    /// <summary>
    /// Executes a SQL query and returns the results.
    /// </summary>
    protected async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.QueryAsync<T>(sql, parameters);
    }

    /// <summary>
    /// Executes a SQL command and returns the number of affected rows.
    /// </summary>
    protected async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>
    /// Executes a SQL query and returns a single scalar value.
    /// </summary>
    protected async Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteScalarAsync<T>(sql, parameters);
    }

    /// <summary>
    /// Verifies that the database contains expected data.
    /// </summary>
    protected async Task VerifyDatabaseHasData()
    {
        var symbolCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM symbols");
        var chunkCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM code_chunks");
        var embeddingCount = await ExecuteScalarAsync<int>("SELECT COUNT(*) FROM embeddings");

        if (symbolCount == 0)
        {
            throw new InvalidOperationException(
                "Database has no symbols. Fixtures may not be properly restored. " +
                "Run scripts/restore-fixtures.sh to restore test data.");
        }

        Console.WriteLine($"Database statistics:");
        Console.WriteLine($"  Symbols: {symbolCount}");
        Console.WriteLine($"  Code chunks: {chunkCount}");
        Console.WriteLine($"  Embeddings: {embeddingCount}");
    }

    /// <summary>
    /// Gets the path to a Git mirror in the test working directory.
    /// </summary>
    protected string GetGitMirrorPath(string repoName)
    {
        return Path.Combine(TestWorkingDirectory, $"{repoName}.git");
    }

    /// <summary>
    /// Verifies that a Git mirror exists in the test working directory.
    /// </summary>
    protected void VerifyGitMirrorExists(string repoName)
    {
        var mirrorPath = GetGitMirrorPath(repoName);
        if (!Directory.Exists(mirrorPath))
        {
            throw new InvalidOperationException(
                $"Git mirror not found: {mirrorPath}. " +
                "Run scripts/restore-fixtures.sh to restore test data.");
        }
    }

    /// <summary>
    /// Runs a shell command and returns the output.
    /// </summary>
    protected async Task<string> RunCommandAsync(string command, string workingDirectory)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"{command}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to start process: {command}");
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command failed with exit code {process.ExitCode}: {command}\n" +
                $"Error: {error}");
        }

        return output;
    }

    /// <summary>
    /// Gets database statistics for a specific repository.
    /// </summary>
    protected async Task<RepositoryStats> GetRepositoryStatsAsync(string repoName)
    {
        var sql = @"
            SELECT 
                COUNT(DISTINCT s.id) as SymbolCount,
                COUNT(DISTINCT c.id) as ChunkCount,
                COUNT(DISTINCT e.id) as EmbeddingCount,
                COUNT(DISTINCT f.id) as FileCount,
                COUNT(DISTINCT edge.id) as EdgeCount
            FROM symbols s
            LEFT JOIN code_chunks c ON c.symbol_id = s.id
            LEFT JOIN embeddings e ON e.chunk_id = c.id
            LEFT JOIN files f ON f.repo_id = s.repo_id AND f.file_path = s.file_path
            LEFT JOIN edges edge ON edge.source_symbol_id = s.id
            WHERE s.repo_id = @RepoName";

        var result = await QueryAsync<RepositoryStats>(sql, new { RepoName = repoName });
        return result.FirstOrDefault() ?? new RepositoryStats();
    }

    public virtual void Dispose()
    {
        if (_disposed)
            return;

        // Cleanup is handled by the cleanup.sh script generated by restore-fixtures.sh
        // We don't automatically clean up here to allow inspection after test failures
        
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Statistics for a repository in the database.
/// </summary>
public record RepositoryStats
{
    public int SymbolCount { get; init; }
    public int ChunkCount { get; init; }
    public int EmbeddingCount { get; init; }
    public int FileCount { get; init; }
    public int EdgeCount { get; init; }
}

