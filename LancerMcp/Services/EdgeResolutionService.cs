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
            .GroupBy(s => NormalizeQualifiedName(s.QualifiedName!))
            .ToDictionary(
                g => g.Key,
                g => g.First().Id,
                StringComparer.OrdinalIgnoreCase);

        // Build lookup for database symbols (repo:branch:qualified_name -> symbol ID)
        // Group edges by repo/branch to scope queries and prevent cross-repo contamination
        // This also allows the query to leverage the (repo_id, branch_name, qualified_name) index
        var edgesByRepo = edges
            .Where(e => !Guid.TryParse(e.TargetSymbolId, out _)) // Only non-GUID targets (qualified names)
            .GroupBy(e => new { e.RepositoryName, e.BranchName })
            .ToList();

        // Key format: "repo:branch:qualified_name" to prevent cross-repo contamination
        var databaseLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repoGroup in edgesByRepo)
        {
            var targetQualifiedNames = repoGroup
                .Select(e => NormalizeQualifiedName(e.TargetSymbolId))
                .Distinct()
                .ToList();

            if (!targetQualifiedNames.Any())
                continue;

            // Query symbols scoped to the same repo and branch to prevent cross-repo contamination
            // Uses the functional index idx_symbols_qualified_name_lower (created in 06_performance_indexes.sql)
            // for efficient case-insensitive lookups without sequential scans
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
                    QualifiedNames = targetQualifiedNames.ToArray()
                },
                transaction,
                cancellationToken: cancellationToken);

            var dbSymbols = await connection.QueryAsync<(string Id, string QualifiedName)>(command);

            foreach (var (id, qualifiedName) in dbSymbols)
            {
                var normalized = NormalizeQualifiedName(qualifiedName);
                // Include repo and branch in the lookup key to prevent cross-repo contamination
                var lookupKey = $"{repoGroup.Key.RepositoryName}:{repoGroup.Key.BranchName}:{normalized}";
                if (!databaseLookup.ContainsKey(lookupKey))
                {
                    databaseLookup[lookupKey] = id;
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
                var normalizedTarget = NormalizeQualifiedName(targetId);

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
    /// </summary>
    private static string NormalizeQualifiedName(string qualifiedName)
    {
        return qualifiedName.Trim().ToLowerInvariant();
    }
}

