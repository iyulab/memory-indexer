using FluentAssertions;
using LocalEmbedder;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Integration.Tests.Fixtures;
using MemoryIndexer.Storage.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace MemoryIndexer.Integration.Tests;

/// <summary>
/// Integration tests using LocalEmbedder for real embedding generation.
/// These tests verify the full memory store workflow with actual vector embeddings.
/// Uses shared embedding fixture for efficient resource usage.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Heavy")]
[Trait("Category", "LocalModel")]
[Collection(EmbeddingTestCollection.Name)]
public class LocalEmbeddingIntegrationTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedEmbeddingFixture _fixture;
    private readonly IMemoryStore _memoryStore;

    public LocalEmbeddingIntegrationTests(SharedEmbeddingFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        _memoryStore = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);

        _output.WriteLine($"Using shared embedding model: {_fixture.ModelId}, Dimensions: {_fixture.Dimensions}");
    }

    [Fact]
    public async Task GenerateEmbedding_ReturnsValidVector()
    {
        // Arrange
        var text = "This is a test sentence for embedding generation.";

        // Act
        var embedding = await _fixture.EmbeddingModel!.EmbedAsync(text);

        // Assert
        embedding.Should().NotBeNull();
        embedding.Length.Should().Be(384); // MiniLM-L6-v2 outputs 384 dimensions
        _output.WriteLine($"Generated embedding with {embedding.Length} dimensions");
        _output.WriteLine($"First 5 values: [{string.Join(", ", embedding.Take(5).Select(v => v.ToString("F4")))}]");

        // Verify it's a valid embedding (not all zeros)
        var nonZeroCount = embedding.Count(v => Math.Abs(v) > 0.0001f);
        nonZeroCount.Should().BeGreaterThan(embedding.Length / 2);
    }

    [Fact]
    public async Task GenerateBatchEmbeddings_ReturnsMultipleVectors()
    {
        // Arrange
        var texts = new[]
        {
            "The quick brown fox jumps over the lazy dog.",
            "Machine learning is transforming the world.",
            "Vector databases enable semantic search.",
            "Natural language processing understands text."
        };

        // Act
        var embeddings = await _fixture.EmbeddingModel!.EmbedAsync(texts);

        // Assert
        embeddings.Should().HaveCount(4);
        foreach (var embedding in embeddings)
        {
            embedding.Length.Should().Be(384);
        }
        _output.WriteLine($"Generated {embeddings.Length} embeddings with {embeddings[0].Length} dimensions each");
    }

    [Fact]
    public async Task SimilarTexts_ShouldHaveHigherCosineSimilarity()
    {
        // Arrange
        var baseText = "How to implement a REST API in Python?";
        var similarText = "What is the best way to create REST APIs using Python Flask?";
        var unrelatedText = "The weather forecast predicts rain tomorrow.";

        // Act
        var baseEmbedding = await _fixture.EmbeddingModel!.EmbedAsync(baseText);
        var similarEmbedding = await _fixture.EmbeddingModel.EmbedAsync(similarText);
        var unrelatedEmbedding = await _fixture.EmbeddingModel.EmbedAsync(unrelatedText);

        var similarScore = LocalEmbedder.LocalEmbedder.CosineSimilarity(baseEmbedding, similarEmbedding);
        var unrelatedScore = LocalEmbedder.LocalEmbedder.CosineSimilarity(baseEmbedding, unrelatedEmbedding);

        // Assert
        _output.WriteLine($"Base: '{baseText}'");
        _output.WriteLine($"Similar: '{similarText}' -> Score: {similarScore:F4}");
        _output.WriteLine($"Unrelated: '{unrelatedText}' -> Score: {unrelatedScore:F4}");

        similarScore.Should().BeGreaterThan(unrelatedScore,
            "semantically similar texts should have higher cosine similarity");
        similarScore.Should().BeGreaterThan(0.5f,
            "similar texts should have reasonable similarity score");
    }

    [Fact]
    public async Task MemoryStoreAndSearch_EndToEnd_WithRealEmbeddings()
    {
        // Arrange - Store memories with real embeddings
        var memories = new[]
        {
            ("Python is a popular programming language for data science and AI."),
            ("Machine learning models require large amounts of training data."),
            ("REST APIs use HTTP methods like GET, POST, PUT, and DELETE."),
            ("The Eiffel Tower is a famous landmark located in Paris, France."),
            ("Database indexing significantly improves query performance.")
        };

        _output.WriteLine("Storing memories with real embeddings...");
        var storedIds = new List<Guid>();

        foreach (var content in memories)
        {
            var embedding = await _fixture.EmbeddingModel!.EmbedAsync(content);
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = content,
                UserId = "test-user",
                SessionId = "test-session",
                Embedding = embedding
            };
            await _memoryStore.StoreAsync(memory);
            storedIds.Add(memory.Id);
            _output.WriteLine($"  Stored: {content[..Math.Min(50, content.Length)]}...");
        }

        // Act - Query with a related question
        var queryText = "How do I build a web API?";
        _output.WriteLine($"\nQuerying: '{queryText}'");

        var queryEmbedding = await _fixture.EmbeddingModel!.EmbedAsync(queryText);
        var searchOptions = new MemorySearchOptions
        {
            Limit = 3,
            UserId = "test-user"
        };
        var results = await _memoryStore.SearchAsync(queryEmbedding, searchOptions);

        // Assert
        results.Should().NotBeEmpty();
        _output.WriteLine($"\nTop {results.Count} results:");
        foreach (var result in results)
        {
            _output.WriteLine($"  [{result.Score:F4}] {result.Memory.Content}");
        }

        // The REST API memory should be ranked high
        var topResult = results.First();
        topResult.Memory.Content.Should().Contain("API",
            "query about web API should return API-related memory first");
    }

    [Fact]
    public async Task SemanticSearch_MultipleQueries_ReturnsRelevantResults()
    {
        // Arrange - Store diverse memories
        var memories = new[]
        {
            "C# is a strongly typed programming language developed by Microsoft.",
            "JavaScript is the most popular language for web development.",
            "Docker containers package applications with their dependencies.",
            "Kubernetes orchestrates container deployment and scaling.",
            "PostgreSQL is a powerful open-source relational database.",
            "MongoDB is a NoSQL document database for flexible schemas.",
            "Git is a distributed version control system for tracking changes.",
            "CI/CD pipelines automate software testing and deployment."
        };

        foreach (var content in memories)
        {
            var embedding = await _fixture.EmbeddingModel!.EmbedAsync(content);
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = content,
                UserId = "test-user",
                Embedding = embedding
            };
            await _memoryStore.StoreAsync(memory);
        }

        // Test multiple queries
        var queries = new[]
        {
            ("What programming languages should I learn?", new[] { "C#", "JavaScript" }),
            ("How do I containerize my application?", new[] { "Docker", "Kubernetes" }),
            ("Which database should I use?", new[] { "PostgreSQL", "MongoDB" })
        };

        foreach (var (query, expectedKeywords) in queries)
        {
            var queryEmbedding = await _fixture.EmbeddingModel!.EmbedAsync(query);
            var results = await _memoryStore.SearchAsync(
                queryEmbedding,
                new MemorySearchOptions { Limit = 2, UserId = "test-user" });

            _output.WriteLine($"\nQuery: '{query}'");
            foreach (var result in results)
            {
                _output.WriteLine($"  [{result.Score:F4}] {result.Memory.Content}");
            }

            // At least one result should contain expected keywords
            var hasRelevantResult = results.Any(r =>
                expectedKeywords.Any(kw => r.Memory.Content.Contains(kw, StringComparison.OrdinalIgnoreCase)));

            hasRelevantResult.Should().BeTrue(
                $"query '{query}' should return results containing one of: {string.Join(", ", expectedKeywords)}");
        }
    }

    [Fact]
    public async Task DuplicateDetection_ByEmbeddingSimilarity()
    {
        // Arrange - Store a base memory
        var originalContent = "Machine learning is a subset of artificial intelligence.";
        var originalEmbedding = await _fixture.EmbeddingModel!.EmbedAsync(originalContent);

        var originalMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = originalContent,
            UserId = "test-user",
            Embedding = originalEmbedding
        };
        await _memoryStore.StoreAsync(originalMemory);

        // Test variations
        var variations = new[]
        {
            ("Machine learning is part of AI and involves training models.", true),  // Similar
            ("ML is a subset of AI technology.", true),                               // Similar
            ("The weather in Paris is usually mild.", false),                         // Different
            ("I like to eat pizza for dinner.", false)                                // Different
        };

        _output.WriteLine($"Original: '{originalContent}'");
        _output.WriteLine("\nTesting variations:");

        foreach (var (text, expectedSimilar) in variations)
        {
            var embedding = await _fixture.EmbeddingModel!.EmbedAsync(text);
            var similarity = LocalEmbedder.LocalEmbedder.CosineSimilarity(originalEmbedding, embedding);

            var isSimilar = similarity > 0.6f; // Threshold for "similar"
            _output.WriteLine($"  [{similarity:F4}] {(isSimilar ? "SIMILAR" : "DIFFERENT")} - '{text}'");

            if (expectedSimilar)
            {
                similarity.Should().BeGreaterThan(0.5f,
                    $"'{text}' should be similar to original");
            }
            else
            {
                similarity.Should().BeLessThan(0.5f,
                    $"'{text}' should be different from original");
            }
        }
    }

    [Fact]
    public async Task UpdateMemory_RecalculatesEmbedding()
    {
        // Arrange - Store initial memory
        var initialContent = "Python is great for scripting.";
        var initialEmbedding = await _fixture.EmbeddingModel!.EmbedAsync(initialContent);

        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = initialContent,
            UserId = "test-user",
            Embedding = initialEmbedding
        };
        await _memoryStore.StoreAsync(memory);

        // Act - Update content and re-embed
        var updatedContent = "Python is excellent for machine learning and data science.";
        var updatedEmbedding = await _fixture.EmbeddingModel!.EmbedAsync(updatedContent);

        memory.Content = updatedContent;
        memory.Embedding = updatedEmbedding;
        await _memoryStore.UpdateAsync(memory);

        // Assert - Search should find the updated content
        var queryEmbedding = await _fixture.EmbeddingModel!.EmbedAsync("What is good for data science?");
        var results = await _memoryStore.SearchAsync(
            queryEmbedding,
            new MemorySearchOptions { Limit = 1, UserId = "test-user" });

        results.Should().HaveCount(1);
        results[0].Memory.Content.Should().Be(updatedContent);
        _output.WriteLine($"Query: 'What is good for data science?'");
        _output.WriteLine($"Result: [{results[0].Score:F4}] {results[0].Memory.Content}");
    }

    [Fact]
    public async Task PerformanceTest_BatchEmbeddingGeneration()
    {
        // Arrange
        var texts = Enumerable.Range(1, 20)
            .Select(i => $"Sample text number {i} for testing batch embedding generation performance.")
            .ToArray();

        // Act - Individual embeddings
        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        var individualEmbeddings = new List<float[]>();
        foreach (var text in texts)
        {
            individualEmbeddings.Add(await _fixture.EmbeddingModel!.EmbedAsync(text));
        }
        sw1.Stop();
        var individualTime = sw1.ElapsedMilliseconds;

        // Act - Batch embeddings
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        var batchEmbeddings = await _fixture.EmbeddingModel!.EmbedAsync(texts);
        sw2.Stop();
        var batchTime = sw2.ElapsedMilliseconds;

        // Assert
        individualEmbeddings.Should().HaveCount(20);
        batchEmbeddings.Should().HaveCount(20);

        _output.WriteLine($"Individual requests ({texts.Length} texts): {individualTime}ms ({individualTime / texts.Length}ms avg)");
        _output.WriteLine($"Batch request ({texts.Length} texts): {batchTime}ms");
        _output.WriteLine($"Batch is {(float)individualTime / Math.Max(batchTime, 1):F2}x faster");

        // Batch should generally be faster
        batchTime.Should().BeLessThanOrEqualTo(individualTime,
            "batch processing should not be slower than individual");
    }

    [Fact]
    public async Task FullWorkflow_StoreSearchUpdateDelete()
    {
        // 1. Store
        var content = "Kubernetes is a container orchestration platform.";
        var embedding = await _fixture.EmbeddingModel!.EmbedAsync(content);
        var memory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = content,
            UserId = "workflow-user",
            ImportanceScore = 0.8f,
            Embedding = embedding
        };

        await _memoryStore.StoreAsync(memory);
        _output.WriteLine($"1. Stored: {memory.Id}");

        // 2. Search
        var queryEmbedding = await _fixture.EmbeddingModel!.EmbedAsync("container orchestration");
        var searchResults = await _memoryStore.SearchAsync(
            queryEmbedding,
            new MemorySearchOptions { UserId = "workflow-user", Limit = 1 });

        searchResults.Should().HaveCount(1);
        searchResults[0].Memory.Id.Should().Be(memory.Id);
        _output.WriteLine($"2. Search found: [{searchResults[0].Score:F4}]");

        // 3. Get by ID
        var retrieved = await _memoryStore.GetByIdAsync(memory.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be(content);
        _output.WriteLine($"3. Retrieved by ID: {retrieved.Id}");

        // 4. Update
        retrieved.Content = "Kubernetes (K8s) is the leading container orchestration platform.";
        retrieved.Embedding = await _fixture.EmbeddingModel.EmbedAsync(retrieved.Content);
        var updated = await _memoryStore.UpdateAsync(retrieved);
        updated.Should().BeTrue();
        _output.WriteLine($"4. Updated content");

        // 5. Verify update
        var updatedMemory = await _memoryStore.GetByIdAsync(memory.Id);
        updatedMemory!.Content.Should().Contain("K8s");
        _output.WriteLine($"5. Verified update: {updatedMemory.Content}");

        // 6. Delete
        var deleted = await _memoryStore.DeleteAsync(memory.Id, hardDelete: true);
        deleted.Should().BeTrue();
        _output.WriteLine($"6. Deleted memory");

        // 7. Verify deletion
        var afterDelete = await _memoryStore.GetByIdAsync(memory.Id);
        afterDelete.Should().BeNull();
        _output.WriteLine($"7. Verified deletion");
    }
}
