namespace LancerMcp.Models;

/// <summary>
/// Represents a repository in the database.
/// </summary>
public sealed class Repository
{
    /// <summary>
    /// Unique identifier for this repository.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Repository name (unique).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Git remote URL.
    /// </summary>
    public required string RemoteUrl { get; init; }

    /// <summary>
    /// Default branch name (e.g., "main", "master").
    /// </summary>
    public required string DefaultBranch { get; init; }

    /// <summary>
    /// When this repository was created in the database.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this repository was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents a branch in the database.
/// </summary>
public sealed class Branch
{
    /// <summary>
    /// Unique identifier for this branch.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Repository ID.
    /// </summary>
    public required string RepoId { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Current HEAD commit SHA.
    /// </summary>
    public required string HeadCommitSha { get; init; }

    /// <summary>
    /// Indexing state.
    /// </summary>
    public IndexState IndexState { get; init; } = IndexState.Pending;

    /// <summary>
    /// Last successfully indexed commit SHA.
    /// </summary>
    public string? IndexedCommitSha { get; init; }

    /// <summary>
    /// When this branch was last indexed.
    /// </summary>
    public DateTimeOffset? LastIndexedAt { get; init; }

    /// <summary>
    /// When this branch was created in the database.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this branch was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Indexing state for a branch.
/// </summary>
public enum IndexState
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Stale
}

/// <summary>
/// Represents a commit in the database.
/// </summary>
public sealed class Commit
{
    /// <summary>
    /// Unique identifier for this commit.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Repository ID.
    /// </summary>
    public required string RepoId { get; init; }

    /// <summary>
    /// Git commit SHA.
    /// </summary>
    public required string Sha { get; init; }

    /// <summary>
    /// Branch name where this commit was indexed.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit author name.
    /// </summary>
    public required string AuthorName { get; init; }

    /// <summary>
    /// Commit author email.
    /// </summary>
    public required string AuthorEmail { get; init; }

    /// <summary>
    /// Commit message.
    /// </summary>
    public required string CommitMessage { get; init; }

    /// <summary>
    /// When the commit was made.
    /// </summary>
    public required DateTimeOffset CommittedAt { get; init; }

    /// <summary>
    /// When this commit was indexed.
    /// </summary>
    public DateTimeOffset IndexedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Represents a file in the database.
/// </summary>
public sealed class FileMetadata
{
    /// <summary>
    /// Unique identifier for this file.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Repository ID.
    /// </summary>
    public required string RepoId { get; init; }

    /// <summary>
    /// Branch name.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Commit SHA.
    /// </summary>
    public required string CommitSha { get; init; }

    /// <summary>
    /// File path relative to repository root.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Detected programming language.
    /// </summary>
    public required Language Language { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Number of lines in the file.
    /// </summary>
    public required int LineCount { get; init; }

    /// <summary>
    /// When this file was indexed.
    /// </summary>
    public DateTimeOffset IndexedAt { get; init; } = DateTimeOffset.UtcNow;
}

