using FluentAssertions;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Embedding.Providers;
using MemoryIndexer.Storage.InMemory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace MemoryIndexer.Integration.Tests;

/// <summary>
/// Integration tests using GPUStack embedding service.
/// These tests require GPUSTACK_URL and GPUSTACK_APIKEY environment variables,
/// and an embedding model available on the GPUStack server.
///
/// Note: As of current testing, GPUStack server has LLM models (gpt-oss, qwen3)
/// but no dedicated embedding models. These tests will skip gracefully when
/// embedding models are not available.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Heavy")]
[Trait("Category", "GpuStack")]
public class GpuStackEmbeddingTests
{
    private readonly ITestOutputHelper _output;
    private readonly string? _gpuStackUrl;
    private readonly string? _gpuStackApiKey;
    private readonly bool _isConfigured;
    private readonly string? _embeddingModel;
    private readonly int _embeddingDimensions;

    public GpuStackEmbeddingTests(ITestOutputHelper output)
    {
        _output = output;

        // Load from environment or .env file
        _gpuStackUrl = Environment.GetEnvironmentVariable("GPUSTACK_URL");
        _gpuStackApiKey = Environment.GetEnvironmentVariable("GPUSTACK_APIKEY");
        _embeddingModel = Environment.GetEnvironmentVariable("GPUSTACK_EMBEDDING_MODEL");

        var dimensionsStr = Environment.GetEnvironmentVariable("GPUSTACK_EMBEDDING_DIMENSIONS");
        _embeddingDimensions = int.TryParse(dimensionsStr, out var dim) ? dim : 1024;

        // Try to load from .env file if not in environment
        if (string.IsNullOrEmpty(_gpuStackUrl))
        {
            var envPath = FindEnvFile();
            if (envPath != null)
            {
                LoadEnvFile(envPath);
                _gpuStackUrl = Environment.GetEnvironmentVariable("GPUSTACK_URL");
                _gpuStackApiKey = Environment.GetEnvironmentVariable("GPUSTACK_APIKEY");
                _embeddingModel = Environment.GetEnvironmentVariable("GPUSTACK_EMBEDDING_MODEL");

                dimensionsStr = Environment.GetEnvironmentVariable("GPUSTACK_EMBEDDING_DIMENSIONS");
                _embeddingDimensions = int.TryParse(dimensionsStr, out dim) ? dim : 1024;
            }
        }

        _isConfigured = !string.IsNullOrEmpty(_gpuStackUrl)
            && !string.IsNullOrEmpty(_gpuStackApiKey)
            && !string.IsNullOrEmpty(_embeddingModel);

        if (_isConfigured)
        {
            _output.WriteLine($"GPUStack URL: {_gpuStackUrl}");
            _output.WriteLine($"GPUStack API Key: {_gpuStackApiKey?[..Math.Min(20, _gpuStackApiKey?.Length ?? 0)]}...");
            _output.WriteLine($"Embedding Model: {_embeddingModel}");
            _output.WriteLine($"Embedding Dimensions: {_embeddingDimensions}");
        }
        else
        {
            _output.WriteLine("GPUStack embedding not configured - tests will be skipped");
            _output.WriteLine($"  URL configured: {!string.IsNullOrEmpty(_gpuStackUrl)}");
            _output.WriteLine($"  API Key configured: {!string.IsNullOrEmpty(_gpuStackApiKey)}");
            _output.WriteLine($"  Embedding Model configured: {!string.IsNullOrEmpty(_embeddingModel)}");
            _output.WriteLine("To enable these tests, set GPUSTACK_EMBEDDING_MODEL environment variable");
        }
    }

    private static string? FindEnvFile()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var envPath = Path.Combine(dir, ".env");
            if (File.Exists(envPath))
                return envPath;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    private static void LoadEnvFile(string path)
    {
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var idx = trimmed.IndexOf('=');
            if (idx > 0)
            {
                var key = trimmed[..idx].Trim();
                var value = trimmed[(idx + 1)..].Trim();
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Configure memory indexer options directly
        var indexerOptions = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProvider.Custom,
                Endpoint = _gpuStackUrl!,
                ApiKey = _gpuStackApiKey,
                Model = _embeddingModel!,
                Dimensions = _embeddingDimensions,
                TimeoutSeconds = 60,
                CacheTtlMinutes = 0 // Disable caching for tests
            }
        };

        // Register options directly (bypass BindConfiguration which needs IConfiguration)
        services.AddSingleton<IOptions<MemoryIndexerOptions>>(Options.Create(indexerOptions));

        // Register memory cache
        services.AddSingleton<IMemoryCache, MemoryCache>();

        // Register HTTP client factory
        services.AddHttpClient();

        // Register embedding service (OpenAI-compatible for GPUStack)
        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MemoryIndexerOptions>>();
            var cache = sp.GetRequiredService<IMemoryCache>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetRequiredService<ILogger<OpenAIEmbeddingService>>();
            return new OpenAIEmbeddingService(
                httpClientFactory.CreateClient(),
                cache,
                options,
                logger);
        });

        // Register in-memory store
        services.AddSingleton<IMemoryStore>(sp =>
            new InMemoryMemoryStore(sp.GetRequiredService<ILogger<InMemoryMemoryStore>>()));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task GenerateEmbedding_WithGpuStack_ReturnsValidVector()
    {
        // Skip if not configured
        if (!_isConfigured)
        {
            _output.WriteLine("Skipping: GPUStack not configured");
            return;
        }

        // Arrange
        using var sp = CreateServiceProvider();
        var embeddingService = sp.GetRequiredService<IEmbeddingService>();
        var testText = "This is a test sentence for embedding generation.";

        // Act
        var embedding = await embeddingService.GenerateEmbeddingAsync(testText);

        // Assert
        embedding.Length.Should().BeGreaterThan(0);
        _output.WriteLine($"Generated embedding with {embedding.Length} dimensions");
        _output.WriteLine($"First 5 values: [{string.Join(", ", embedding.Span.ToArray().Take(5).Select(v => v.ToString("F4")))}]");

        // Verify it's a valid embedding (not all zeros)
        var nonZeroCount = embedding.Span.ToArray().Count(v => Math.Abs(v) > 0.0001f);
        nonZeroCount.Should().BeGreaterThan(embedding.Length / 2);
    }

    [Fact]
    public async Task GenerateBatchEmbeddings_WithGpuStack_ReturnsMultipleVectors()
    {
        if (!_isConfigured)
        {
            _output.WriteLine("Skipping: GPUStack not configured");
            return;
        }

        // Arrange
        using var sp = CreateServiceProvider();
        var embeddingService = sp.GetRequiredService<IEmbeddingService>();
        var testTexts = new[]
        {
            "The quick brown fox jumps over the lazy dog.",
            "Machine learning is transforming the world.",
            "Vector databases enable semantic search.",
            "Natural language processing understands text."
        };

        // Act
        var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(testTexts);

        // Assert
        embeddings.Should().HaveCount(4);
        foreach (var embedding in embeddings)
        {
            embedding.Length.Should().BeGreaterThan(0);
        }

        _output.WriteLine($"Generated {embeddings.Count} embeddings with {embeddings[0].Length} dimensions each");
    }

    [Fact]
    public async Task SimilarTexts_ShouldHaveHigherCosineSimilarity()
    {
        if (!_isConfigured)
        {
            _output.WriteLine("Skipping: GPUStack not configured");
            return;
        }

        // Arrange
        using var sp = CreateServiceProvider();
        var embeddingService = sp.GetRequiredService<IEmbeddingService>();

        var baseText = "How to implement a REST API in Python?";
        var similarText = "What is the best way to create REST APIs using Python Flask?";
        var unrelatedText = "The weather forecast predicts rain tomorrow.";

        // Act
        var baseEmbedding = await embeddingService.GenerateEmbeddingAsync(baseText);
        var similarEmbedding = await embeddingService.GenerateEmbeddingAsync(similarText);
        var unrelatedEmbedding = await embeddingService.GenerateEmbeddingAsync(unrelatedText);

        var similarScore = CosineSimilarity(baseEmbedding.Span, similarEmbedding.Span);
        var unrelatedScore = CosineSimilarity(baseEmbedding.Span, unrelatedEmbedding.Span);

        // Assert
        _output.WriteLine($"Similar text score: {similarScore:F4}");
        _output.WriteLine($"Unrelated text score: {unrelatedScore:F4}");

        similarScore.Should().BeGreaterThan(unrelatedScore,
            "semantically similar texts should have higher cosine similarity");
        similarScore.Should().BeGreaterThan(0.5f,
            "similar texts should have reasonable similarity score");
    }

    [Fact]
    public async Task MemoryStoreAndRecall_EndToEnd_WithRealEmbeddings()
    {
        if (!_isConfigured)
        {
            _output.WriteLine("Skipping: GPUStack not configured");
            return;
        }

        // Arrange
        using var sp = CreateServiceProvider();
        var memoryStore = sp.GetRequiredService<IMemoryStore>();
        var embeddingService = sp.GetRequiredService<IEmbeddingService>();

        // Store memories with real embeddings
        var memories = new[]
        {
            (Guid.NewGuid(), "Python is a popular programming language for data science."),
            (Guid.NewGuid(), "Machine learning models require training data."),
            (Guid.NewGuid(), "REST APIs use HTTP methods like GET, POST, PUT, DELETE."),
            (Guid.NewGuid(), "The Eiffel Tower is located in Paris, France."),
            (Guid.NewGuid(), "Database indexing improves query performance.")
        };

        _output.WriteLine("Storing memories...");
        foreach (var (id, content) in memories)
        {
            var embedding = await embeddingService.GenerateEmbeddingAsync(content);
            var memory = new MemoryIndexer.Core.Models.MemoryUnit
            {
                Id = id,
                Content = content,
                UserId = "test-user",
                SessionId = "test-session",
                Embedding = embedding
            };
            await memoryStore.StoreAsync(memory);
            _output.WriteLine($"  Stored: {id}");
        }

        // Query with a related question
        var queryText = "How do I build a web API?";
        _output.WriteLine($"\nQuerying: '{queryText}'");

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(queryText);
        var searchOptions = new MemoryIndexer.Core.Interfaces.MemorySearchOptions
        {
            Limit = 3,
            UserId = "test-user"
        };
        var results = await memoryStore.SearchAsync(queryEmbedding, searchOptions);

        // Assert
        results.Should().NotBeEmpty();
        _output.WriteLine($"\nTop {results.Count} results:");
        foreach (var result in results)
        {
            _output.WriteLine($"  [{result.Score:F4}] {result.Memory.Id}: {result.Memory.Content}");
        }

        // The REST API memory should be ranked high
        var topResult = results.First();
        topResult.Memory.Content.Should().Contain("API",
            "query about web API should return API-related memory");
    }

    [Fact]
    public async Task MultipleQueries_PerformanceTest()
    {
        if (!_isConfigured)
        {
            _output.WriteLine("Skipping: GPUStack not configured");
            return;
        }

        // Arrange
        using var sp = CreateServiceProvider();
        var embeddingService = sp.GetRequiredService<IEmbeddingService>();

        var queries = new[]
        {
            "What is machine learning?",
            "How does neural network work?",
            "Explain deep learning concepts.",
            "What are transformers in NLP?",
            "How to train a language model?"
        };

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var embeddings = new List<ReadOnlyMemory<float>>();

        foreach (var query in queries)
        {
            var embedding = await embeddingService.GenerateEmbeddingAsync(query);
            embeddings.Add(embedding);
        }

        sw.Stop();

        // Assert
        embeddings.Should().HaveCount(5);
        _output.WriteLine($"Generated {embeddings.Count} embeddings in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average: {sw.ElapsedMilliseconds / embeddings.Count}ms per embedding");

        // Performance check
        var avgMs = sw.ElapsedMilliseconds / embeddings.Count;
        avgMs.Should().BeLessThan(5000, "embedding generation should be reasonably fast");
    }

    [Fact]
    public async Task BatchVsIndividual_PerformanceComparison()
    {
        if (!_isConfigured)
        {
            _output.WriteLine("Skipping: GPUStack not configured");
            return;
        }

        // Arrange
        using var sp = CreateServiceProvider();
        var embeddingService = sp.GetRequiredService<IEmbeddingService>();

        var texts = Enumerable.Range(1, 10)
            .Select(i => $"Sample text number {i} for embedding generation test.")
            .ToArray();

        // Individual embeddings
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        foreach (var text in texts)
        {
            await embeddingService.GenerateEmbeddingAsync(text);
        }
        sw1.Stop();
        var individualTime = sw1.ElapsedMilliseconds;

        // Batch embeddings
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        await embeddingService.GenerateBatchEmbeddingsAsync(texts);
        sw2.Stop();
        var batchTime = sw2.ElapsedMilliseconds;

        // Report
        _output.WriteLine($"Individual requests: {individualTime}ms ({individualTime / texts.Length}ms avg)");
        _output.WriteLine($"Batch request: {batchTime}ms");
        _output.WriteLine($"Batch is {(float)individualTime / batchTime:F2}x faster");

        // Batch should generally be faster or at least not significantly slower
        batchTime.Should().BeLessThan(individualTime * 2,
            "batch processing should not be significantly slower than individual");
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have the same length");

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }
}
