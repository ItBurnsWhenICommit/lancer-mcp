using Microsoft.Extensions.Options;
using LancerMcp.Configuration;
using LancerMcp.Models;

namespace LancerMcp.Services;

/// <summary>
/// Service for chunking parsed files into code chunks for embedding.
/// Creates chunks at function/class granularity with context overlap.
/// </summary>
public sealed class ChunkingService
{
    private readonly GitTrackerService _gitTracker;
    private readonly ILogger<ChunkingService> _logger;
    private readonly IOptionsMonitor<ServerOptions> _options;

    public ChunkingService(
        GitTrackerService gitTracker,
        ILogger<ChunkingService> logger,
        IOptionsMonitor<ServerOptions> options)
    {
        _gitTracker = gitTracker;
        _logger = logger;
        _options = options;
    }

    /// <summary>
    /// Chunks a parsed file into code chunks for embedding.
    /// </summary>
    public async Task<ChunkedFile> ChunkFileAsync(
        ParsedFile parsedFile,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the full file content
            var content = await _gitTracker.GetFileContentAsync(
                parsedFile.RepositoryName,
                parsedFile.CommitSha,
                parsedFile.FilePath,
                cancellationToken);

            if (content == null)
            {
                return new ChunkedFile
                {
                    RepositoryName = parsedFile.RepositoryName,
                    BranchName = parsedFile.BranchName,
                    CommitSha = parsedFile.CommitSha,
                    FilePath = parsedFile.FilePath,
                    Language = parsedFile.Language,
                    Success = false,
                    ErrorMessage = "File content not found"
                };
            }

            var lines = content.Split('\n');
            var chunks = new List<CodeChunk>();

            // Create chunks for each symbol
            foreach (var symbol in parsedFile.Symbols)
            {
                // Only chunk meaningful symbols (skip variables, parameters, etc.)
                if (!ShouldChunkSymbol(symbol.Kind))
                {
                    continue;
                }

                var chunk = CreateChunkForSymbol(
                    symbol,
                    lines,
                    parsedFile.RepositoryName,
                    parsedFile.BranchName,
                    parsedFile.CommitSha,
                    parsedFile.FilePath,
                    parsedFile.Language,
                    parsedFile.Symbols);

                if (chunk != null)
                {
                    chunks.Add(chunk);
                }
            }

            var result = new ChunkedFile
            {
                RepositoryName = parsedFile.RepositoryName,
                BranchName = parsedFile.BranchName,
                CommitSha = parsedFile.CommitSha,
                FilePath = parsedFile.FilePath,
                Language = parsedFile.Language,
                Chunks = chunks,
                Success = true
            };

            _logger.LogInformation(
                "Chunked file {FilePath}: {ChunkCount} chunks, {TokenCount} total tokens",
                parsedFile.FilePath,
                result.TotalChunks,
                result.TotalTokens);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error chunking file {FilePath}", parsedFile.FilePath);
            return new ChunkedFile
            {
                RepositoryName = parsedFile.RepositoryName,
                BranchName = parsedFile.BranchName,
                CommitSha = parsedFile.CommitSha,
                FilePath = parsedFile.FilePath,
                Language = parsedFile.Language,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Determines if a symbol should be chunked.
    /// </summary>
    private static bool ShouldChunkSymbol(SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Class => true,
            SymbolKind.Interface => true,
            SymbolKind.Struct => true,
            SymbolKind.Enum => true,
            SymbolKind.Method => true,
            SymbolKind.Function => true,
            SymbolKind.Constructor => true,
            SymbolKind.Property => true,
            SymbolKind.Namespace => false, // Too large
            SymbolKind.Variable => false,  // Too small
            SymbolKind.Parameter => false, // Too small
            SymbolKind.Field => false,     // Usually too small
            _ => false
        };
    }

    /// <summary>
    /// Creates a code chunk for a symbol with context overlap.
    /// </summary>
    private CodeChunk? CreateChunkForSymbol(
        Symbol symbol,
        string[] lines,
        string repositoryName,
        string branchName,
        string commitSha,
        string filePath,
        Language language,
        List<Symbol> allSymbols)
    {
        // Get configuration values
        var contextLinesBefore = _options.CurrentValue.ChunkContextLinesBefore;
        var contextLinesAfter = _options.CurrentValue.ChunkContextLinesAfter;
        var maxChunkChars = _options.CurrentValue.MaxChunkChars;

        // Calculate chunk boundaries with context overlap
        int chunkStartLine = Math.Max(1, symbol.StartLine - contextLinesBefore);
        int chunkEndLine = Math.Min(lines.Length, symbol.EndLine + contextLinesAfter);

        // Extract lines (convert from 1-based to 0-based indexing)
        var chunkLines = lines[(chunkStartLine - 1)..chunkEndLine];
        var chunkContent = string.Join('\n', chunkLines);

        // Check if chunk is too large
        if (chunkContent.Length > maxChunkChars)
        {
            _logger.LogWarning(
                "Chunk for symbol {Symbol} in {FilePath} is too large ({Size} chars), truncating to {MaxSize} chars",
                symbol.Name, filePath, chunkContent.Length, maxChunkChars);

            // Truncate to max size (remove context overlap if needed)
            var symbolLines = lines[(symbol.StartLine - 1)..symbol.EndLine];
            chunkContent = string.Join('\n', symbolLines);

            if (chunkContent.Length > maxChunkChars)
            {
                chunkContent = chunkContent[..maxChunkChars];
            }

            chunkStartLine = symbol.StartLine;
            chunkEndLine = symbol.EndLine;
        }

        // Estimate token count (rough approximation: 1 token ≈ 4 characters)
        int tokenCount = chunkContent.Length / 4;

        // Find parent symbol name (for nested symbols)
        string? parentSymbolName = null;
        if (symbol.ParentSymbolId != null)
        {
            var parentSymbol = allSymbols.FirstOrDefault(s => s.Id == symbol.ParentSymbolId);
            parentSymbolName = parentSymbol?.Name;
        }

        return new CodeChunk
        {
            RepositoryName = repositoryName,
            BranchName = branchName,
            CommitSha = commitSha,
            FilePath = filePath,
            SymbolId = symbol.Id,
            SymbolName = symbol.QualifiedName ?? symbol.Name,
            SymbolKind = symbol.Kind,
            Language = language,
            Content = chunkContent,
            StartLine = symbol.StartLine,
            EndLine = symbol.EndLine,
            ChunkStartLine = chunkStartLine,
            ChunkEndLine = chunkEndLine,
            TokenCount = tokenCount,
            ParentSymbolName = parentSymbolName,
            Signature = symbol.Signature,
            Documentation = symbol.Documentation
        };
    }
}

