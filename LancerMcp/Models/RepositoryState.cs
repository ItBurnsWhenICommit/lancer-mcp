namespace LancerMcp.Models;

/// <summary>
/// Represents the state of a tracked repository.
/// </summary>
public sealed class RepositoryState
{
    /// <summary>
    /// Unique identifier for this repository.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Remote URL of the repository.
    /// </summary>
    public required string RemoteUrl { get; init; }

    /// <summary>
    /// Local path where the repository is cloned.
    /// </summary>
    public required string LocalPath { get; init; }

    /// <summary>
    /// Default branch name (main/master/trunk).
    /// </summary>
    public required string DefaultBranch { get; init; }

    /// <summary>
    /// Tracked branches and their current state.
    /// </summary>
    public Dictionary<string, BranchState> Branches { get; init; } = new();

    /// <summary>
    /// Last time the repository was updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Whether the repository has been successfully cloned.
    /// </summary>
    public bool IsCloned { get; set; }
}

/// <summary>
/// Represents the state of a tracked branch.
/// </summary>
public sealed class BranchState
{
    /// <summary>
    /// Branch name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Last indexed commit SHA.
    /// </summary>
    public string? LastIndexedSha { get; set; }

    /// <summary>
    /// Current HEAD commit SHA.
    /// </summary>
    public string? CurrentSha { get; set; }

    /// <summary>
    /// Last time this branch was indexed.
    /// </summary>
    public DateTimeOffset? LastIndexed { get; set; }

    /// <summary>
    /// Last time this branch was accessed/queried (for cache eviction).
    /// </summary>
    public DateTimeOffset? LastAccessed { get; set; }

    /// <summary>
    /// Whether this branch needs indexing.
    /// </summary>
    public bool NeedsIndexing => LastIndexedSha != CurrentSha;
}

/// <summary>
/// Represents a file change detected between commits.
/// </summary>
public sealed class FileChange
{
    /// <summary>
    /// Repository name.
    /// </summary>
    public required string RepositoryName { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit SHA where this change occurred.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    /// File path relative to repository root.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Type of change (Added, Modified, Deleted, Renamed).
    /// </summary>
    public required ChangeType ChangeType { get; init; }

    /// <summary>
    /// Old file path (for renames).
    /// </summary>
    public string? OldFilePath { get; init; }
}

/// <summary>
/// Type of file change.
/// </summary>
public enum ChangeType
{
    Added,
    Modified,
    Deleted,
    Renamed
}

