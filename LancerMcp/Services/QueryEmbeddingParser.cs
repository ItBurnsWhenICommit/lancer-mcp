using System.Buffers.Binary;

namespace LancerMcp.Services;

public sealed record QueryEmbeddingParseResult(
    bool Success,
    string? ErrorCode,
    string? Error,
    float[]? Vector,
    string? Model);

public static class QueryEmbeddingParser
{
    public static QueryEmbeddingParseResult TryParse(
        string? base64,
        int? dims,
        string? model,
        int maxDims)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return new QueryEmbeddingParseResult(
                false,
                "missing_query_embedding",
                "Query embedding not provided.",
                null,
                null);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return new QueryEmbeddingParseResult(
                false,
                "invalid_query_embedding",
                "Query embedding is not valid base64.",
                null,
                null);
        }

        if (bytes.Length == 0 || bytes.Length % 4 != 0)
        {
            return new QueryEmbeddingParseResult(
                false,
                "invalid_query_embedding",
                "Query embedding length is invalid.",
                null,
                null);
        }

        var inferredDims = bytes.Length / 4;
        if (dims.HasValue && dims.Value != inferredDims)
        {
            return new QueryEmbeddingParseResult(
                false,
                "invalid_query_embedding_dims",
                "Query embedding dims mismatch.",
                null,
                null);
        }

        if (inferredDims <= 0 || inferredDims > maxDims)
        {
            return new QueryEmbeddingParseResult(
                false,
                "invalid_query_embedding_dims",
                "Query embedding dims out of range.",
                null,
                null);
        }

        var vector = new float[inferredDims];
        for (var i = 0; i < inferredDims; i++)
        {
            var offset = i * 4;
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset, 4));
        }

        return new QueryEmbeddingParseResult(
            true,
            null,
            null,
            vector,
            model?.Trim().ToLowerInvariant());
    }
}
