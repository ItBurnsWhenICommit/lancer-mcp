using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using LancerMcp.Configuration;
using LancerMcp.Models;
using LancerMcp.Repositories;
using Microsoft.Extensions.Options;

namespace LancerMcp.Services;

/// <summary>
/// Orchestrates query execution using hybrid search (BM25 + vector + graph re-ranking).
/// </summary>
public sealed class QueryOrchestrator
{
    private const int SimilarityCandidateLimit = 2000;
    private const int SimilarityTopK = 10;
    private readonly ILogger<QueryOrchestrator> _logger;
    private readonly ICodeChunkRepository _chunkRepository;
    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly ISymbolRepository _symbolRepository;
    private readonly ISymbolSearchRepository _symbolSearchRepository;
    private readonly IEdgeRepository _edgeRepository;
    private readonly ISymbolFingerprintRepository _fingerprintRepository;
    private readonly EmbeddingService _embeddingService;
    private readonly IOptionsMonitor<ServerOptions> _options;

    // Intent detection patterns

    /// <summary>
    /// Pattern for Navigation queries - finding specific symbols or definitions
    /// Examples: "find QueryOrchestrator", "show me the User class", "where is the login method"
    /// </summary>
    private static readonly Regex NavigationPattern = new(
        @"\b(find|show|where is|locate|go to|jump to|open|view|display|get|lookup|navigate to|definition of|declaration of)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern for Relations queries - finding relationships between symbols
    /// Examples: "what calls X", "what does X call", "dependencies of X", "who uses X"
    /// </summary>
    private static readonly Regex RelationsPattern = new(
        @"\b(calls?|called by|references?|referenced by|uses?|used by|depends? on|dependen(ts?|cies)|implements?|implemented by|extends?|extended by|inherits?|inherited by|overrides?|overridden by|invokes?|invoked by|imports?|imported by|requires?|required by|call chain|callers?|callees?|subclasses?|superclasses?|children|parents|who (calls?|uses?|references?|depends on|implements?|extends?|inherits?))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern for Documentation queries - getting explanations or understanding code
    /// Examples: "explain how X works", "what does X do", "tell me about X"
    /// </summary>
    private static readonly Regex DocumentationPattern = new(
        @"\b(what (is|does|are)|explain|describe|documentation|docs|how (does|do|to)|purpose of|why|tell me about|info about|information about|details about|overview of|summary of|understand|learn about)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern for Examples queries - finding usage patterns or code samples
    /// Examples: "show me how to use X", "example of X", "usage pattern for X"
    /// </summary>
    private static readonly Regex ExamplesPattern = new(
        @"\b(example|usage|how to use|sample|demo|show me how|tutorial|guide|pattern|best practice|snippet|code sample)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern for explicit Search queries - finding code by concept or functionality
    /// Examples: "search for error handling", "find code that validates input", "implementation of authentication"
    /// </summary>
    private static readonly Regex SearchPattern = new(
        @"\b(search|look for|find (code|logic|implementation|algorithm) (that|for|which)|code (that|for|which)|logic (that|for|which)|implementation of|algorithm for)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern to detect exact symbol names (PascalCase, camelCase, or specific class/method names)
    /// Examples: "QueryOrchestrator", "getUserById", "IRepository"
    /// </summary>
    private static readonly Regex ExactSymbolPattern = new(
        @"\b([A-Z][a-zA-Z0-9]*(?:\.[A-Z][a-zA-Z0-9]*)*)\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Pattern to detect conceptual/descriptive queries (multiple common programming terms)
    /// Examples: "error handling code", "database connection logic", "authentication service"
    /// </summary>
    private static readonly Regex ConceptualPattern = new(
        @"\b(code|logic|handling|handler|manager|service|utility|helper|function|method|class|interface|struct|enum|type|component|module|package|library|framework|pattern|strategy|factory|builder|adapter|decorator|proxy|singleton|observer|command|state|template|visitor|chain|mediator|memento|prototype|bridge|composite|facade|flyweight|interpreter|iterator|algorithm|implementation|validation|authentication|authorization|configuration|initialization|setup|cleanup|processing|parsing|formatting|serialization|deserialization|encoding|decoding|encryption|decryption|compression|decompression|caching|logging|monitoring|tracing|debugging|testing|mocking|stubbing|assertion|verification)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Pattern to detect file-based queries
    /// Examples: "files in src/", "code in controllers/", "*.cs files"
    /// </summary>
    private static readonly Regex FilePattern = new(
        @"(files? (in|under|within)|\.([a-z]{1,4})\s|/[a-zA-Z0-9_\-/]+/)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public QueryOrchestrator(
        ILogger<QueryOrchestrator> logger,
        ICodeChunkRepository chunkRepository,
        IEmbeddingRepository embeddingRepository,
        ISymbolRepository symbolRepository,
        ISymbolSearchRepository symbolSearchRepository,
        IEdgeRepository edgeRepository,
        ISymbolFingerprintRepository fingerprintRepository,
        EmbeddingService embeddingService,
        IOptionsMonitor<ServerOptions> options)
    {
        _logger = logger;
        _chunkRepository = chunkRepository;
        _embeddingRepository = embeddingRepository;
        _symbolRepository = symbolRepository;
        _symbolSearchRepository = symbolSearchRepository;
        _edgeRepository = edgeRepository;
        _fingerprintRepository = fingerprintRepository;
        _embeddingService = embeddingService;
        _options = options;
    }

    /// <summary>
    /// Execute a query and return ranked results.
    /// Repository parameter is required - multi-repo queries are not supported.
    /// </summary>
    public async Task<QueryResponse> QueryAsync(
        string query,
        string repositoryName,
        string? branchName = null,
        Language? language = null,
        int maxResults = 50,
        RetrievalProfile? profileOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            throw new ArgumentException("Repository name is required. Multi-repo queries are not supported.", nameof(repositoryName));
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Step 1: Parse and analyze the query
            var profile = profileOverride ?? _options.CurrentValue.DefaultRetrievalProfile;
            var parsedQuery = ParseQuery(query, repositoryName, branchName, language, maxResults, profile);
            _logger.LogInformation("Query intent detected: {Intent} for repository: {Repository}", parsedQuery.Intent, repositoryName);

            // Step 2: Execute search based on intent
            Dictionary<string, object>? errorMetadata = null;
            List<SearchResult> results;

            if (parsedQuery.Intent == QueryIntent.Similar)
            {
                var similarityOutcome = await ExecuteSimilaritySearchAsync(parsedQuery, cancellationToken);
                results = similarityOutcome.Results;
                errorMetadata = similarityOutcome.Metadata;
            }
            else
            {
                results = await ExecuteSearchAsync(parsedQuery, cancellationToken);

                // Step 3: Re-rank results if needed
                if (parsedQuery.Intent == QueryIntent.Relations && parsedQuery.IncludeRelated)
                {
                    results = await ApplyGraphReRankingAsync(results, parsedQuery, cancellationToken);
                }
            }

            // Step 4: Generate suggested follow-up queries
            var suggestedQueries = GenerateSuggestedQueries(parsedQuery, results);

            stopwatch.Stop();

            var metadata = new Dictionary<string, object>
            {
                ["keywords"] = parsedQuery.Keywords,
                ["repository"] = repositoryName,
                ["branch"] = branchName ?? "all",
                ["profile"] = parsedQuery.Profile.ToString()
            };

            if (errorMetadata != null)
            {
                foreach (var entry in errorMetadata)
                {
                    metadata[entry.Key] = entry.Value;
                }
            }

            return new QueryResponse
            {
                Query = query,
                Intent = parsedQuery.Intent,
                Results = results.Take(maxResults).ToList(),
                TotalResults = results.Count,
                ExecutionTimeMs = stopwatch.ElapsedMilliseconds,
                SuggestedQueries = suggestedQueries,
                Metadata = metadata
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
        string repositoryName,
        string? branchName,
        Language? language,
        int maxResults,
        RetrievalProfile profile)
    {
        var intent = DetectIntent(query);
        string? similarSymbolId = null;
        List<string>? similarTerms = null;
        var keywordSource = query;

        if (TryParseSimilarQuery(query, out var seedId, out var extraTerms))
        {
            intent = QueryIntent.Similar;
            similarSymbolId = seedId;
            similarTerms = extraTerms;
            keywordSource = string.Join(' ', extraTerms);
        }

        // Extract keywords (simple tokenization)
        var keywords = ExtractKeywords(keywordSource);

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
            MaxResults = maxResults,
            Profile = profile,
            SimilarSymbolId = similarSymbolId,
            SimilarTerms = similarTerms
        };
    }

    /// <summary>
    /// Detect the intent of the query with improved heuristics.
    /// Order matters: check more specific patterns first to avoid false positives.
    /// </summary>
    private QueryIntent DetectIntent(string query)
    {
        if (TryParseSimilarQuery(query, out _, out _))
            return QueryIntent.Similar;

        // 1. Check for Relations intent first (most specific patterns)
        // Examples: "what calls X", "dependencies of Y", "who uses Z"
        if (RelationsPattern.IsMatch(query))
            return QueryIntent.Relations;

        // 2. Check for Documentation intent
        // Examples: "explain how X works", "what does Y do", "tell me about Z"
        if (DocumentationPattern.IsMatch(query))
            return QueryIntent.Documentation;

        // 3. Check for Examples intent
        // Examples: "show me how to use X", "example of Y", "usage pattern for Z"
        if (ExamplesPattern.IsMatch(query))
            return QueryIntent.Examples;

        // 4. Check for explicit Search patterns
        // Examples: "search for error handling", "find code that validates input"
        if (SearchPattern.IsMatch(query))
            return QueryIntent.Search;

        // 5. Improved Navigation vs Search detection
        // Navigation: "find QueryOrchestrator class" (exact symbol)
        // Search: "find error handling code" (conceptual)
        if (NavigationPattern.IsMatch(query))
        {
            var hasExactSymbol = ExactSymbolPattern.IsMatch(query);
            var hasConceptualTerms = ConceptualPattern.Matches(query).Count >= 2;

            // If query has conceptual terms but no clear exact symbol, treat as Search
            if (hasConceptualTerms && !hasExactSymbol)
                return QueryIntent.Search;

            // If query has exact symbol name, treat as Navigation
            if (hasExactSymbol)
                return QueryIntent.Navigation;

            // Default to Navigation for "find X" patterns
            return QueryIntent.Navigation;
        }

        // 6. Check if query is file-based
        // Examples: "files in src/", "*.cs files"
        if (FilePattern.IsMatch(query))
            return QueryIntent.Search;

        // 7. Default to Search for everything else
        // This handles general queries like "authentication", "database connection", etc.
        return QueryIntent.Search;
    }

    private async Task<SimilaritySearchOutcome> ExecuteSimilaritySearchAsync(
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parsedQuery.SimilarSymbolId))
        {
            return SimilaritySearchOutcome.Error("seed_missing", "Similarity query missing seed symbol id.");
        }

        var seedSymbol = await _symbolRepository.GetByIdAsync(parsedQuery.SimilarSymbolId, cancellationToken);
        if (seedSymbol == null)
        {
            return SimilaritySearchOutcome.Error("seed_not_found", $"Seed symbol '{parsedQuery.SimilarSymbolId}' not found.");
        }

        var fingerprint = await _fingerprintRepository.GetBySymbolIdAsync(seedSymbol.Id, cancellationToken);
        if (fingerprint == null)
        {
            return SimilaritySearchOutcome.Error("seed_fingerprint_missing", "Seed symbol fingerprint missing. Reindex to compute similarity.");
        }

        if (!string.Equals(parsedQuery.RepositoryName, seedSymbol.RepositoryName, StringComparison.Ordinal))
        {
            return SimilaritySearchOutcome.Error("seed_scope_mismatch", "Seed symbol does not belong to requested repository.");
        }

        if (!string.IsNullOrWhiteSpace(parsedQuery.BranchName) &&
            !string.Equals(parsedQuery.BranchName, seedSymbol.BranchName, StringComparison.Ordinal))
        {
            return SimilaritySearchOutcome.Error("seed_scope_mismatch", "Seed symbol does not belong to requested branch.");
        }

        var branchName = parsedQuery.BranchName ?? seedSymbol.BranchName;
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return SimilaritySearchOutcome.Error("seed_scope_missing", "Seed symbol branch is unavailable for similarity search.");
        }

        var candidates = await _fingerprintRepository.FindCandidatesAsync(
            seedSymbol.RepositoryName,
            branchName,
            seedSymbol.Language,
            seedSymbol.Kind,
            fingerprint.FingerprintKind,
            fingerprint.Band0,
            fingerprint.Band1,
            fingerprint.Band2,
            fingerprint.Band3,
            SimilarityCandidateLimit,
            cancellationToken);

        var candidateList = candidates
            .Where(candidate => candidate.SymbolId != seedSymbol.Id)
            .GroupBy(candidate => candidate.SymbolId)
            .Select(group =>
            {
                var entry = group.First();
                var distance = BitOperations.PopCount(fingerprint.Fingerprint ^ entry.Fingerprint);
                return new SimilarityCandidate(entry.SymbolId, entry.Fingerprint, distance);
            })
            .OrderBy(candidate => candidate.Distance)
            .ToList();

        if (candidateList.Count == 0)
        {
            return new SimilaritySearchOutcome(new List<SearchResult>(), null);
        }

        var candidateIds = candidateList.Select(candidate => candidate.SymbolId).ToList();
        var symbols = await _symbolRepository.GetByIdsAsync(candidateIds, cancellationToken);
        var symbolLookup = symbols.ToDictionary(symbol => symbol.Id, symbol => symbol);
        var snippets = await _symbolSearchRepository.GetSnippetsBySymbolIdsAsync(candidateIds, cancellationToken);

        var results = new List<SearchResult>();
        var maxResults = Math.Min(parsedQuery.MaxResults, SimilarityTopK);
        var terms = parsedQuery.SimilarTerms ?? new List<string>();

        foreach (var candidate in candidateList)
        {
            if (results.Count >= maxResults)
            {
                break;
            }

            if (!symbolLookup.TryGetValue(candidate.SymbolId, out var symbol))
            {
                continue;
            }

            if (symbol.Language != seedSymbol.Language || symbol.Kind != seedSymbol.Kind)
            {
                continue;
            }

            snippets.TryGetValue(symbol.Id, out var snippet);
            if (!MatchesExtraTerms(symbol, snippet, terms))
            {
                continue;
            }

            var score = 1.0f - (candidate.Distance / 64f);
            results.Add(new SearchResult
            {
                Id = symbol.Id,
                Type = "symbol",
                Repository = symbol.RepositoryName,
                Branch = symbol.BranchName,
                FilePath = symbol.FilePath,
                Language = symbol.Language,
                SymbolName = symbol.Name,
                QualifiedName = symbol.QualifiedName,
                SymbolKind = symbol.Kind,
                Content = snippet ?? symbol.Signature ?? $"{symbol.Kind} {symbol.Name}",
                StartLine = symbol.StartLine,
                EndLine = symbol.EndLine,
                Score = score,
                Signature = symbol.Signature,
                Documentation = symbol.Documentation,
                Reasons = new List<string>
                {
                    "similarity:simhash",
                    $"distance:{candidate.Distance}",
                    $"seed:{seedSymbol.Id}"
                }
            });
        }

        return new SimilaritySearchOutcome(results, null);
    }

    private static bool TryParseSimilarQuery(string query, out string seedSymbolId, out List<string> extraTerms)
    {
        seedSymbolId = string.Empty;
        extraTerms = new List<string>();

        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var trimmed = query.Trim();
        if (!trimmed.StartsWith("similar:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var remainder = trimmed["similar:".Length..].Trim();
        if (string.IsNullOrWhiteSpace(remainder))
        {
            return false;
        }

        var parts = remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        seedSymbolId = parts[0];
        if (parts.Length > 1)
        {
            extraTerms = parts.Skip(1).ToList();
        }

        return true;
    }

    private static bool MatchesExtraTerms(Symbol symbol, string? snippet, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return true;
        }

        var haystack = string.Join(' ', new[]
        {
            symbol.Name,
            symbol.QualifiedName,
            symbol.Signature,
            symbol.Documentation,
            snippet
        }.Where(text => !string.IsNullOrWhiteSpace(text)));

        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                continue;
            }

            if (haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private sealed record SimilaritySearchOutcome(List<SearchResult> Results, Dictionary<string, object>? Metadata)
    {
        public static SimilaritySearchOutcome Error(string errorCode, string errorMessage)
            => new(new List<SearchResult>(), new Dictionary<string, object>
            {
                ["errorCode"] = errorCode,
                ["error"] = errorMessage
            });
    }

    private sealed record SimilarityCandidate(string SymbolId, ulong Fingerprint, int Distance);

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
    /// Execute the search based on parsed query with fallback mechanism.
    /// </summary>
    private async Task<List<SearchResult>> ExecuteSearchAsync(
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        List<SearchResult> results;

        if (parsedQuery.Profile == RetrievalProfile.Fast)
        {
            switch (parsedQuery.Intent)
            {
                case QueryIntent.Navigation:
                    results = await ExecuteNavigationSearchAsync(parsedQuery, cancellationToken);
                    if (!results.Any())
                    {
                        results = await ExecuteFastSearchAsync(parsedQuery, cancellationToken);
                    }
                    return results;

                case QueryIntent.Relations:
                    results = await ExecuteRelationsSearchAsync(parsedQuery, cancellationToken);
                    if (!results.Any())
                    {
                        results = await ExecuteFastSearchAsync(parsedQuery, cancellationToken);
                    }
                    return results;

                default:
                    return await ExecuteFastSearchAsync(parsedQuery, cancellationToken);
            }
        }

        switch (parsedQuery.Intent)
        {
            case QueryIntent.Navigation:
                results = await ExecuteNavigationSearchAsync(parsedQuery, cancellationToken);

                // Fallback: If Navigation returns 0 results, try hybrid search
                if (!results.Any())
                {
                    _logger.LogInformation("Navigation search returned 0 results, falling back to hybrid search");
                    results = await ExecuteHybridSearchAsync(parsedQuery, cancellationToken);
                }
                return results;

            case QueryIntent.Relations:
                results = await ExecuteRelationsSearchAsync(parsedQuery, cancellationToken);

                // Fallback: If Relations returns 0 results, try hybrid search
                if (!results.Any())
                {
                    _logger.LogInformation("Relations search returned 0 results, falling back to hybrid search");
                    results = await ExecuteHybridSearchAsync(parsedQuery, cancellationToken);
                }
                return results;

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
                    parsedQuery.BranchName,
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
                        QualifiedName = symbol.QualifiedName,
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
                parsedQuery.BranchName,
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
                    QualifiedName = symbol.QualifiedName,
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
                    parsedQuery.BranchName,
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
                            QualifiedName = symbol.QualifiedName,
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
                                    QualifiedName = sourceSymbol.QualifiedName,
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
                            QualifiedName = symbol.QualifiedName,
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

        // Optionally enrich with semantic context if we have results
        if (results.Any() && parsedQuery.IncludeRelated)
        {
            results = await EnrichRelationsWithSemanticContextAsync(results, parsedQuery, cancellationToken);
        }

        return results;
    }

    /// <summary>
    /// Enrich relation results with semantic context from hybrid search.
    /// This adds related code chunks that provide context around the relationships.
    /// </summary>
    private async Task<List<SearchResult>> EnrichRelationsWithSemanticContextAsync(
        List<SearchResult> relationResults,
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build a query from the original query to find semantic context
            // For example, "what calls QueryAsync" -> find code chunks about QueryAsync
            var contextQuery = string.Join(" ", parsedQuery.Keywords);

            // Get a few semantic results for context (limit to 5 to avoid overwhelming)
            var contextParsedQuery = new ParsedQuery
            {
                OriginalQuery = contextQuery,
                Intent = QueryIntent.Search,
                Keywords = parsedQuery.Keywords,
                SymbolNames = parsedQuery.SymbolNames,
                FilePaths = parsedQuery.FilePaths,
                Language = parsedQuery.Language,
                RepositoryName = parsedQuery.RepositoryName,
                BranchName = parsedQuery.BranchName,
                IncludeRelated = false,
                MaxResults = 5
            };

            var contextResults = await ExecuteHybridSearchAsync(contextParsedQuery, cancellationToken);

            // Filter out context results that are already in the relation results
            var relationSymbolIds = new HashSet<string>(relationResults.Select(r => r.Id));
            var uniqueContextResults = contextResults
                .Where(cr => !relationSymbolIds.Contains(cr.Id))
                .Take(3) // Only add top 3 context results
                .Select(cr => new SearchResult
                {
                    Id = cr.Id,
                    Type = "semantic_context",
                    Repository = cr.Repository,
                    Branch = cr.Branch,
                    FilePath = cr.FilePath,
                    Language = cr.Language,
                    SymbolName = cr.SymbolName,
                    QualifiedName = cr.QualifiedName,
                    SymbolKind = cr.SymbolKind,
                    Content = cr.Content,
                    StartLine = cr.StartLine,
                    EndLine = cr.EndLine,
                    Score = cr.Score * 0.5f, // Lower score to indicate it's context, not primary result
                    BM25Score = cr.BM25Score,
                    VectorScore = cr.VectorScore,
                    Signature = cr.Signature,
                    Documentation = cr.Documentation
                })
                .ToList();

            // Append context results after the primary relation results
            if (uniqueContextResults.Any())
            {
                _logger.LogInformation("Added {Count} semantic context results to relations query", uniqueContextResults.Count);
                relationResults.AddRange(uniqueContextResults);
            }

            return relationResults;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enrich relations with semantic context, returning original results");
            return relationResults;
        }
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
    /// Execute fast search (symbol search + lightweight reranking).
    /// </summary>
    private async Task<List<SearchResult>> ExecuteFastSearchAsync(
        ParsedQuery parsedQuery,
        CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();

        var repoId = parsedQuery.RepositoryName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(repoId))
        {
            return results;
        }

        var matches = await _symbolSearchRepository.SearchAsync(
            repoId,
            parsedQuery.OriginalQuery,
            parsedQuery.BranchName,
            parsedQuery.MaxResults * 2,
            cancellationToken);

        var matchList = matches.ToList();
        if (matchList.Count == 0)
        {
            return results;
        }

        var symbolIds = matchList.Select(m => m.SymbolId).Distinct().ToList();
        var symbols = await _symbolRepository.GetByIdsAsync(symbolIds, cancellationToken);
        var symbolLookup = symbols.ToDictionary(s => s.Id, s => s);
        var reasons = BuildReasons(parsedQuery);

        foreach (var match in matchList)
        {
            if (!symbolLookup.TryGetValue(match.SymbolId, out var symbol))
            {
                continue;
            }

            var snippet = string.IsNullOrWhiteSpace(match.Snippet) ? null : match.Snippet;
            results.Add(new SearchResult
            {
                Id = symbol.Id,
                Type = "symbol",
                Repository = symbol.RepositoryName,
                Branch = symbol.BranchName,
                FilePath = symbol.FilePath,
                Language = symbol.Language,
                SymbolName = symbol.Name,
                QualifiedName = symbol.QualifiedName,
                SymbolKind = symbol.Kind,
                Content = snippet ?? symbol.Signature ?? $"{symbol.Kind} {symbol.Name}",
                StartLine = symbol.StartLine,
                EndLine = symbol.EndLine,
                Score = match.Score,
                Signature = symbol.Signature,
                Documentation = symbol.Documentation,
                Reasons = reasons.Count == 0 ? null : new List<string>(reasons)
            });
        }

        if (parsedQuery.IncludeRelated && results.Count > 0)
        {
            results = await ApplyGraphReRankingAsync(results, parsedQuery, cancellationToken);
        }

        return results;
    }

    private static List<string> BuildReasons(ParsedQuery parsedQuery)
    {
        var reasons = new List<string>();

        foreach (var keyword in parsedQuery.Keywords.Take(3))
        {
            reasons.Add($"match:{keyword}");
        }

        if (parsedQuery.SymbolNames?.Count > 0)
        {
            reasons.Add($"symbol:{parsedQuery.SymbolNames[0]}");
        }

        return reasons;
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

        // Repository name is guaranteed to be non-null by QueryAsync validation
        var chunks = await _chunkRepository.SearchFullTextAsync(
            repoId: parsedQuery.RepositoryName!,
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
                result.Branch,
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
                QualifiedName = result.QualifiedName,
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
