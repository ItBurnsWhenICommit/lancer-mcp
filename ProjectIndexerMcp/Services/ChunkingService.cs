using ProjectIndexerMcp.Models;

namespace ProjectIndexerMcp.Services;

/// <summary>
/// Service for chunking parsed files into code chunks for embedding.
/// Creates chunks at function/class granularity with context overlap.
/// </summary>
public sealed class ChunkingService
{
    private readonly GitTrackerService _gitTracker;
    private readonly ILogger<ChunkingService> _logger;

    // Context overlap configuration (in lines)
    private const int ContextOverlapLines = 5; // ~30-60 tokens depending on line length

    // Maximum chunk size in characters (to stay within 8k token limit)
    private const int MaxChunkChars = 30000; // ~7500 tokens (conservative estimate: 4 chars/token)

    public ChunkingService(
        GitTrackerService gitTracker,
        ILogger<ChunkingService> logger)
    {
        _gitTracker = gitTracker;
        _logger = logger;
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

            _logger.LogDebug("Created {Count} chunks for file {FilePath}",
                chunks.Count, parsedFile.FilePath);

            return new ChunkedFile
            {
                RepositoryName = parsedFile.RepositoryName,
                BranchName = parsedFile.BranchName,
                CommitSha = parsedFile.CommitSha,
                FilePath = parsedFile.FilePath,
                Language = parsedFile.Language,
                Chunks = chunks,
                Success = true
            };
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
        // Calculate chunk boundaries with context overlap
        int chunkStartLine = Math.Max(1, symbol.StartLine - ContextOverlapLines);
        int chunkEndLine = Math.Min(lines.Length, symbol.EndLine + ContextOverlapLines);

        // Extract lines (convert from 1-based to 0-based indexing)
        var chunkLines = lines[(chunkStartLine - 1)..chunkEndLine];
        var chunkContent = string.Join('\n', chunkLines);

        // Check if chunk is too large
        if (chunkContent.Length > MaxChunkChars)
        {
            _logger.LogWarning(
                "Chunk for symbol {Symbol} in {FilePath} is too large ({Size} chars), truncating",
                symbol.Name, filePath, chunkContent.Length);

            // Truncate to max size (remove context overlap if needed)
            var symbolLines = lines[(symbol.StartLine - 1)..symbol.EndLine];
            chunkContent = string.Join('\n', symbolLines);

            if (chunkContent.Length > MaxChunkChars)
            {
                chunkContent = chunkContent[..MaxChunkChars];
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

