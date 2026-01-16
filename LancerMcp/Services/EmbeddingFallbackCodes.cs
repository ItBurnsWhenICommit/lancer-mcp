namespace LancerMcp.Services;

public static class EmbeddingFallbackCodes
{
    public const string EmbeddingsDisabled = "embeddings_disabled";
    public const string ProviderUnavailable = "embedding_provider_unavailable";
    public const string MissingQueryEmbedding = "missing_query_embedding";
    public const string QueryEmbeddingInvalid = "query_embedding_invalid";
}
