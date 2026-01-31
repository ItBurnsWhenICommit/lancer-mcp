namespace LancerMcp.Services;

public static class EmbeddingJobErrorCodes
{
    public const string ChunkMissing = "chunk_missing";
    public const string ProviderError = "provider_error";
    public const string DimsMismatch = "dims_mismatch";
    public const string MaxAttemptsExceeded = "max_attempts_exceeded";
    public const string UnsupportedTarget = "unsupported_target";
}
