namespace LancerMcp.Benchmarks;

public sealed record BenchmarkIndexingStats(
    long ElapsedMs,
    long PeakWorkingSetBytes,
    long DatabaseSizeBytes,
    int FileCount,
    int SymbolCount,
    int ChunkCount);

public sealed record BenchmarkQueryExecution(
    string Query,
    long ElapsedMs,
    int JsonBytes,
    int SnippetChars,
    IReadOnlyList<string> ResultSymbols);

public sealed record BenchmarkReport(
    string Dataset,
    int TopK,
    double TopKHitRate,
    long QueryLatencyP50Ms,
    long QueryLatencyP95Ms,
    BenchmarkIndexingStats Indexing)
{
    public static BenchmarkReport Build(
        BenchmarkIndexingStats indexing,
        BenchmarkQuerySet querySet,
        IReadOnlyList<BenchmarkQueryExecution> executions)
    {
        var hits = 0;
        foreach (var spec in querySet.Queries)
        {
            var execution = executions.First(e => e.Query == spec.Query);
            if (spec.ExpectedSymbols.Any(symbol => execution.ResultSymbols.Contains(symbol)))
            {
                hits++;
            }
        }

        var latencies = executions.Select(e => e.ElapsedMs).ToArray();
        var p50 = BenchmarkStatistics.Percentile(latencies, 0.50);
        var p95 = BenchmarkStatistics.Percentile(latencies, 0.95);

        var hitRate = querySet.Queries.Count == 0 ? 0.0 : (double)hits / querySet.Queries.Count;

        return new BenchmarkReport(
            Dataset: querySet.Name,
            TopK: querySet.TopK,
            TopKHitRate: hitRate,
            QueryLatencyP50Ms: p50,
            QueryLatencyP95Ms: p95,
            Indexing: indexing);
    }
}
