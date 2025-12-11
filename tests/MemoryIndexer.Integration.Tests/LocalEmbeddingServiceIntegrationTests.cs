using FluentAssertions;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Integration.Tests.Fixtures;
using MemoryIndexer.Storage.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace MemoryIndexer.Integration.Tests;

/// <summary>
/// Integration tests for LocalEmbeddingService using shared fixture.
/// Tests the SDK's embedding service integration with LocalEmbedder.
/// Uses shared embedding fixture for efficient resource usage.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Heavy")]
[Trait("Category", "LocalModel")]
[Collection(EmbeddingTestCollection.Name)]
public class LocalEmbeddingServiceIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedEmbeddingFixture _fixture;
    private readonly IMemoryStore _memoryStore;

    public LocalEmbeddingServiceIntegrationTests(SharedEmbeddingFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        _memoryStore = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);

        _output.WriteLine($"Using shared embedding service: {_fixture.ModelId}, Dimensions: {_fixture.Dimensions}");
    }

    [Fact]
    public async Task EmbeddingService_ShouldBeResolvable()
    {
        // Assert
        _fixture.EmbeddingService.Should().NotBeNull();
        _fixture.EmbeddingService!.Dimensions.Should().Be(384);

        _output.WriteLine($"EmbeddingService resolved: {_fixture.EmbeddingService.GetType().Name}");
        _output.WriteLine($"Dimensions: {_fixture.EmbeddingService.Dimensions}");

        await Task.CompletedTask;
    }

    [Fact]
    public async Task GenerateEmbedding_ThroughService_ReturnsValidVector()
    {
        // Arrange
        var text = "This is a test sentence for embedding generation via DI.";

        // Act
        var embedding = await _fixture.EmbeddingService!.GenerateEmbeddingAsync(text);

        // Assert
        embedding.Length.Should().Be(384);
        embedding.Span.ToArray().Count(v => Math.Abs(v) > 0.0001f).Should().BeGreaterThan(100);

        _output.WriteLine($"Generated embedding with {embedding.Length} dimensions");
        _output.WriteLine($"First 5 values: [{string.Join(", ", embedding.Span.ToArray().Take(5).Select(v => v.ToString("F4")))}]");
    }

    [Fact]
    public async Task GenerateBatchEmbeddings_ThroughService_ReturnsMultipleVectors()
    {
        // Arrange
        var texts = new[]
        {
            "First sentence for batch embedding.",
            "Second sentence with different content.",
            "Third sentence about programming."
        };

        // Act
        var embeddings = await _fixture.EmbeddingService!.GenerateBatchEmbeddingsAsync(texts);

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
        var text = $"This text should be cached for performance - {Guid.NewGuid()}.";

        // Act - First call (generates embedding)
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        var embedding1 = await _fixture.EmbeddingService!.GenerateEmbeddingAsync(text);
        sw1.Stop();
        var firstCallTime = sw1.ElapsedMilliseconds;

        // Second call (should use cache)
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var embedding2 = await _fixture.EmbeddingService.GenerateEmbeddingAsync(text);
        sw2.Stop();
        var secondCallTime = sw2.ElapsedMilliseconds;

        // Assert
        embedding1.Span.ToArray().Should().BeEquivalentTo(embedding2.Span.ToArray());

        // Cache hit should be significantly faster
        _output.WriteLine($"First call: {firstCallTime}ms, Second call (cached): {secondCallTime}ms");

        // The second call should be faster due to caching
        secondCallTime.Should().BeLessThanOrEqualTo(firstCallTime);
    }

    [Fact]
    public async Task FullMemoryWorkflow_WithLocalEmbeddings()
    {
        // Arrange
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
            var embedding = await _fixture.EmbeddingService!.GenerateEmbeddingAsync(content);
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = content,
                UserId = "test-user",
                SessionId = "test-session",
                Embedding = embedding
            };
            await _memoryStore.StoreAsync(memory);
        }

        // Query
        var queryText = "How do I build web applications?";
        _output.WriteLine($"\nQuerying: '{queryText}'");

        var queryEmbedding = await _fixture.EmbeddingService!.GenerateEmbeddingAsync(queryText);
        var results = await _memoryStore.SearchAsync(
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
        var baseText = "How to train a machine learning model?";
        var similarText = "What are the steps to build an ML model?";
        var unrelatedText = "The weather forecast shows sunny skies tomorrow.";

        // Act
        var baseEmbedding = await _fixture.EmbeddingService!.GenerateEmbeddingAsync(baseText);
        var similarEmbedding = await _fixture.EmbeddingService.GenerateEmbeddingAsync(similarText);
        var unrelatedEmbedding = await _fixture.EmbeddingService.GenerateEmbeddingAsync(unrelatedText);

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
    public async Task ServiceDimensions_MatchModelConfiguration()
    {
        // The configured model is all-MiniLM-L6-v2 with 384 dimensions
        _fixture.EmbeddingService!.Dimensions.Should().Be(384);

        // Verify actual embedding matches expected dimensions
        var embedding = await _fixture.EmbeddingService.GenerateEmbeddingAsync("Test");
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
