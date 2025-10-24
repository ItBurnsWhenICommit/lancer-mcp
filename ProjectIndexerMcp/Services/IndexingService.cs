using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
using ProjectIndexerMcp.Models;

namespace ProjectIndexerMcp.Services;

/// <summary>
/// Orchestrates the indexing pipeline: language detection, parsing, and symbol extraction.
/// </summary>
public sealed class IndexingService
{
    private readonly ILogger<IndexingService> _logger;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly GitTrackerService _gitTracker;
    private readonly LanguageDetectionService _languageDetection;
    private readonly RoslynParserService _roslynParser;
    private readonly BasicParserService _basicParser;
    private readonly SemaphoreSlim _concurrencyLimiter;

    public IndexingService(
        ILogger<IndexingService> logger,
        IOptionsMonitor<ServerOptions> options,
        GitTrackerService gitTracker,
        LanguageDetectionService languageDetection,
        RoslynParserService roslynParser,
        BasicParserService basicParser)
    {
        _logger = logger;
        _options = options;
        _gitTracker = gitTracker;
        _languageDetection = languageDetection;
        _roslynParser = roslynParser;
        _basicParser = basicParser;

        var concurrency = options.CurrentValue.FileReadConcurrency;
        _concurrencyLimiter = new SemaphoreSlim(concurrency, concurrency);
    }

    /// <summary>
    /// Indexes a batch of file changes and automatically marks the branch as indexed.
    /// </summary>
    public async Task<IndexingResult> IndexFilesAsync(IEnumerable<FileChange> fileChanges, CancellationToken cancellationToken = default)
    {
        var result = new IndexingResult();
        var tasks = new List<Task<ParsedFile?>>();
        var fileChangesList = fileChanges.ToList();

        foreach (var fileChange in fileChangesList)
        {
            // Skip deleted files
            if (fileChange.ChangeType == ChangeType.Deleted)
            {
                result.DeletedFiles.Add(fileChange.FilePath);
                continue;
            }

            tasks.Add(IndexFileAsync(fileChange, cancellationToken));
        }

        var parsedFiles = await Task.WhenAll(tasks);

        foreach (var parsedFile in parsedFiles)
        {
            if (parsedFile == null)
            {
                result.SkippedFiles++;
                continue;
            }

            if (parsedFile.Success)
            {
                result.ParsedFiles.Add(parsedFile);
                result.TotalSymbols += parsedFile.Symbols.Count;
                result.TotalEdges += parsedFile.Edges.Count;
            }
            else
            {
                result.FailedFiles++;
                _logger.LogWarning("Failed to parse {FilePath}: {Error}", parsedFile.FilePath, parsedFile.ErrorMessage);
            }
        }

        _logger.LogInformation(
            "Indexing complete: {ParsedCount} parsed, {SkippedCount} skipped, {FailedCount} failed, {DeletedCount} deleted, {SymbolCount} symbols, {EdgeCount} edges",
            result.ParsedFiles.Count,
            result.SkippedFiles,
            result.FailedFiles,
            result.DeletedFiles.Count,
            result.TotalSymbols,
            result.TotalEdges);

        // Automatically mark the branch as indexed after successful indexing
        if (fileChangesList.Any())
        {
            var firstChange = fileChangesList.First();
            _gitTracker.MarkBranchAsIndexed(firstChange.RepositoryName, firstChange.BranchName);
        }

        return result;
    }

    /// <summary>
    /// Indexes a single file.
    /// </summary>
    private async Task<ParsedFile?> IndexFileAsync(FileChange fileChange, CancellationToken cancellationToken)
    {
        await _concurrencyLimiter.WaitAsync(cancellationToken);
        try
        {
            // Read file content directly from Git blob
            var content = await _gitTracker.GetFileContentAsync(
                fileChange.RepositoryName,
                fileChange.CommitSha,
                fileChange.FilePath,
                cancellationToken);

            if (content == null)
            {
                _logger.LogDebug("File not found or is binary: {FilePath}", fileChange.FilePath);
                return null;
            }

            // Check file size
            if (content.Length > _options.CurrentValue.MaxFileBytes)
            {
                _logger.LogDebug("File too large ({Size} bytes): {FilePath}", content.Length, fileChange.FilePath);
                return null;
            }

            // Detect language
            var language = _languageDetection.DetectLanguage(fileChange.FilePath, content);

            // Check if we should index this language
            if (!_languageDetection.ShouldIndex(language))
            {
                _logger.LogDebug("Skipping {Language} file: {FilePath}", language, fileChange.FilePath);
                return null;
            }

            // Parse based on language
            ParsedFile parsedFile;
            if (language == Language.CSharp)
            {
                parsedFile = await _roslynParser.ParseFileAsync(
                    fileChange.RepositoryName,
                    fileChange.BranchName,
                    fileChange.CommitSha,
                    fileChange.FilePath,
                    content,
                    cancellationToken);
            }
            else
            {
                parsedFile = await _basicParser.ParseFileAsync(
                    fileChange.RepositoryName,
                    fileChange.BranchName,
                    fileChange.CommitSha,
                    fileChange.FilePath,
                    content,
                    language,
                    cancellationToken);
            }

            return parsedFile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error indexing file: {FilePath}", fileChange.FilePath);
            return new ParsedFile
            {
                RepositoryName = fileChange.RepositoryName,
                BranchName = fileChange.BranchName,
                CommitSha = fileChange.CommitSha,
                FilePath = fileChange.FilePath,
                Language = Language.Unknown,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }
}

/// <summary>
/// Result of an indexing operation.
/// </summary>
public sealed class IndexingResult
{
    /// <summary>
    /// Successfully parsed files.
    /// </summary>
    public List<ParsedFile> ParsedFiles { get; init; } = new();

    /// <summary>
    /// Files that were deleted.
    /// </summary>
    public List<string> DeletedFiles { get; init; } = new();

    /// <summary>
    /// Number of files skipped (wrong language, too large, etc.).
    /// </summary>
    public int SkippedFiles { get; set; }

    /// <summary>
    /// Number of files that failed to parse.
    /// </summary>
    public int FailedFiles { get; set; }

    /// <summary>
    /// Total number of symbols extracted.
    /// </summary>
    public int TotalSymbols { get; set; }

    /// <summary>
    /// Total number of edges extracted.
    /// </summary>
    public int TotalEdges { get; set; }
}

