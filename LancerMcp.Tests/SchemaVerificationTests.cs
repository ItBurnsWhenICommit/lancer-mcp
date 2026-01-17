using Xunit;

namespace LancerMcp.Tests;

public sealed class SchemaVerificationTests
{
    [Fact]
    public void Schema_IncludesEmbeddingJobsEmbeddingDimsAndIndexes()
    {
        var root = FindRepoRoot();
        var tables = File.ReadAllText(Path.Combine(root, "database", "schema", "02_tables.sql"));
        var chunks = File.ReadAllText(Path.Combine(root, "database", "schema", "03_chunks_embeddings.sql"));
        var indexes = File.ReadAllText(Path.Combine(root, "database", "schema", "06_performance_indexes.sql"));

        Assert.Contains("CREATE TABLE embedding_jobs", tables, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADD COLUMN IF NOT EXISTS dims", chunks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idx_embedding_jobs_status_next_attempt", indexes, StringComparison.Ordinal);
        Assert.Contains("idx_embedding_jobs_status_locked_at", indexes, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "lancer-mcp.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("lancer-mcp.sln not found");
    }
}
