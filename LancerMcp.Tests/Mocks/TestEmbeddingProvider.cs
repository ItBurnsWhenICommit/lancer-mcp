using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LancerMcp.Models;
using LancerMcp.Services;

namespace LancerMcp.Tests.Mocks;

public sealed class TestEmbeddingProvider : IEmbeddingProvider
{
    private readonly bool _isAvailable;

    public TestEmbeddingProvider(bool isAvailable = true)
    {
        _isAvailable = isAvailable;
    }

    public bool IsAvailable => _isAvailable;

    public Task<EmbeddingProviderResult> TryGenerateQueryEmbeddingAsync(string input, CancellationToken cancellationToken)
        => Task.FromResult(new EmbeddingProviderResult(
            IsSuccess: false,
            IsTransientFailure: true,
            ErrorCode: "provider_unavailable",
            ErrorMessage: "Embedding provider unavailable.",
            Dims: null,
            Vector: null,
            Embeddings: Array.Empty<Embedding>()));

    public Task<EmbeddingProviderResult> TryGenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken)
        => Task.FromResult(new EmbeddingProviderResult(
            IsSuccess: false,
            IsTransientFailure: true,
            ErrorCode: "provider_unavailable",
            ErrorMessage: "Embedding provider unavailable.",
            Dims: null,
            Vector: null,
            Embeddings: Array.Empty<Embedding>()));
}
