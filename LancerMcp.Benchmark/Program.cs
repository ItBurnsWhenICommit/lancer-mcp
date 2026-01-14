using LibGit2Sharp;
using LancerMcp.Benchmark;
using LancerMcp.Benchmarks;
using LancerMcp.Configuration;
using LancerMcp.Repositories;
using LancerMcp.Services;
using LancerMcp.Tools;
using Microsoft.Build.Locator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

if (!MSBuildLocator.IsRegistered)
{
    MSBuildLocator.RegisterDefaults();
}

var repoName = "csharp-testdata";
var branchName = "main";
var repoRoot = Directory.GetCurrentDirectory();
var testdataRoot = Path.Combine(repoRoot, "testdata", "csharp");

if (!Directory.Exists(testdataRoot))
{
    throw new DirectoryNotFoundException($"Missing testdata at {testdataRoot}");
}

var sourceRepoPath = PrepareSourceRepository(testdataRoot, branchName);
var workingDirectory = Path.Combine(Path.GetTempPath(), "lancer-benchmark", "workdir");
Directory.CreateDirectory(workingDirectory);

var serverOptions = new ServerOptions
{
    WorkingDirectory = workingDirectory,
    Repositories = new[]
    {
        new ServerOptions.RepositoryDescriptor
        {
            Name = repoName,
            RemoteUrl = sourceRepoPath,
            DefaultBranch = branchName
        }
    },
    EmbeddingServiceUrl = null,
    MaxResponseResults = 10,
    MaxResponseSnippetChars = 8000,
    MaxResponseBytes = 16384,
    DatabaseHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "localhost",
    DatabasePort = int.Parse(Environment.GetEnvironmentVariable("DB_PORT") ?? "5432"),
    DatabaseName = Environment.GetEnvironmentVariable("DB_NAME") ?? "lancer",
    DatabaseUser = Environment.GetEnvironmentVariable("DB_USER") ?? "postgres",
    DatabasePassword = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "postgres"
};

var optionsMonitor = new SimpleOptionsMonitor(serverOptions);

using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning));

var databaseService = new DatabaseService(
    loggerFactory.CreateLogger<DatabaseService>(),
    optionsMonitor);

var repositoryRepository = new RepositoryRepository(
    databaseService,
    loggerFactory.CreateLogger<RepositoryRepository>());

var branchRepository = new BranchRepository(
    databaseService,
    loggerFactory.CreateLogger<BranchRepository>());

var fileRepository = new FileRepository(
    databaseService,
    loggerFactory.CreateLogger<FileRepository>());



var symbolRepository = new SymbolRepository(
    databaseService,
    loggerFactory.CreateLogger<SymbolRepository>());

var edgeRepository = new EdgeRepository(
    databaseService,
    loggerFactory.CreateLogger<EdgeRepository>());

var chunkRepository = new CodeChunkRepository(
    databaseService,
    loggerFactory.CreateLogger<CodeChunkRepository>());

var embeddingRepository = new EmbeddingRepository(
    databaseService,
    loggerFactory.CreateLogger<EmbeddingRepository>());

var workspaceLoader = new WorkspaceLoader(loggerFactory.CreateLogger<WorkspaceLoader>());

var gitTracker = new GitTrackerService(
    loggerFactory.CreateLogger<GitTrackerService>(),
    optionsMonitor,
    repositoryRepository,
    branchRepository,
    workspaceLoader);

using var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(serverOptions.EmbeddingTimeoutSeconds)
};

var embeddingService = new EmbeddingService(
    httpClient,
    optionsMonitor,
    loggerFactory.CreateLogger<EmbeddingService>());

var indexingService = new IndexingService(
    loggerFactory.CreateLogger<IndexingService>(),
    optionsMonitor,
    databaseService,
    gitTracker,
    new LanguageDetectionService(),
    new RoslynParserService(loggerFactory.CreateLogger<RoslynParserService>()),
    new BasicParserService(loggerFactory.CreateLogger<BasicParserService>()),
    new ChunkingService(gitTracker, loggerFactory.CreateLogger<ChunkingService>(), optionsMonitor),
    embeddingService,
    workspaceLoader,
    new PersistenceService(loggerFactory.CreateLogger<PersistenceService>(), fileRepository, symbolRepository, chunkRepository),
    new EdgeResolutionService(loggerFactory.CreateLogger<EdgeResolutionService>()));

var queryOrchestrator = new QueryOrchestrator(
    loggerFactory.CreateLogger<QueryOrchestrator>(),
    chunkRepository,
    embeddingRepository,
    symbolRepository,
    edgeRepository,
    embeddingService,
    optionsMonitor);

var codeIndexTool = new CodeIndexTool(
    gitTracker,
    indexingService,
    queryOrchestrator,
    optionsMonitor,
    loggerFactory.CreateLogger<CodeIndexTool>());

var querySet = BenchmarkQuerySet.FromFile(Path.Combine(testdataRoot, "benchmarks.json"));
var backend = new BenchmarkBackend(
    gitTracker,
    indexingService,
    codeIndexTool,
    repositoryRepository,
    databaseService,
    repoName,
    branchName);

var runner = new BenchmarkRunner(backend);
var report = await runner.RunAsync(querySet, CancellationToken.None);

Console.WriteLine($"Indexing time (ms): {report.Indexing.ElapsedMs}");
Console.WriteLine($"Peak memory (bytes): {report.Indexing.PeakWorkingSetBytes}");
Console.WriteLine($"DB size (bytes): {report.Indexing.DatabaseSizeBytes}");
Console.WriteLine($"Query p50/p95 (ms): {report.QueryLatencyP50Ms}/{report.QueryLatencyP95Ms}");
Console.WriteLine($"Top-{report.TopK} hit rate: {report.TopKHitRate:P1}");

static string PrepareSourceRepository(string testdataRoot, string branchName)
{
    var repoRoot = Path.Combine(Path.GetTempPath(), "lancer-benchmark", "source", "csharp-testdata");

    if (Directory.Exists(repoRoot))
    {
        Directory.Delete(repoRoot, recursive: true);
    }

    Directory.CreateDirectory(repoRoot);
    CopyDirectory(testdataRoot, repoRoot);

    Repository.Init(repoRoot);
    using var repo = new Repository(repoRoot);

    var files = Directory.GetFiles(repoRoot, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(repoRoot, path));
    Commands.Stage(repo, files);
    var signature = new Signature("benchmark", "benchmark@local", DateTimeOffset.UtcNow);
    repo.Commit("initial benchmark corpus", signature, signature);

    var branch = repo.Branches[branchName] ?? repo.CreateBranch(branchName);
    Commands.Checkout(repo, branch);

    return repoRoot;
}

static void CopyDirectory(string source, string destination)
{
    foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
    {
        var targetDir = directory.Replace(source, destination);
        Directory.CreateDirectory(targetDir);
    }

    foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
    {
        var targetFile = file.Replace(source, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        File.Copy(file, targetFile, overwrite: true);
    }
}

internal sealed class SimpleOptionsMonitor : IOptionsMonitor<ServerOptions>
{
    private readonly ServerOptions _options;

    public SimpleOptionsMonitor(ServerOptions options)
    {
        _options = options;
    }

    public ServerOptions CurrentValue => _options;

    public ServerOptions Get(string? name) => _options;

    public IDisposable OnChange(Action<ServerOptions, string?> listener) => NoopDisposable.Instance;

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
