using System.ClientModel;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Embeddings;

namespace SharedLib.Embedding;

/// <summary>
/// OpenAI embedding service implementation for samples and tests.
/// Not included in the main MemoryIndexer packages.
/// Includes built-in embedding cache to reduce API calls.
/// </summary>
public sealed class OpenAIEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAIEmbeddingService> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _cacheTtl;
    private readonly int _maxCacheSize;

    /// <summary>
    /// Supported OpenAI embedding models and their dimensions.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> SupportedModels = new Dictionary<string, int>
    {
        ["text-embedding-3-small"] = 1536,
        ["text-embedding-3-large"] = 3072,
        ["text-embedding-ada-002"] = 1536
    };

    /// <summary>
    /// Gets the dimension of embeddings produced by this service.
    /// </summary>
    public int Dimensions { get; }

    /// <summary>
    /// Creates an OpenAI embedding service with the specified API key and model.
    /// </summary>
    /// <param name="apiKey">OpenAI API key.</param>
    /// <param name="model">Model name (e.g., "text-embedding-3-small").</param>
    /// <param name="dimensions">Optional dimension override for models that support it.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="endpoint">Optional custom endpoint for Azure OpenAI or compatible APIs.</param>
    /// <param name="cacheTtl">Cache TTL for embeddings (default: 30 minutes).</param>
    /// <param name="maxCacheSize">Maximum cache entries (default: 1000).</param>
    public OpenAIEmbeddingService(
        string apiKey,
        string model = "text-embedding-3-small",
        int? dimensions = null,
        ILogger<OpenAIEmbeddingService>? logger = null,
        Uri? endpoint = null,
        TimeSpan? cacheTtl = null,
        int maxCacheSize = 1000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey, nameof(apiKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(model, nameof(model));

        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenAIEmbeddingService>.Instance;
        _cacheTtl = cacheTtl ?? TimeSpan.FromMinutes(30);
        _maxCacheSize = maxCacheSize;

        var credential = new ApiKeyCredential(apiKey);
        if (endpoint != null)
        {
            var options = new OpenAIClientOptions { Endpoint = endpoint };
            var client = new OpenAIClient(credential, options);
            _client = client.GetEmbeddingClient(model);
        }
        else
        {
            _client = new EmbeddingClient(model, credential);
        }

        // Determine dimensions
        if (dimensions.HasValue)
        {
            Dimensions = dimensions.Value;
        }
        else if (SupportedModels.TryGetValue(model, out var defaultDimensions))
        {
            Dimensions = defaultDimensions;
        }
        else
        {
            Dimensions = 1536; // Default fallback
            _logger.LogWarning("Unknown model '{Model}', using default dimensions {Dimensions}", model, Dimensions);
        }

        _logger.LogInformation("OpenAI embedding service initialized: model={Model}, dimensions={Dimensions}, cacheTtl={CacheTtl}",
            model, Dimensions, _cacheTtl);
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));

        var cacheKey = ComputeCacheKey(text);

        // Check cache
        if (_cache.TryGetValue(cacheKey, out var entry) && !entry.IsExpired)
        {
            _logger.LogDebug("Cache hit for embedding (key={Key})", cacheKey[..8]);
            return entry.Embedding;
        }

        // Generate embedding
        var response = await _client.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
        var embedding = response.Value.ToFloats();

        // Cache result
        CacheEmbedding(cacheKey, embedding);

        return embedding;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchEmbeddingsAsync(
        IEnumerable<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts, nameof(texts));

        var textList = texts.ToList();
        if (textList.Count == 0)
            return [];

        // Check cache for each text
        var results = new ReadOnlyMemory<float>[textList.Count];
        var uncachedIndices = new List<int>();
        var uncachedTexts = new List<string>();

        for (int i = 0; i < textList.Count; i++)
        {
            var cacheKey = ComputeCacheKey(textList[i]);
            if (_cache.TryGetValue(cacheKey, out var entry) && !entry.IsExpired)
            {
                results[i] = entry.Embedding;
            }
            else
            {
                uncachedIndices.Add(i);
                uncachedTexts.Add(textList[i]);
            }
        }

        // Generate embeddings for uncached texts
        if (uncachedTexts.Count > 0)
        {
            var response = await _client.GenerateEmbeddingsAsync(uncachedTexts, cancellationToken: cancellationToken);
            var embeddings = response.Value.OrderBy(e => e.Index).Select(e => e.ToFloats()).ToList();

            for (int i = 0; i < uncachedIndices.Count; i++)
            {
                var originalIndex = uncachedIndices[i];
                var embedding = embeddings[i];
                results[originalIndex] = embedding;

                // Cache result
                var cacheKey = ComputeCacheKey(textList[originalIndex]);
                CacheEmbedding(cacheKey, embedding);
            }
        }

        _logger.LogDebug("Batch embedding: {Total} total, {Cached} cached, {Generated} generated",
            textList.Count, textList.Count - uncachedTexts.Count, uncachedTexts.Count);

        return results;
    }

    private static string ComputeCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash);
    }

    private void CacheEmbedding(string key, ReadOnlyMemory<float> embedding)
    {
        // Simple eviction: if at capacity, remove oldest entries
        if (_cache.Count >= _maxCacheSize)
        {
            var oldestKeys = _cache
                .OrderBy(kvp => kvp.Value.CreatedAt)
                .Take(_cache.Count / 4) // Remove 25%
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var oldKey in oldestKeys)
            {
                _cache.TryRemove(oldKey, out _);
            }

            _logger.LogDebug("Cache eviction: removed {Count} entries", oldestKeys.Count);
        }

        _cache[key] = new CacheEntry(embedding, _cacheTtl);
    }

    private sealed class CacheEntry
    {
        public ReadOnlyMemory<float> Embedding { get; }
        public DateTime CreatedAt { get; }
        public DateTime ExpiresAt { get; }
        public bool IsExpired => DateTime.UtcNow > ExpiresAt;

        public CacheEntry(ReadOnlyMemory<float> embedding, TimeSpan ttl)
        {
            Embedding = embedding;
            CreatedAt = DateTime.UtcNow;
            ExpiresAt = CreatedAt + ttl;
        }
    }
}
