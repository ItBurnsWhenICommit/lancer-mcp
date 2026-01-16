using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LancerMcp.Models;

namespace LancerMcp.Services;

public interface IEmbeddingProvider
{
    bool IsAvailable { get; }

    Task<EmbeddingProviderResult> TryGenerateQueryEmbeddingAsync(string input, CancellationToken cancellationToken);

    Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken);
}

public sealed record EmbeddingProviderResult(
    bool IsSuccess,
    bool IsTransientFailure,
    string? ErrorCode,
    string? ErrorMessage,
    int? Dims,
    float[]? Vector,
    IReadOnlyList<Embedding> Embeddings);
