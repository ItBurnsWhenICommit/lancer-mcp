using LancerMcp.Models;
using Dapper;

namespace LancerMcp.Services;

/// <summary>
/// Resolves cross-file symbol edges by looking up target symbols in the database.
/// Edges with qualified name strings as targets are resolved to actual symbol IDs.
/// </summary>
public sealed class EdgeResolutionService
{
    private readonly ILogger<EdgeResolutionService> _logger;

    public EdgeResolutionService(ILogger<EdgeResolutionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Resolves cross-file edges by looking up target symbols in the database.
    /// Edges with qualified name strings as targets are resolved to actual symbol IDs.
    /// Returns a tuple of (resolved edges, count of resolved edges).
    /// </summary>
    public async Task<(List<SymbolEdge> edges, int resolvedCount)> ResolveCrossFileEdgesAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        List<SymbolEdge> edges,
        List<Symbol> currentBatchSymbols,
        CancellationToken cancellationToken)
    {
        var resolvedEdges = new List<SymbolEdge>();
        var resolvedCount = 0;

        if (!edges.Any())
        {
            return (resolvedEdges, resolvedCount);
        }

        // Build lookup for current batch symbols (qualified_name -> symbol ID)
        var currentBatchLookup = currentBatchSymbols
            .Where(s => !string.IsNullOrWhiteSpace(s.QualifiedName))
            .GroupBy(s => NormalizeQualifiedName(s.QualifiedName!, stripParameters: false))
            .ToDictionary(
                g => g.Key,
                g => g.First().Id,
                StringComparer.OrdinalIgnoreCase);
        var currentBatchStrippedLookup = currentBatchSymbols
            .Where(s => !string.IsNullOrWhiteSpace(s.QualifiedName))
            .GroupBy(s => NormalizeQualifiedName(s.QualifiedName!, stripParameters: true))
            .ToDictionary(
                g => g.Key,
                g => g.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);
        var currentBatchById = currentBatchSymbols.ToDictionary(s => s.Id);

        // Build lookup for database symbols (repo:branch:qualified_name -> symbol ID)
        // Group edges by repo/branch to scope queries and prevent cross-repo contamination
        // This also allows the query to leverage the (repo_id, branch_name, qualified_name) index
        var edgesByRepo = edges
            .Where(e => !Guid.TryParse(e.TargetSymbolId, out _)) // Only non-GUID targets (qualified names)
            .GroupBy(e => new { e.RepositoryName, e.BranchName })
            .ToList();

        // Key format: "repo:branch:qualified_name" to prevent cross-repo contamination
        var databaseLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var databaseStrippedLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var repoGroup in edgesByRepo)
        {
            var targetQualifiedNames = repoGroup
                .Select(e => NormalizeQualifiedName(e.TargetSymbolId, stripParameters: false))
                .Distinct()
                .ToList();
            var targetQualifiedNameBases = repoGroup
                .Select(e => NormalizeQualifiedName(e.TargetSymbolId, stripParameters: true))
                .Distinct()
                .ToList();

            if (!targetQualifiedNames.Any())
                continue;

            // Query symbols scoped to the same repo and branch to prevent cross-repo contamination
            // Uses the functional index idx_symbols_qualified_name_lower (created in 06_performance_indexes.sql)
            // for efficient case-insensitive lookups without sequential scans.
            // Note: PostgreSQL's query planner uses the index with the ANY operator for typical array sizes
            // (verified up to 10 values). For very large arrays (100+ values), the planner may switch to
            // bitmap/sequential scans depending on selectivity.
            const string sql = @"
                SELECT id, qualified_name
                FROM symbols
                WHERE repo_id = @RepoId
                  AND branch_name = @BranchName
                  AND LOWER(qualified_name) = ANY(@QualifiedNames)";

            var command = new CommandDefinition(
                sql,
                new
                {
                    RepoId = repoGroup.Key.RepositoryName,
                    BranchName = repoGroup.Key.BranchName,
                    QualifiedNames = targetQualifiedNames.ToArray() // Npgsql requires CLR array for PostgreSQL array type
                },
                transaction,
                cancellationToken: cancellationToken);

            var dbSymbols = await connection.QueryAsync<(string Id, string QualifiedName)>(command);

            foreach (var (id, qualifiedName) in dbSymbols)
            {
                var normalized = NormalizeQualifiedName(qualifiedName, stripParameters: false);
                // Include repo and branch in the lookup key to prevent cross-repo contamination
                var lookupKey = $"{repoGroup.Key.RepositoryName}:{repoGroup.Key.BranchName}:{normalized}";
                if (!databaseLookup.ContainsKey(lookupKey))
                {
                    databaseLookup[lookupKey] = id;
                }
            }

            if (targetQualifiedNameBases.Count > 0)
            {
                var patterns = targetQualifiedNameBases
                    .SelectMany(name => new[] { name, $"{name}(%"})
                    .Distinct()
                    .ToList();

                const string fallbackSql = @"
                    SELECT id, qualified_name
                    FROM symbols
                    WHERE repo_id = @RepoId
                      AND branch_name = @BranchName
                      AND LOWER(qualified_name) LIKE ANY(@QualifiedNamePatterns)";

                var fallbackCommand = new CommandDefinition(
                    fallbackSql,
                    new
                    {
                        RepoId = repoGroup.Key.RepositoryName,
                        BranchName = repoGroup.Key.BranchName,
                        QualifiedNamePatterns = patterns.ToArray()
                    },
                    transaction,
                    cancellationToken: cancellationToken);

                var fallbackSymbols = await connection.QueryAsync<(string Id, string QualifiedName)>(fallbackCommand);
                foreach (var (id, qualifiedName) in fallbackSymbols)
                {
                    var normalized = NormalizeQualifiedName(qualifiedName, stripParameters: true);
                    var lookupKey = $"{repoGroup.Key.RepositoryName}:{repoGroup.Key.BranchName}:{normalized}";
                    if (!databaseStrippedLookup.TryGetValue(lookupKey, out var ids))
                    {
                        ids = new List<string>();
                        databaseStrippedLookup[lookupKey] = ids;
                    }

                    if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        // Resolve edges
        foreach (var edge in edges)
        {
            var targetId = edge.TargetSymbolId;
            var wasResolved = false;

            // Check if target is a GUID (already resolved)
            if (!Guid.TryParse(targetId, out _))
            {
                // Target is a qualified name string, try to resolve it
                var normalizedTarget = NormalizeQualifiedName(targetId, stripParameters: false);

                // First check current batch (same repo/branch only)
                if (currentBatchLookup.TryGetValue(normalizedTarget, out var resolvedId))
                {
                    targetId = resolvedId;
                    wasResolved = true;
                    _logger.LogDebug("Resolved edge target '{OriginalTarget}' -> '{NormalizedTarget}' to symbol ID {SymbolId} (current batch)",
                        edge.TargetSymbolId, normalizedTarget, resolvedId);
                }
                // Then check database (scoped to same repo/branch)
                else
                {
                    var lookupKey = $"{edge.RepositoryName}:{edge.BranchName}:{normalizedTarget}";
                    if (databaseLookup.TryGetValue(lookupKey, out resolvedId))
                    {
                        targetId = resolvedId;
                        wasResolved = true;
                        _logger.LogDebug("Resolved edge target '{OriginalTarget}' -> '{NormalizedTarget}' to symbol ID {SymbolId} (database)",
                            edge.TargetSymbolId, normalizedTarget, resolvedId);
                    }
                    var strippedTarget = NormalizeQualifiedName(targetId, stripParameters: true);
                    if (TryResolveByStrippedName(
                            strippedTarget,
                            currentBatchStrippedLookup,
                            databaseStrippedLookup,
                            edge.RepositoryName,
                            edge.BranchName,
                            out resolvedId))
                    {
                        targetId = resolvedId;
                        wasResolved = true;
                        _logger.LogDebug("Resolved edge target '{OriginalTarget}' -> '{NormalizedTarget}' to symbol ID {SymbolId} (stripped fallback)",
                            edge.TargetSymbolId, strippedTarget, resolvedId);
                    }
                    // Fallback: resolve simple method names within the same parent symbol
                    else if (currentBatchById.TryGetValue(edge.SourceSymbolId, out var sourceSymbol) &&
                             !string.IsNullOrWhiteSpace(sourceSymbol.ParentSymbolId))
                    {
                        var simpleName = ExtractSimpleName(strippedTarget);
                        if (!string.IsNullOrEmpty(simpleName))
                        {
                            var localMatch = currentBatchSymbols.FirstOrDefault(s =>
                                s.ParentSymbolId == sourceSymbol.ParentSymbolId &&
                                string.Equals(s.Name, simpleName, StringComparison.OrdinalIgnoreCase));

                            if (localMatch != null)
                            {
                                targetId = localMatch.Id;
                                wasResolved = true;
                                _logger.LogDebug("Resolved edge target '{OriginalTarget}' -> '{SimpleName}' to symbol ID {SymbolId} (local fallback)",
                                    edge.TargetSymbolId, simpleName, localMatch.Id);
                            }
                        }
                    }
                    // If still not resolved, skip this edge (external reference)
                    else
                    {
                        _logger.LogDebug("Could not resolve edge target '{OriginalTarget}' -> '{NormalizedTarget}' in {Repo}/{Branch} (external reference or missing symbol)",
                            edge.TargetSymbolId, normalizedTarget, edge.RepositoryName, edge.BranchName);
                        // Skip edges to external symbols (framework types, etc.)
                        continue;
                    }
                }
            }

            if (wasResolved)
            {
                resolvedCount++;
            }

            resolvedEdges.Add(new SymbolEdge
            {
                Id = edge.Id,
                SourceSymbolId = edge.SourceSymbolId,
                TargetSymbolId = targetId,
                Kind = edge.Kind,
                RepositoryName = edge.RepositoryName,
                BranchName = edge.BranchName,
                CommitSha = edge.CommitSha,
                IndexedAt = edge.IndexedAt
            });
        }

        return (resolvedEdges, resolvedCount);
    }

    /// <summary>
    /// Normalizes qualified names for consistent lookups (case-insensitive, trimmed).
    /// Uses ToLowerInvariant() which is culture-invariant.
    ///
    /// Note: The database query uses LOWER() which is locale-dependent. This works correctly
    /// for ASCII qualified names (typical in C#), but may have edge cases with non-ASCII symbols
    /// if the database locale differs from invariant culture (e.g., Turkish İ/i).
    /// </summary>
    private static string NormalizeQualifiedName(string qualifiedName, bool stripParameters)
    {
        var trimmed = qualifiedName.Trim();
        if (stripParameters)
        {
            var parameterIndex = trimmed.IndexOf('(');
            if (parameterIndex >= 0)
            {
                trimmed = trimmed[..parameterIndex];
            }
        }

        return trimmed.ToLowerInvariant();
    }

    private static bool TryResolveByStrippedName(
        string strippedTarget,
        IReadOnlyDictionary<string, List<string>> currentBatchStrippedLookup,
        IReadOnlyDictionary<string, List<string>> databaseStrippedLookup,
        string repositoryName,
        string branchName,
        out string resolvedId)
    {
        resolvedId = string.Empty;

        if (currentBatchStrippedLookup.TryGetValue(strippedTarget, out var currentIds) &&
            currentIds.Count == 1)
        {
            resolvedId = currentIds[0];
            return true;
        }

        var lookupKey = $"{repositoryName}:{branchName}:{strippedTarget}";
        if (databaseStrippedLookup.TryGetValue(lookupKey, out var databaseIds) &&
            databaseIds.Count == 1)
        {
            resolvedId = databaseIds[0];
            return true;
        }

        return false;
    }

    private static string ExtractSimpleName(string normalizedQualifiedName)
    {
        if (string.IsNullOrWhiteSpace(normalizedQualifiedName))
        {
            return string.Empty;
        }

        var lastDot = normalizedQualifiedName.LastIndexOf('.');
        if (lastDot >= 0 && lastDot + 1 < normalizedQualifiedName.Length)
        {
            return normalizedQualifiedName[(lastDot + 1)..];
        }

        return normalizedQualifiedName;
    }
}
