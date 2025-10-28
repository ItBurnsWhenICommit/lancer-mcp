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
public sealed class EmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<ServerOptions> _options;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(
        HttpClient httpClient,
        IOptions<ServerOptions> options,
        ILogger<EmbeddingService> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;

        // Configure HTTP client timeout
        _httpClient.Timeout = TimeSpan.FromSeconds(options.Value.EmbeddingTimeoutSeconds);
    }

    /// <summary>
    /// Generates embeddings for a batch of code chunks.
    /// </summary>
    public async Task<List<Embedding>> GenerateEmbeddingsAsync(IReadOnlyList<CodeChunk> chunks, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.Value.EmbeddingServiceUrl))
        {
            throw new InvalidOperationException(
                "EmbeddingServiceUrl is not configured. Please set it in appsettings.json or environment variables.");
        }

        var embeddings = new List<Embedding>();
        var batchSize = _options.Value.EmbeddingBatchSize;

        // Process chunks in batches
        for (int i = 0; i < chunks.Count; i += batchSize)
        {
            var batch = chunks.Skip(i).Take(batchSize).ToList();
            var batchEmbeddings = await GenerateBatchEmbeddingsAsync(batch, cancellationToken);
            embeddings.AddRange(batchEmbeddings);

            _logger.LogDebug("Generated embeddings for batch {BatchNum}/{TotalBatches} ({Count} chunks)", (i / batchSize) + 1, (chunks.Count + batchSize - 1) / batchSize, batch.Count);
        }

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

            var url = $"{_options.Value.EmbeddingServiceUrl!.TrimEnd('/')}/embed";

            // Send request to TEI
            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
            response.EnsureSuccessStatusCode();

            // Parse response
            var embedResponse = await response.Content.ReadFromJsonAsync<float[][]>(cancellationToken) ?? throw new InvalidOperationException("Received null response from embedding service");

            if (embedResponse.Length != chunks.Count)
            {
                throw new InvalidOperationException($"Expected {chunks.Count} embeddings but received {embedResponse.Length}");
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
                    Model = _options.Value.EmbeddingModel,
                    ModelVersion = "v2"
                });
            }

            return embeddings;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error calling embedding service at {Url}", _options.Value.EmbeddingServiceUrl);
            throw new InvalidOperationException(
                $"Failed to connect to embedding service at {_options.Value.EmbeddingServiceUrl}. " +
                "Make sure the TEI Docker container is running.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embeddings for {Count} chunks", chunks.Count);
            throw;
        }
    }

    /// <summary>
    /// Checks if the embedding service is available and healthy.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.Value.EmbeddingServiceUrl))
        {
            return false;
        }

        try
        {
            var url = $"{_options.Value.EmbeddingServiceUrl.TrimEnd('/')}/health";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets information about the embedding model from the TEI service.
    /// </summary>
    public async Task<ModelInfo?> GetModelInfoAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.Value.EmbeddingServiceUrl))
        {
            return null;
        }

        try
        {
            var url = $"{_options.Value.EmbeddingServiceUrl.TrimEnd('/')}/info";
            var response = await _httpClient.GetAsync(url, cancellationToken);

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

    // Request/Response DTOs for TEI API

    private sealed class EmbedRequest
    {
        [JsonPropertyName("inputs")]
        public required string[] Inputs { get; init; }
    }

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
}

