using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ProjectIndexerMcp.Configuration;
using ProjectIndexerMcp.Models;

namespace ProjectIndexerMcp.Services;

/// <summary>
/// Service for generating embeddings using Text Embeddings Inference (TEI).
/// Communicates with a TEI Docker container running jina-embeddings-v2-base-code.
/// </summary>
public sealed class EmbeddingService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<ServerOptions> _options;
    private readonly ILogger<EmbeddingService> _logger;
    private bool _disposed;

    public EmbeddingService(
        HttpClient httpClient,
        IOptionsMonitor<ServerOptions> options,
        ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Generates embeddings for a batch of code chunks.
    /// </summary>
    public async Task<List<Embedding>> GenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return new List<Embedding>();
        }

        if (string.IsNullOrEmpty(_options.CurrentValue.EmbeddingServiceUrl))
        {
            throw new InvalidOperationException(
                "EmbeddingServiceUrl is not configured. Please set it in appsettings.json or environment variables.");
        }

        _logger.LogInformation("Generating embeddings for {Count} chunks", chunks.Count);

        var embeddings = new List<Embedding>();
        var batchSize = _options.CurrentValue.EmbeddingBatchSize;

        // Process chunks in batches
        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize).ToList();
            var batchEmbeddings = await GenerateBatchEmbeddingsAsync(batch, cancellationToken);
            embeddings.AddRange(batchEmbeddings);

            _logger.LogDebug("Generated embeddings for batch {BatchNum}/{TotalBatches} ({Count} chunks)",
                (i / batchSize) + 1, (chunks.Count + batchSize - 1) / batchSize, batch.Count);
        }

        _logger.LogInformation(
            "Successfully generated {Count} embeddings using model {Model}",
            embeddings.Count,
            _options.CurrentValue.EmbeddingModel);

        return embeddings;
    }

    /// <summary>
    /// Generates embeddings for a single batch of chunks.
    /// </summary>
    private async Task<List<Embedding>> GenerateBatchEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken)
    {
        try
        {
            // Prepare request payload
            var request = new EmbedRequest
            {
                Inputs = chunks.Select(c => c.Content).ToArray()
            };

            // Send request to TEI (using BaseAddress configured in DI)
            var response = await _httpClient.PostAsJsonAsync("/embed", request, cancellationToken);

            // Handle 413 Payload Too Large by splitting the batch
            if (response.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge)
            {
                if (chunks.Count == 1)
                {
                    // Single chunk is too large, skip it
                    _logger.LogWarning(
                        "Chunk is too large for embedding service ({Size} chars), skipping: {ChunkId}",
                        chunks[0].Content.Length, chunks[0].Id);
                    return new List<Embedding>();
                }

                // Split batch in half and retry
                _logger.LogWarning(
                    "Batch of {Count} chunks too large (413), splitting into smaller batches",
                    chunks.Count);

                var mid = chunks.Count / 2;
                var firstHalf = chunks.Take(mid).ToList();
                var secondHalf = chunks.Skip(mid).ToList();

                var splitEmbeddings = new List<Embedding>();
                splitEmbeddings.AddRange(await GenerateBatchEmbeddingsAsync(firstHalf, cancellationToken));
                splitEmbeddings.AddRange(await GenerateBatchEmbeddingsAsync(secondHalf, cancellationToken));
                return splitEmbeddings;
            }

            response.EnsureSuccessStatusCode();

            // Parse response
            var embedResponse = await response.Content.ReadFromJsonAsync<float[][]>(cancellationToken)
                ?? throw new InvalidOperationException("Embedding service returned null response");

            if (embedResponse.Length != chunks.Count)
            {
                throw new InvalidOperationException(
                    $"Embedding count mismatch: expected {chunks.Count}, got {embedResponse.Length}");
            }

            // Create Embedding objects
            var embeddings = new List<Embedding>();
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var vector = embedResponse[i];

                embeddings.Add(new Embedding
                {
                    ChunkId = chunk.Id,
                    RepositoryName = chunk.RepositoryName,
                    BranchName = chunk.BranchName,
                    CommitSha = chunk.CommitSha,
                    Vector = vector,
                    Model = _options.CurrentValue.EmbeddingModel,
                    ModelVersion = "v2"
                });
            }

            return embeddings;
        }
        catch (HttpRequestException ex)
        {
            var serviceUrl = _options.CurrentValue.EmbeddingServiceUrl;
            _logger.LogError(ex, "Failed to communicate with embedding service at {Url}", serviceUrl);
            throw new InvalidOperationException(
                $"Embedding service unavailable at {serviceUrl}. " +
                "Make sure the TEI Docker container is running.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate embeddings for {Count} chunks", chunks.Count);
            throw;
        }
    }

    /// <summary>
    /// Checks if the embedding service is available and healthy.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.CurrentValue.EmbeddingServiceUrl))
        {
            return false;
        }

        try
        {
            var response = await _httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding service health check failed");
            return false;
        }
    }

    /// <summary>
    /// Gets information about the embedding model from the TEI service.
    /// </summary>
    public async Task<ModelInfo?> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.CurrentValue.EmbeddingServiceUrl))
        {
            return null;
        }

        try
        {
            var response = await _httpClient.GetAsync("/info", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ModelInfo>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get model info from embedding service");
            return null;
        }
    }

    /// <summary>
    /// Disposes the HTTP client resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient?.Dispose();
        _disposed = true;
    }

    #region DTOs for TEI API communication

    /// <summary>
    /// Request payload for TEI /embed endpoint.
    /// </summary>
    private sealed class EmbedRequest
    {
        [JsonPropertyName("inputs")]
        public required string[] Inputs { get; init; }
    }

    /// <summary>
    /// Response from TEI /embed endpoint.
    /// TEI returns a simple array of arrays, but we can also accept structured responses.
    /// </summary>
    private sealed class EmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<List<float>>? Embeddings { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("dimension")]
        public int? Dimension { get; init; }

        [JsonPropertyName("count")]
        public int? Count { get; init; }
    }

    /// <summary>
    /// Response from TEI /info endpoint containing model metadata.
    /// </summary>
    public sealed class ModelInfo
    {
        [JsonPropertyName("model_id")]
        public string? ModelId { get; init; }

        [JsonPropertyName("model_sha")]
        public string? ModelSha { get; init; }

        [JsonPropertyName("model_dtype")]
        public string? ModelDtype { get; init; }

        [JsonPropertyName("model_type")]
        public string? ModelType { get; init; }

        [JsonPropertyName("max_concurrent_requests")]
        public int MaxConcurrentRequests { get; init; }

        [JsonPropertyName("max_input_length")]
        public int MaxInputLength { get; init; }

        [JsonPropertyName("max_batch_tokens")]
        public int MaxBatchTokens { get; init; }

        [JsonPropertyName("max_batch_requests")]
        public int? MaxBatchRequests { get; init; }

        [JsonPropertyName("max_client_batch_size")]
        public int MaxClientBatchSize { get; init; }

        [JsonPropertyName("tokenization_workers")]
        public int TokenizationWorkers { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }
    }

    #endregion
}
