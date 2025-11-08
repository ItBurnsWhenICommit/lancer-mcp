using System.Diagnostics;
using System.Text.RegularExpressions;
using LancerMcp.Models;
using LancerMcp.Repositories;

namespace LancerMcp.Services;

/// <summary>
/// Orchestrates query execution using hybrid search (BM25 + vector + graph re-ranking).
/// </summary>
public sealed class QueryOrchestrator
{
    private readonly ILogger<QueryOrchestrator> _logger;
    private readonly ICodeChunkRepository _chunkRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly IEdgeRepository _edgeRepository;
    private readonly EmbeddingService _embeddingService;

    // Intent detection patterns
    private static readonly Regex NavigationPattern = new(
        @"\b(find|show|where is|locate|go to|definition of|navigate to)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RelationsPattern = new(
        @"\b(calls?|references?|uses?|depends? on|implements?|extends?|inherits?|call chain|callers?|callees?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DocumentationPattern = new(
        @"\b(what (is|does)|explain|describe|documentation|how (does|to)|purpose of)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExamplesPattern = new(
        @"\b(example|usage|how to use|sample|demo)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public QueryOrchestrator(
        ILogger<QueryOrchestrator> logger,
        ICodeChunkRepository chunkRepository,
        IEmbeddingRepository embeddingRepository,
        ISymbolRepository symbolRepository,
        IEdgeRepository edgeRepository,
        EmbeddingService embeddingService)
    {
        _logger = logger;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _symbolRepository = symbolRepository;
        _edgeRepository = edgeRepository;
        _embeddingService = embeddingService;
    }

    /// <summary>
    /// Execute a query and return ranked results.
    /// </summary>
    public async Task<QueryResponse> QueryAsync(
        string query,
        string? repositoryName = null,
        string? branchName = null,
        Language? language = null,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Step 1: Parse and analyze the query
            var parsedQuery = ParseQuery(query, repositoryName, branchName, language, maxResults);
            _logger.LogInformation("Query intent detected: {Intent}", parsedQuery.Intent);

            // Step 2: Execute search based on intent
            var results = await ExecuteSearchAsync(parsedQuery, cancellationToken);

            // Step 3: Re-rank results if needed
            if (parsedQuery.Intent == QueryIntent.Relations && parsedQuery.IncludeRelated)
            {
                results = await ApplyGraphReRankingAsync(results, parsedQuery, cancellationToken);
            }

            // Step 4: Generate suggested follow-up queries
            var suggestedQueries = GenerateSuggestedQueries(parsedQuery, results);

            stopwatch.Stop();

            return new QueryResponse
            {
                Query = query,
                Intent = parsedQuery.Intent,
                Results = results.Take(maxResults).ToList(),
                TotalResults = results.Count,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                SuggestedQueries = suggestedQueries,
                Metadata = new Dictionary<string, object>
                {
                    ["keywords"] = parsedQuery.Keywords,
                    ["repository"] = repositoryName ?? "all",
                    ["branch"] = branchName ?? "all"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing query: {Query}", query);
            throw;
        }
    }

    /// <summary>
    /// Parse the query and detect intent.
    /// </summary>
    private ParsedQuery ParseQuery(
        string query,
        string? repositoryName,
        string? branchName,
        Language? language,
        int maxResults)
    {
        // Detect intent
        var intent = DetectIntent(query);

        // Extract keywords (simple tokenization)
        var keywords = ExtractKeywords(query);

        // Extract symbol names (CamelCase or snake_case identifiers)
        var symbolNames = ExtractSymbolNames(query);

        // Extract file paths
        var filePaths = ExtractFilePaths(query);

        // Determine if we should include related symbols
        var includeRelated = intent == QueryIntent.Relations ||
                           query.Contains("related", StringComparison.OrdinalIgnoreCase) ||
                           query.Contains("context", StringComparison.OrdinalIgnoreCase);

        return new ParsedQuery
        {
            OriginalQuery = query,
            Intent = intent,
            Keywords = keywords,
            SymbolNames = symbolNames.Any() ? symbolNames : null,
            FilePaths = filePaths.Any() ? filePaths : null,
            Language = language,
            RepositoryName = repositoryName,
            BranchName = branchName,
            IncludeRelated = includeRelated,
            MaxResults = maxResults
        };
    }

    /// <summary>
    /// Detect the intent of the query.
    /// </summary>
    private QueryIntent DetectIntent(string query)
    {
        if (NavigationPattern.IsMatch(query))
            return QueryIntent.Navigation;

        if (RelationsPattern.IsMatch(query))
            return QueryIntent.Relations;

        if (DocumentationPattern.IsMatch(query))
            return QueryIntent.Documentation;

        if (ExamplesPattern.IsMatch(query))
            return QueryIntent.Examples;

        return QueryIntent.Search;
    }

    /// <summary>
    /// Extract keywords from the query.
    /// </summary>
    private List<string> ExtractKeywords(string query)
    {
        // Remove common stop words and extract meaningful terms
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for",
            "of", "with", "by", "from", "as", "is", "was", "are", "were", "be",
            "been", "being", "have", "has", "had", "do", "does", "did", "will",
            "would", "should", "could", "may", "might", "can", "find", "show",
            "where", "what", "how", "why", "when", "who"
        };

        var words = Regex.Split(query, @"\W+")
            .Where(w => w.Length > 2 && !stopWords.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .Distinct()
            .ToList();

        return words;
    }

    /// <summary>
    /// Extract potential symbol names from the query.
    /// </summary>
    private List<string> ExtractSymbolNames(string query)
    {
        // Match CamelCase or snake_case identifiers
        var pattern = @"\b([A-Z][a-z]+(?:[A-Z][a-z]+)*|[a-z]+(?:_[a-z]+)+)\b";
        var matches = Regex.Matches(query, pattern);

        return matches
            .Select(m => m.Value)
            .Where(s => s.Length > 2)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Extract file paths from the query.
    /// </summary>
    private List<string> ExtractFilePaths(string query)
    {
        // Match file paths (e.g., src/Program.cs, lib/utils.py)
        var pattern = @"\b[\w/\\]+\.\w+\b";
        var matches = Regex.Matches(query, pattern);

        return matches
            .Select(m => m.Value)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Execute the search based on parsed query.
    /// </summary>
    private async Task<List<SearchResult>> ExecuteSearchAsync(
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        switch (parsedQuery.Intent)
        {
            case QueryIntent.Navigation:
                return await ExecuteNavigationSearchAsync(parsedQuery, cancellationToken);

            case QueryIntent.Relations:
                return await ExecuteRelationsSearchAsync(parsedQuery, cancellationToken);

            case QueryIntent.Documentation:
            case QueryIntent.Examples:
            case QueryIntent.Search:
            default:
                return await ExecuteHybridSearchAsync(parsedQuery, cancellationToken);
        }
    }

    /// <summary>
    /// Execute navigation search (find specific symbols).
    /// </summary>
    private async Task<List<SearchResult>> ExecuteNavigationSearchAsync(
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        var seenSymbolIds = new HashSet<string>(); // Track seen symbols to avoid duplicates

        // Search for symbols by name
        if (parsedQuery.SymbolNames?.Any() == true)
        {
            foreach (var symbolName in parsedQuery.SymbolNames)
            {
                var symbols = await _symbolRepository.SearchByNameAsync(
                    parsedQuery.RepositoryName ?? string.Empty,
                    symbolName,
                    fuzzy: true,
                    limit: parsedQuery.MaxResults,
                    cancellationToken);

                foreach (var symbol in symbols)
                {
                    // Skip if we've already processed this symbol
                    if (!seenSymbolIds.Add(symbol.Id))
                        continue;

                    results.Add(new SearchResult
                    {
                        Id = symbol.Id,
                        Type = "symbol",
                        Repository = symbol.RepositoryName,
                        Branch = symbol.BranchName,
                        FilePath = symbol.FilePath,
                        Language = symbol.Language,
                        SymbolName = symbol.Name,
                        SymbolKind = symbol.Kind,
                        Content = symbol.Signature ?? $"{symbol.Kind} {symbol.Name}",
                        StartLine = symbol.StartLine,
                        EndLine = symbol.EndLine,
                        Score = 1.0f,
                        Signature = symbol.Signature,
                        Documentation = symbol.Documentation
                    });
                }
            }
        }

        // If no symbol names found, fall back to keyword search
        if (!results.Any())
        {
            var query = string.Join(" ", parsedQuery.Keywords);
            var symbols = await _symbolRepository.SearchByNameAsync(
                parsedQuery.RepositoryName ?? string.Empty,
                query,
                fuzzy: true,
                limit: parsedQuery.MaxResults,
                cancellationToken);

            foreach (var symbol in symbols)
            {
                // Skip if we've already processed this symbol
                if (!seenSymbolIds.Add(symbol.Id))
                    continue;

                results.Add(new SearchResult
                {
                    Id = symbol.Id,
                    Type = "symbol",
                    Repository = symbol.RepositoryName,
                    Branch = symbol.BranchName,
                    FilePath = symbol.FilePath,
                    Language = symbol.Language,
                    SymbolName = symbol.Name,
                    SymbolKind = symbol.Kind,
                    Content = symbol.Signature ?? $"{symbol.Kind} {symbol.Name}",
                    StartLine = symbol.StartLine,
                    EndLine = symbol.EndLine,
                    Score = 0.8f,
                    Signature = symbol.Signature,
                    Documentation = symbol.Documentation
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Execute relations search (find symbol relationships).
    /// </summary>
    private async Task<List<SearchResult>> ExecuteRelationsSearchAsync(
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        var seenSymbolIds = new HashSet<string>(); // Track seen symbols to avoid duplicates

        // Detect query direction: "what calls X" vs "what does X call"
        var isIncomingQuery = DetectIncomingRelationQuery(parsedQuery.OriginalQuery);

        // First, find the symbols mentioned in the query
        if (parsedQuery.SymbolNames?.Any() == true)
        {
            foreach (var symbolName in parsedQuery.SymbolNames)
            {
                var symbols = await _symbolRepository.SearchByNameAsync(
                    parsedQuery.RepositoryName ?? string.Empty,
                    symbolName,
                    fuzzy: true,
                    limit: 5,
                    cancellationToken);

                foreach (var symbol in symbols)
                {
                    // Skip if we've already processed this symbol
                    if (!seenSymbolIds.Add(symbol.Id))
                        continue;

                    // Get related symbols through edges
                    var relatedSymbols = new List<RelatedSymbol>();

                    // Get outgoing edges (what this symbol calls/references)
                    var outgoingEdges = await _edgeRepository.GetBySourceAsync(symbol.Id, cancellationToken: cancellationToken);
                    foreach (var edge in outgoingEdges.Take(10))
                    {
                        var targetSymbol = await _symbolRepository.GetByIdAsync(edge.TargetSymbolId, cancellationToken);
                        if (targetSymbol != null)
                        {
                            relatedSymbols.Add(new RelatedSymbol
                            {
                                Id = targetSymbol.Id,
                                Name = targetSymbol.Name,
                                Kind = targetSymbol.Kind,
                                RelationType = edge.Kind.ToString(),
                                FilePath = targetSymbol.FilePath,
                                Line = targetSymbol.StartLine
                            });
                        }
                    }

                    // Get incoming edges (what calls/references this symbol)
                    var incomingEdges = await _edgeRepository.GetByTargetAsync(symbol.Id, cancellationToken: cancellationToken);
                    foreach (var edge in incomingEdges.Take(10))
                    {
                        var sourceSymbol = await _symbolRepository.GetByIdAsync(edge.SourceSymbolId, cancellationToken);
                        if (sourceSymbol != null)
                        {
                            relatedSymbols.Add(new RelatedSymbol
                            {
                                Id = sourceSymbol.Id,
                                Name = sourceSymbol.Name,
                                Kind = sourceSymbol.Kind,
                                RelationType = $"CalledBy_{edge.Kind}",
                                FilePath = sourceSymbol.FilePath,
                                Line = sourceSymbol.StartLine
                            });
                        }
                    }

                    // If this is an incoming query (what calls X), promote callers to primary results
                    if (isIncomingQuery && incomingEdges.Any())
                    {
                        // Add the target symbol first as context
                        results.Add(new SearchResult
                        {
                            Id = symbol.Id,
                            Type = "symbol_with_relations",
                            Repository = symbol.RepositoryName,
                            Branch = symbol.BranchName,
                            FilePath = symbol.FilePath,
                            Language = symbol.Language,
                            SymbolName = symbol.Name,
                            SymbolKind = symbol.Kind,
                            Content = symbol.Signature ?? $"{symbol.Kind} {symbol.Name}",
                            StartLine = symbol.StartLine,
                            EndLine = symbol.EndLine,
                            Score = 1.0f,
                            Signature = symbol.Signature,
                            Documentation = symbol.Documentation,
                            RelatedSymbols = relatedSymbols
                        });

                        // Then add each caller as a primary result
                        foreach (var edge in incomingEdges.Take(20))
                        {
                            var sourceSymbol = await _symbolRepository.GetByIdAsync(edge.SourceSymbolId, cancellationToken);
                            if (sourceSymbol != null && seenSymbolIds.Add(sourceSymbol.Id))
                            {
                                results.Add(new SearchResult
                                {
                                    Id = sourceSymbol.Id,
                                    Type = "caller",
                                    Repository = sourceSymbol.RepositoryName,
                                    Branch = sourceSymbol.BranchName,
                                    FilePath = sourceSymbol.FilePath,
                                    Language = sourceSymbol.Language,
                                    SymbolName = sourceSymbol.Name,
                                    SymbolKind = sourceSymbol.Kind,
                                    Content = sourceSymbol.Signature ?? $"{sourceSymbol.Kind} {sourceSymbol.Name}",
                                    StartLine = sourceSymbol.StartLine,
                                    EndLine = sourceSymbol.EndLine,
                                    Score = 0.9f,
                                    Signature = sourceSymbol.Signature,
                                    Documentation = sourceSymbol.Documentation,
                                    RelatedSymbols = new List<RelatedSymbol>
                                    {
                                        new RelatedSymbol
                                        {
                                            Id = symbol.Id,
                                            Name = symbol.Name,
                                            Kind = symbol.Kind,
                                            RelationType = edge.Kind.ToString(),
                                            FilePath = symbol.FilePath,
                                            Line = symbol.StartLine
                                        }
                                    }
                                });
                            }
                        }
                    }
                    else
                    {
                        // Default behavior: return the symbol with its relationships
                        results.Add(new SearchResult
                        {
                            Id = symbol.Id,
                            Type = "symbol_with_relations",
                            Repository = symbol.RepositoryName,
                            Branch = symbol.BranchName,
                            FilePath = symbol.FilePath,
                            Language = symbol.Language,
                            SymbolName = symbol.Name,
                            SymbolKind = symbol.Kind,
                            Content = symbol.Signature ?? $"{symbol.Kind} {symbol.Name}",
                            StartLine = symbol.StartLine,
                            EndLine = symbol.EndLine,
                            Score = 1.0f,
                            Signature = symbol.Signature,
                            Documentation = symbol.Documentation,
                            RelatedSymbols = relatedSymbols
                        });
                    }
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Detect if the query is asking for incoming relationships (what calls/uses X).
    /// </summary>
    private bool DetectIncomingRelationQuery(string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        // Patterns that indicate incoming relationships (what calls/uses/references this)
        var incomingPatterns = new[]
        {
            @"\bwhat\s+(calls?|uses?|references?|depends?\s+on)\b",
            @"\b(callers?|usages?|references?)\s+of\b",
            @"\bwho\s+(calls?|uses?|references?)\b",
            @"\bfind\s+(callers?|usages?|references?)\b",
            @"\bshow\s+(callers?|usages?|references?)\b"
        };

        return incomingPatterns.Any(pattern => Regex.IsMatch(lowerQuery, pattern));
    }

    /// <summary>
    /// Execute hybrid search (BM25 + vector similarity).
    /// </summary>
    private async Task<List<SearchResult>> ExecuteHybridSearchAsync(
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();

        try
        {
            // Generate embedding for the query
            // Create a temporary chunk for the query text
            var queryChunk = new CodeChunk
            {
                Id = Guid.NewGuid().ToString(),
                RepositoryName = parsedQuery.RepositoryName ?? string.Empty,
                BranchName = parsedQuery.BranchName ?? string.Empty,
                CommitSha = string.Empty,
                FilePath = string.Empty,
                Language = Language.Unknown,
                Content = parsedQuery.OriginalQuery,
                StartLine = 0,
                EndLine = 0,
                ChunkStartLine = 0,
                ChunkEndLine = 0,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
                new[] { queryChunk },
                cancellationToken);

            if (embeddings.Count == 0 || embeddings[0].Vector.Length == 0)
            {
                _logger.LogWarning("Failed to generate embedding, falling back to full-text search only");
                return await ExecuteFullTextSearchOnlyAsync(parsedQuery, cancellationToken);
            }

            var queryEmbedding = embeddings[0].Vector;

            // Execute hybrid search using the database function
            var hybridResults = await _embeddingRepository.HybridSearchAsync(
                queryText: parsedQuery.OriginalQuery,
                queryVector: queryEmbedding,
                repoId: parsedQuery.RepositoryName,
                branchName: parsedQuery.BranchName,
                bm25Weight: 0.3f,
                vectorWeight: 0.7f,
                limit: parsedQuery.MaxResults * 2, // Get more results for re-ranking
                cancellationToken);

            // Convert to SearchResult objects
            foreach (var (chunkId, score, bm25Score, vectorScore) in hybridResults)
            {
                var chunk = await _chunkRepository.GetByIdAsync(chunkId, cancellationToken);
                if (chunk == null)
                    continue;

                results.Add(new SearchResult
                {
                    Id = chunk.Id,
                    Type = "code_chunk",
                    Repository = chunk.RepositoryName,
                    Branch = chunk.BranchName,
                    FilePath = chunk.FilePath,
                    Language = chunk.Language,
                    SymbolName = chunk.SymbolName,
                    SymbolKind = chunk.SymbolKind,
                    Content = chunk.Content,
                    StartLine = chunk.ChunkStartLine,
                    EndLine = chunk.ChunkEndLine,
                    Score = score,
                    BM25Score = bm25Score,
                    VectorScore = vectorScore,
                    Signature = chunk.Signature,
                    Documentation = chunk.Documentation
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in hybrid search, falling back to full-text search");
            return await ExecuteFullTextSearchOnlyAsync(parsedQuery, cancellationToken);
        }

        return results;
    }

    /// <summary>
    /// Execute full-text search only (fallback when embeddings are not available).
    /// </summary>
    private async Task<List<SearchResult>> ExecuteFullTextSearchOnlyAsync(
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();

        var chunks = await _chunkRepository.SearchFullTextAsync(
            repoId: parsedQuery.RepositoryName ?? string.Empty,
            query: parsedQuery.OriginalQuery,
            branchName: parsedQuery.BranchName,
            language: parsedQuery.Language,
            limit: parsedQuery.MaxResults,
            cancellationToken);

        foreach (var chunk in chunks)
        {
            results.Add(new SearchResult
            {
                Id = chunk.Id,
                Type = "code_chunk",
                Repository = chunk.RepositoryName,
                Branch = chunk.BranchName,
                FilePath = chunk.FilePath,
                Language = chunk.Language,
                SymbolName = chunk.SymbolName,
                SymbolKind = chunk.SymbolKind,
                Content = chunk.Content,
                StartLine = chunk.ChunkStartLine,
                EndLine = chunk.ChunkEndLine,
                Score = 0.5f, // Default score for full-text only
                BM25Score = 0.5f,
                Signature = chunk.Signature,
                Documentation = chunk.Documentation
            });
        }

        return results;
    }

    /// <summary>
    /// Apply graph re-ranking to boost results based on symbol relationships.
    /// </summary>
    private async Task<List<SearchResult>> ApplyGraphReRankingAsync(
        List<SearchResult> results,
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        // For each result, calculate a graph score based on:
        // 1. Number of incoming/outgoing edges
        // 2. Centrality in the call graph
        // 3. Relevance to query symbols

        var rerankedResults = new List<SearchResult>();

        foreach (var result in results)
        {
            if (result.SymbolName == null)
            {
                rerankedResults.Add(result);
                continue;
            }

            // Get the symbol
            var symbols = await _symbolRepository.SearchByNameAsync(
                result.Repository,
                result.SymbolName,
                fuzzy: false,
                limit: 1,
                cancellationToken);

            var symbol = symbols.FirstOrDefault();
            if (symbol == null)
            {
                rerankedResults.Add(result);
                continue;
            }

            // Count edges
            var outgoingEdges = await _edgeRepository.GetBySourceAsync(symbol.Id, cancellationToken: cancellationToken);
            var incomingEdges = await _edgeRepository.GetByTargetAsync(symbol.Id, cancellationToken: cancellationToken);

            var outgoingCount = outgoingEdges.Count();
            var incomingCount = incomingEdges.Count();

            // Calculate graph score (normalized)
            var graphScore = Math.Min(1.0f, (outgoingCount + incomingCount * 2) / 20.0f);

            // Create new result with updated scores
            var updatedResult = new SearchResult
            {
                Id = result.Id,
                Type = result.Type,
                Repository = result.Repository,
                Branch = result.Branch,
                FilePath = result.FilePath,
                Language = result.Language,
                SymbolName = result.SymbolName,
                SymbolKind = result.SymbolKind,
                Content = result.Content,
                StartLine = result.StartLine,
                EndLine = result.EndLine,
                Score = result.Score * 0.7f + graphScore * 0.3f,
                BM25Score = result.BM25Score,
                VectorScore = result.VectorScore,
                GraphScore = graphScore,
                Signature = result.Signature,
                Documentation = result.Documentation,
                RelatedSymbols = result.RelatedSymbols
            };

            rerankedResults.Add(updatedResult);
        }

        // Re-sort by updated score
        return rerankedResults.OrderByDescending(r => r.Score).ToList();
    }

    /// <summary>
    /// Generate suggested follow-up queries based on results.
    /// </summary>
    private List<string> GenerateSuggestedQueries(ParsedQuery parsedQuery, List<SearchResult> results)
    {
        var suggestions = new List<string>();

        // Suggest related queries based on intent
        switch (parsedQuery.Intent)
        {
            case QueryIntent.Search:
                if (results.Any(r => r.SymbolName != null))
                {
                    var topSymbol = results.First(r => r.SymbolName != null);
                    suggestions.Add($"Show me how {topSymbol.SymbolName} is used");
                    suggestions.Add($"What calls {topSymbol.SymbolName}?");
                    suggestions.Add($"Find similar code to {topSymbol.SymbolName}");
                }
                break;

            case QueryIntent.Navigation:
                if (results.Any())
                {
                    var topResult = results.First();
                    suggestions.Add($"Show me the implementation of {topResult.SymbolName}");
                    suggestions.Add($"What are the dependencies of {topResult.SymbolName}?");
                }
                break;

            case QueryIntent.Relations:
                if (results.Any(r => r.RelatedSymbols?.Any() == true))
                {
                    var topResult = results.First(r => r.RelatedSymbols?.Any() == true);
                    var relatedSymbol = topResult.RelatedSymbols!.First();
                    suggestions.Add($"Show me {relatedSymbol.Name}");
                    suggestions.Add($"Find the call chain from {topResult.SymbolName} to {relatedSymbol.Name}");
                }
                break;

            case QueryIntent.Documentation:
                if (results.Any())
                {
                    suggestions.Add($"Show me examples of {parsedQuery.Keywords.FirstOrDefault()}");
                    suggestions.Add($"Find tests for {parsedQuery.Keywords.FirstOrDefault()}");
                }
                break;

            case QueryIntent.Examples:
                if (results.Any())
                {
                    suggestions.Add($"Explain how {parsedQuery.Keywords.FirstOrDefault()} works");
                    suggestions.Add($"Find more examples of {parsedQuery.Keywords.FirstOrDefault()}");
                }
                break;
        }

        return suggestions.Take(3).ToList();
    }
}
