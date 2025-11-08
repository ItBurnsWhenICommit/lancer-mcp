namespace LancerMcp.Models;

/// <summary>
/// Metadata extracted from a Git commit.
/// </summary>
public sealed class CommitMetadata
{
    /// <summary>
    /// Commit SHA.
    /// </summary>
    public required string Sha { get; init; }

    /// <summary>
    /// Author name.
    /// </summary>
    public required string AuthorName { get; init; }

    /// <summary>
    /// Author email.
    /// </summary>
    public required string AuthorEmail { get; init; }

    /// <summary>
    /// Commit message.
    /// </summary>
    public required string CommitMessage { get; init; }

    /// <summary>
    /// Commit timestamp.
    /// </summary>
    public required DateTimeOffset CommittedAt { get; init; }
}

