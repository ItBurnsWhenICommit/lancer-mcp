using System.Diagnostics;
using LancerMcp.Benchmarks;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tools;

namespace LancerMcp.Benchmark;

public sealed class BenchmarkBackend : IBenchmarkBackend
{
    private readonly GitTrackerService _gitTracker;
    private readonly IndexingService _indexingService;
    private readonly CodeIndexTool _codeIndexTool;
    private readonly RepositoryRepository _repositoryRepository;
    private readonly DatabaseService _databaseService;
    private readonly string _repositoryName;
    private readonly string _branchName;

    public BenchmarkBackend(
        GitTrackerService gitTracker,
        IndexingService indexingService,
        CodeIndexTool codeIndexTool,
        RepositoryRepository repositoryRepository,
        DatabaseService databaseService,
        string repositoryName,
        string branchName)
    {
        _gitTracker = gitTracker;
        _indexingService = indexingService;
        _codeIndexTool = codeIndexTool;
        _repositoryRepository = repositoryRepository;
        _databaseService = databaseService;
        _repositoryName = repositoryName;
        _branchName = branchName;
    }

    public async Task<BenchmarkIndexingStats> IndexAsync(CancellationToken cancellationToken)
    {
        var existing = await _repositoryRepository.GetByNameAsync(_repositoryName, cancellationToken);
        if (existing != null)
        {
            await _repositoryRepository.DeleteAsync(existing.Id, cancellationToken);
        }

        await _gitTracker.InitializeAsync(cancellationToken);

        var fileChanges = await _gitTracker.GetFileChangesAsync(_repositoryName, _branchName, cancellationToken);
        var process = Process.GetCurrentProcess();
        var stopwatch = Stopwatch.StartNew();
        var result = await _indexingService.IndexFilesAsync(fileChanges, cancellationToken);
        stopwatch.Stop();

        var dbSize = await _databaseService.ExecuteScalarAsync<long>(
            "SELECT pg_database_size(current_database())",
            cancellationToken: cancellationToken);

        var chunkCount = await _databaseService.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM code_chunks WHERE repo_id = @RepoId AND branch_name = @Branch",
            new { RepoId = _repositoryName, Branch = _branchName },
            cancellationToken);

        return new BenchmarkIndexingStats(
            stopwatch.ElapsedMilliseconds,
            process.PeakWorkingSet64,
            dbSize,
            result.ParsedFiles.Count,
            result.TotalSymbols,
            (int)chunkCount);
    }

    public async Task<IReadOnlyList<BenchmarkQueryExecution>> ExecuteQueriesAsync(BenchmarkQuerySet querySet, CancellationToken cancellationToken)
    {
        var executions = new List<BenchmarkQueryExecution>();

        foreach (var spec in querySet.Queries)
        {
            var stopwatch = Stopwatch.StartNew();
            var json = await _codeIndexTool.Query(_repositoryName, spec.Query, _branchName, maxResults: querySet.TopK, cancellationToken);
            stopwatch.Stop();

            var parsed = BenchmarkResponseParser.Parse(json, spec.Query, stopwatch.ElapsedMilliseconds);
            executions.Add(parsed);
        }

        return executions;
    }
}
