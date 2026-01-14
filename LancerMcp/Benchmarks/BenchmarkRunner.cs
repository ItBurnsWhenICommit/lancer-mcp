namespace LancerMcp.Benchmarks;

public interface IBenchmarkBackend
{
    Task<BenchmarkIndexingStats> IndexAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<BenchmarkQueryExecution>> ExecuteQueriesAsync(BenchmarkQuerySet querySet, CancellationToken cancellationToken);
}

public sealed class BenchmarkRunner
{
    private readonly IBenchmarkBackend _backend;

    public BenchmarkRunner(IBenchmarkBackend backend)
    {
        _backend = backend;
    }

    public async Task<BenchmarkReport> RunAsync(BenchmarkQuerySet querySet, CancellationToken cancellationToken)
    {
        var indexing = await _backend.IndexAsync(cancellationToken);
        var executions = await _backend.ExecuteQueriesAsync(querySet, cancellationToken);
        return BenchmarkReport.Build(indexing, querySet, executions);
    }
}
