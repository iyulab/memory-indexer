using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Core.Tests;

/// <summary>
/// Shared test helper methods for creating test data.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Creates a test memory unit with default values.
    /// </summary>
    public static MemoryUnit CreateTestMemory(
        string userId = "test-user",
        string? content = null,
        MemoryType type = MemoryType.Episodic,
        float importanceScore = 0.7f,
        ReadOnlyMemory<float>? embedding = null)
    {
        return new MemoryUnit
        {
            UserId = userId,
            Content = content ?? "Test memory content",
            Type = type,
            ImportanceScore = importanceScore,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Embedding = embedding
        };
    }

    /// <summary>
    /// Creates a test memory with topics.
    /// </summary>
    public static MemoryUnit CreateTestMemoryWithTopics(
        string content,
        List<string> topics,
        string userId = "test-user")
    {
        return new MemoryUnit
        {
            UserId = userId,
            Content = content,
            Type = MemoryType.Episodic,
            ImportanceScore = 0.7f,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Topics = topics
        };
    }

    /// <summary>
    /// Creates a normalized test embedding vector.
    /// </summary>
    public static ReadOnlyMemory<float> CreateTestEmbedding(int dimensions, int seed = 42)
    {
        var embedding = new float[dimensions];
        var random = new Random(seed);

        for (var i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }

        // Normalize
        var norm = MathF.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < dimensions; i++)
            {
                embedding[i] /= norm;
            }
        }

        return embedding;
    }

    /// <summary>
    /// Creates multiple test memories.
    /// </summary>
    public static List<MemoryUnit> CreateTestMemories(int count, string userId = "test-user", int dimensions = 768)
    {
        var memories = new List<MemoryUnit>();
        for (var i = 0; i < count; i++)
        {
            var memory = CreateTestMemory(
                userId: userId,
                content: $"Test memory content {i}",
                importanceScore: 0.5f + (i * 0.01f),
                embedding: CreateTestEmbedding(dimensions, seed: 42 + i));
            memories.Add(memory);
        }
        return memories;
    }

    /// <summary>
    /// Creates a test memory with a specific ID and content (for migration tests).
    /// </summary>
    public static MemoryUnit CreateTestMemoryWithId(
        string userId,
        string content,
        ReadOnlyMemory<float>? embedding = null)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            Type = MemoryType.Semantic,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Embedding = embedding ?? new float[384]
        };
    }

    /// <summary>
    /// Generates a deterministic mock embedding based on text hash.
    /// </summary>
    public static ReadOnlyMemory<float> GenerateMockEmbedding(string text, int dimensions = 768)
    {
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var embedding = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }
        // Normalize
        var norm = MathF.Sqrt(embedding.Sum(x => x * x));
        if (norm > 0)
        {
            for (var i = 0; i < dimensions; i++)
            {
                embedding[i] /= norm;
            }
        }
        return embedding;
    }

    /// <summary>
    /// Creates a test memory with specific importance score.
    /// </summary>
    public static MemoryUnit CreateTestMemoryWithImportance(
        float importance,
        string content = "Test content",
        int embeddingDimensions = 1024)
    {
        return new MemoryUnit
        {
            Content = content,
            ImportanceScore = importance,
            Embedding = GenerateMockEmbedding(content + importance, embeddingDimensions)
        };
    }
}
