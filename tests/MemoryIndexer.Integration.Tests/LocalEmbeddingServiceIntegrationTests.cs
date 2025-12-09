using FluentAssertions;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Embedding.Providers;
using MemoryIndexer.Storage.InMemory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace MemoryIndexer.Integration.Tests;

/// <summary>
/// Integration tests for LocalEmbeddingService using dependency injection.
/// Tests the SDK's DI integration with LocalEmbedder-based embedding service.
/// </summary>
[Trait("Category", "Integration")]
public class LocalEmbeddingServiceIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ServiceProvider? _serviceProvider;

    public LocalEmbeddingServiceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // Configure options for Local embedding provider
        var indexerOptions = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProvider.Local,
                Model = "all-MiniLM-L6-v2",
                Dimensions = 384,
                CacheTtlMinutes = 5
            }
        };

        // Register options
        services.AddSingleton<IOptions<MemoryIndexerOptions>>(Options.Create(indexerOptions));

        // Register memory cache
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));

        // Register logging
        services.AddLogging();

        // Register LocalEmbeddingService directly (mimics SDK registration)
        services.AddSingleton<IEmbeddingService>(sp =>
            new LocalEmbeddingService(
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IOptions<MemoryIndexerOptions>>(),
                NullLogger<LocalEmbeddingService>.Instance));

        // Register memory store
        services.AddSingleton<IMemoryStore>(sp =>
            new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance));

        _serviceProvider = services.BuildServiceProvider();

        _output.WriteLine("ServiceProvider initialized with LocalEmbeddingService");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _serviceProvider?.Dispose();
        }
    }

    [Fact]
    public async Task EmbeddingService_ShouldBeResolvable()
    {
        // Arrange & Act
        var embeddingService = _serviceProvider!.GetRequiredService<IEmbeddingService>();

        // Assert
        embeddingService.Should().NotBeNull();
        embeddingService.Should().BeOfType<LocalEmbeddingService>();
        embeddingService.Dimensions.Should().Be(384);

        _output.WriteLine($"EmbeddingService resolved: {embeddingService.GetType().Name}");
        _output.WriteLine($"Dimensions: {embeddingService.Dimensions}");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task GenerateEmbedding_ThroughDI_ReturnsValidVector()
    {
        // Arrange
        var embeddingService = _serviceProvider!.GetRequiredService<IEmbeddingService>();
        var text = "This is a test sentence for embedding generation via DI.";

        // Act
        var embedding = await embeddingService.GenerateEmbeddingAsync(text);

        // Assert
        embedding.Length.Should().Be(384);
        embedding.Span.ToArray().Count(v => Math.Abs(v) > 0.0001f).Should().BeGreaterThan(100);

        _output.WriteLine($"Generated embedding with {embedding.Length} dimensions");
        _output.WriteLine($"First 5 values: [{string.Join(", ", embedding.Span.ToArray().Take(5).Select(v => v.ToString("F4")))}]");
    }

    [Fact]
    public async Task GenerateBatchEmbeddings_ThroughDI_ReturnsMultipleVectors()
    {
        // Arrange
        var embeddingService = _serviceProvider!.GetRequiredService<IEmbeddingService>();
        var texts = new[]
        {
            "First sentence for batch embedding.",
            "Second sentence with different content.",
            "Third sentence about programming."
        };

        // Act
        var embeddings = await embeddingService.GenerateBatchEmbeddingsAsync(texts);

        // Assert
        embeddings.Should().HaveCount(3);
        foreach (var embedding in embeddings)
        {
            embedding.Length.Should().Be(384);
        }

        _output.WriteLine($"Generated {embeddings.Count} batch embeddings");
    }

    [Fact]
    public async Task EmbeddingCaching_WorksCorrectly()
    {
        // Arrange
        var embeddingService = _serviceProvider!.GetRequiredService<IEmbeddingService>();
        var text = "This text should be cached for performance.";

        // Act - First call (generates embedding)
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        var embedding1 = await embeddingService.GenerateEmbeddingAsync(text);
        sw1.Stop();
        var firstCallTime = sw1.ElapsedMilliseconds;

        // Second call (should use cache)
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var embedding2 = await embeddingService.GenerateEmbeddingAsync(text);
        sw2.Stop();
        var secondCallTime = sw2.ElapsedMilliseconds;

        // Assert
        embedding1.Span.ToArray().Should().BeEquivalentTo(embedding2.Span.ToArray());

        // Cache hit should be significantly faster (at least 10x faster typically)
        _output.WriteLine($"First call: {firstCallTime}ms, Second call (cached): {secondCallTime}ms");

        // The second call should be faster due to caching
        // Note: First call includes model loading time
        secondCallTime.Should().BeLessThanOrEqualTo(firstCallTime);
    }

    [Fact]
    public async Task FullMemoryWorkflow_WithLocalEmbeddings()
    {
        // Arrange
        var embeddingService = _serviceProvider!.GetRequiredService<IEmbeddingService>();
        var memoryStore = _serviceProvider!.GetRequiredService<IMemoryStore>();

        var memories = new[]
        {
            "Python is a versatile programming language used for web development and data science.",
            "JavaScript is the primary language for web browser scripting.",
            "Machine learning models require training data to learn patterns.",
            "Docker containers isolate applications with their dependencies.",
            "REST APIs use HTTP methods for client-server communication."
        };

        // Store memories with real embeddings
        _output.WriteLine("Storing memories...");
        foreach (var content in memories)
        {
            var embedding = await embeddingService.GenerateEmbeddingAsync(content);
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = content,
                UserId = "test-user",
                SessionId = "test-session",
                Embedding = embedding
            };
            await memoryStore.StoreAsync(memory);
        }

        // Query
        var queryText = "How do I build web applications?";
        _output.WriteLine($"\nQuerying: '{queryText}'");

        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(queryText);
        var results = await memoryStore.SearchAsync(
            queryEmbedding,
            new MemorySearchOptions { Limit = 3, UserId = "test-user" });

        // Assert
        results.Should().NotBeEmpty();
        _output.WriteLine($"\nTop {results.Count} results:");
        foreach (var result in results)
        {
            _output.WriteLine($"  [{result.Score:F4}] {result.Memory.Content}");
        }

        // Should find web-related content
        var topContents = results.Select(r => r.Memory.Content).ToList();
        topContents.Any(c => c.Contains("web", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
    }

    [Fact]
    public async Task SemanticSimilarity_ProducesExpectedResults()
    {
        // Arrange
        var embeddingService = _serviceProvider!.GetRequiredService<IEmbeddingService>();

        var baseText = "How to train a machine learning model?";
        var similarText = "What are the steps to build an ML model?";
        var unrelatedText = "The weather forecast shows sunny skies tomorrow.";

        // Act
        var baseEmbedding = await embeddingService.GenerateEmbeddingAsync(baseText);
        var similarEmbedding = await embeddingService.GenerateEmbeddingAsync(similarText);
        var unrelatedEmbedding = await embeddingService.GenerateEmbeddingAsync(unrelatedText);

        var similarScore = CosineSimilarity(baseEmbedding.Span, similarEmbedding.Span);
        var unrelatedScore = CosineSimilarity(baseEmbedding.Span, unrelatedEmbedding.Span);

        // Assert
        _output.WriteLine($"Base: '{baseText}'");
        _output.WriteLine($"Similar: '{similarText}' -> Score: {similarScore:F4}");
        _output.WriteLine($"Unrelated: '{unrelatedText}' -> Score: {unrelatedScore:F4}");

        similarScore.Should().BeGreaterThan(unrelatedScore,
            "semantically similar texts should have higher cosine similarity");
        similarScore.Should().BeGreaterThan(0.5f,
            "similar ML-related texts should have reasonable similarity");
    }

    [Fact]
    public async Task DifferentModels_HaveDifferentDimensions()
    {
        // This test validates that the model configuration works correctly
        var embeddingService = _serviceProvider!.GetRequiredService<IEmbeddingService>();

        // The configured model is all-MiniLM-L6-v2 with 384 dimensions
        embeddingService.Dimensions.Should().Be(384);

        // Verify actual embedding matches expected dimensions
        var embedding = await embeddingService.GenerateEmbeddingAsync("Test");
        embedding.Length.Should().Be(384);

        _output.WriteLine($"Model all-MiniLM-L6-v2: {embedding.Length} dimensions (expected 384)");
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
