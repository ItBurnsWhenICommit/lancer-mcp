namespace LancerMcp.Models;

public enum EmbeddingJobStatus
{
    Pending,
    Processing,
    Completed,
    Blocked
}

public sealed class EmbeddingJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public required string RepositoryName { get; init; }
    public required string BranchName { get; init; }
    public required string CommitSha { get; init; }
    public required string TargetKind { get; init; }
    public required string TargetId { get; init; }
    public required string Model { get; init; }
    public int? Dims { get; set; }
    public EmbeddingJobStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
