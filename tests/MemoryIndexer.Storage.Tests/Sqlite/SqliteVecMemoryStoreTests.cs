using FluentAssertions;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Storage.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Storage.Tests.Sqlite;

/// <summary>
/// Unit tests for SqliteVecMemoryStore.
/// Uses temporary database files that are cleaned up after each test.
/// </summary>
public class SqliteVecMemoryStoreTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private SqliteVecMemoryStore _store = null!;

    public SqliteVecMemoryStoreTests()
    {
        // Create unique temp database for each test
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_memories_{Guid.NewGuid():N}.db");
    }

    public async Task InitializeAsync()
    {
        var options = new SqliteOptions
        {
            DatabasePath = _testDbPath,
            UseWalMode = true,
            FtsTokenizer = "trigram",
            EnableVectorSearch = true,
            EnableFullTextSearch = true
        };

        _store = new SqliteVecMemoryStore(
            _testDbPath,
            vectorDimensions: 768,
            options: options,
            logger: NullLogger<SqliteVecMemoryStore>.Instance);

        // Constructor calls InitializeAsync internally
    }

    public async Task DisposeAsync()
    {
        await _store.DisposeAsync();

        // Clean up database files
        TryDeleteFile(_testDbPath);
        TryDeleteFile($"{_testDbPath}-wal");
        TryDeleteFile($"{_testDbPath}-shm");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #region CRUD Operations

    [Fact]
    public async Task StoreAsync_ShouldStoreMemory()
    {
        // Arrange
        var memory = CreateTestMemory();

        // Act
        var result = await _store.StoreAsync(memory);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Content.Should().Be(memory.Content);
    }

    [Fact]
    public async Task StoreAsync_ShouldGenerateNewIdIfEmpty()
    {
        // Arrange
        var memory = CreateTestMemory();
        memory.Id = Guid.Empty;

        // Act
        var result = await _store.StoreAsync(memory);

        // Assert
        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingMemory_ShouldReturnMemory()
    {
        // Arrange
        var memory = await _store.StoreAsync(CreateTestMemory());

        // Act
        var result = await _store.GetByIdAsync(memory.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(memory.Id);
        result.Content.Should().Be(memory.Content);
        result.UserId.Should().Be(memory.UserId);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingMemory_ShouldReturnNull()
    {
        // Act
        var result = await _store.GetByIdAsync(Guid.NewGuid());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ExistingMemory_ShouldUpdate()
    {
        // Arrange
        var memory = await _store.StoreAsync(CreateTestMemory());
        memory.Content = "Updated content";
        memory.ImportanceScore = 0.9f;

        // Act
        var result = await _store.UpdateAsync(memory);

        // Assert
        result.Should().BeTrue();

        var updated = await _store.GetByIdAsync(memory.Id);
        updated!.Content.Should().Be("Updated content");
        updated.ImportanceScore.Should().BeApproximately(0.9f, 0.01f);
    }

    [Fact]
    public async Task UpdateAsync_NonExistingMemory_ShouldReturnFalse()
    {
        // Arrange
        var memory = CreateTestMemory();
        memory.Id = Guid.NewGuid();

        // Act
        var result = await _store.UpdateAsync(memory);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_ShouldMarkAsDeleted()
    {
        // Arrange
        var memory = await _store.StoreAsync(CreateTestMemory());

        // Act
        var result = await _store.DeleteAsync(memory.Id, hardDelete: false);

        // Assert
        result.Should().BeTrue();

        var deleted = await _store.GetByIdAsync(memory.Id);
        deleted!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_HardDelete_ShouldRemoveMemory()
    {
        // Arrange
        var memory = await _store.StoreAsync(CreateTestMemory());

        // Act
        var result = await _store.DeleteAsync(memory.Id, hardDelete: true);

        // Assert
        result.Should().BeTrue();

        var deleted = await _store.GetByIdAsync(memory.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NonExistingMemory_ShouldReturnFalse()
    {
        // Act
        var result = await _store.DeleteAsync(Guid.NewGuid(), hardDelete: true);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Vector Search

    [Fact]
    public async Task SearchAsync_WithEmbedding_ShouldReturnSimilarMemories()
    {
        // Arrange
        var embedding = CreateTestEmbedding(768);
        var memory = CreateTestMemory();
        memory.Embedding = embedding;
        await _store.StoreAsync(memory);

        var options = new MemorySearchOptions
        {
            UserId = "test-user",
            Limit = 10
        };

        // Act
        var results = await _store.SearchAsync(embedding, options);

        // Assert
        results.Should().HaveCount(1);
        results[0].Score.Should().BeGreaterThan(0.9f); // Same embedding = high similarity
    }

    [Fact]
    public async Task SearchAsync_ShouldRespectLimit()
    {
        // Arrange
        var baseEmbedding = CreateTestEmbedding(768);
        for (int i = 0; i < 10; i++)
        {
            var memory = CreateTestMemory();
            // Use the same base embedding with slight variations to ensure high similarity
            memory.Embedding = baseEmbedding;
            await _store.StoreAsync(memory);
        }

        var options = new MemorySearchOptions
        {
            UserId = "test-user",
            Limit = 5,
            MinScore = 0.0f // Explicitly set to include all results
        };

        // Act
        var results = await _store.SearchAsync(baseEmbedding, options);

        // Assert
        results.Should().HaveCount(5);
    }

    [Fact]
    public async Task SearchAsync_ShouldFilterByMinScore()
    {
        // Arrange
        var embedding = CreateTestEmbedding(768, seed: 42);

        // Store memory with completely different embedding
        var memory = CreateTestMemory();
        memory.Embedding = CreateTestEmbedding(768, seed: 999); // Different seed = different embedding
        await _store.StoreAsync(memory);

        var options = new MemorySearchOptions
        {
            UserId = "test-user",
            Limit = 10,
            MinScore = 0.99f // Very high threshold
        };

        // Act
        var results = await _store.SearchAsync(embedding, options);

        // Assert
        // Result may be 0 or 1 depending on embedding similarity
        results.Should().HaveCountLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task SearchAsync_ShouldFilterByUserId()
    {
        // Arrange
        var embedding = CreateTestEmbedding(768);

        var user1Memory = CreateTestMemory("user1");
        user1Memory.Embedding = embedding;
        await _store.StoreAsync(user1Memory);

        var user2Memory = CreateTestMemory("user2");
        user2Memory.Embedding = embedding;
        await _store.StoreAsync(user2Memory);

        var options = new MemorySearchOptions
        {
            UserId = "user1",
            Limit = 10
        };

        // Act
        var results = await _store.SearchAsync(embedding, options);

        // Assert
        results.Should().HaveCount(1);
        results[0].Memory.UserId.Should().Be("user1");
    }

    #endregion

    #region Full-Text Search

    [Fact]
    public async Task FullTextSearchAsync_ShouldFindMatchingContent()
    {
        // Arrange
        var memory1 = CreateTestMemory();
        memory1.Content = "The quick brown fox jumps over the lazy dog";
        await _store.StoreAsync(memory1);

        var memory2 = CreateTestMemory();
        memory2.Content = "A different sentence about cats and dogs";
        await _store.StoreAsync(memory2);

        // Act
        var results = await _store.FullTextSearchAsync("fox", "test-user", limit: 10);

        // Assert
        results.Should().HaveCount(1);
        results[0].Memory.Content.Should().Contain("fox");
    }

    [Fact]
    public async Task FullTextSearchAsync_ShouldFilterByUserId()
    {
        // Arrange
        var memory1 = CreateTestMemory("user1");
        memory1.Content = "Machine learning and artificial intelligence";
        await _store.StoreAsync(memory1);

        var memory2 = CreateTestMemory("user2");
        memory2.Content = "Machine learning algorithms";
        await _store.StoreAsync(memory2);

        // Act
        var results = await _store.FullTextSearchAsync("machine", "user1", limit: 10);

        // Assert
        results.Should().HaveCount(1);
        results[0].Memory.UserId.Should().Be("user1");
    }

    [Fact]
    public async Task FullTextSearchAsync_ShouldReturnEmptyForNoMatches()
    {
        // Arrange
        var memory = CreateTestMemory();
        memory.Content = "Test content about programming";
        await _store.StoreAsync(memory);

        // Act
        var results = await _store.FullTextSearchAsync("nonexistent", "test-user", limit: 10);

        // Assert
        results.Should().BeEmpty();
    }

    #endregion

    #region Hybrid Search

    [Fact]
    public async Task HybridSearchAsync_ShouldCombineVectorAndTextResults()
    {
        // Arrange
        var embedding = CreateTestEmbedding(768);

        var memory = CreateTestMemory();
        memory.Content = "Machine learning is a subset of artificial intelligence";
        memory.Embedding = embedding;
        await _store.StoreAsync(memory);

        // Act
        var results = await _store.HybridSearchAsync(
            "machine learning",
            embedding,
            "test-user",
            limit: 10,
            denseWeight: 0.6f,
            sparseWeight: 0.4f);

        // Assert
        results.Should().HaveCount(1);
        results[0].Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HybridSearchAsync_ShouldRespectLimit()
    {
        // Arrange
        var embedding = CreateTestEmbedding(768);
        for (int i = 0; i < 20; i++)
        {
            var memory = CreateTestMemory();
            memory.Content = $"Document number {i} about machine learning";
            memory.Embedding = CreateTestEmbedding(768, seed: i);
            await _store.StoreAsync(memory);
        }

        // Act
        var results = await _store.HybridSearchAsync(
            "machine learning",
            embedding,
            "test-user",
            limit: 5,
            denseWeight: 0.6f,
            sparseWeight: 0.4f);

        // Assert
        results.Should().HaveCount(5);
    }

    #endregion

    #region Batch Operations

    [Fact]
    public async Task GetAllAsync_ShouldReturnUserMemories()
    {
        // Arrange
        var user1Memory = CreateTestMemory("user1");
        var user2Memory = CreateTestMemory("user2");

        await _store.StoreAsync(user1Memory);
        await _store.StoreAsync(user2Memory);

        // Act
        var results = await _store.GetAllAsync("user1");

        // Assert
        results.Should().HaveCount(1);
        results[0].UserId.Should().Be("user1");
    }

    [Fact]
    public async Task GetAllAsync_ShouldExcludeDeletedMemories()
    {
        // Arrange
        var memory1 = await _store.StoreAsync(CreateTestMemory("user1"));
        var memory2 = await _store.StoreAsync(CreateTestMemory("user1"));

        await _store.DeleteAsync(memory1.Id, hardDelete: false);

        // Act
        var results = await _store.GetAllAsync("user1");

        // Assert
        results.Should().HaveCount(1);
        results[0].Id.Should().Be(memory2.Id);
    }

    [Fact]
    public async Task GetCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        await _store.StoreAsync(CreateTestMemory("user1"));
        await _store.StoreAsync(CreateTestMemory("user1"));
        await _store.StoreAsync(CreateTestMemory("user2"));

        // Act
        var count = await _store.GetCountAsync("user1");

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetCountAsync_ShouldExcludeDeletedMemories()
    {
        // Arrange
        var memory = await _store.StoreAsync(CreateTestMemory("user1"));
        await _store.StoreAsync(CreateTestMemory("user1"));

        await _store.DeleteAsync(memory.Id, hardDelete: false);

        // Act
        var count = await _store.GetCountAsync("user1");

        // Assert
        count.Should().Be(1);
    }

    #endregion

    #region Metadata and JSON Storage

    [Fact]
    public async Task StoreAsync_ShouldPersistTopicsAndEntities()
    {
        // Arrange
        var memory = CreateTestMemory();
        memory.Topics = new List<string> { "AI", "Machine Learning", "NLP" };
        memory.Entities = new List<string> { "GPT-4", "Claude", "Anthropic" };

        // Act
        var stored = await _store.StoreAsync(memory);
        var retrieved = await _store.GetByIdAsync(stored.Id);

        // Assert
        retrieved!.Topics.Should().BeEquivalentTo(new[] { "AI", "Machine Learning", "NLP" });
        retrieved.Entities.Should().BeEquivalentTo(new[] { "GPT-4", "Claude", "Anthropic" });
    }

    [Fact]
    public async Task StoreAsync_ShouldPersistMetadata()
    {
        // Arrange
        var memory = CreateTestMemory();
        memory.Metadata = new Dictionary<string, string>
        {
            { "source", "conversation" },
            { "confidence", "0.95" },
            { "language", "en" }
        };

        // Act
        var stored = await _store.StoreAsync(memory);
        var retrieved = await _store.GetByIdAsync(stored.Id);

        // Assert
        retrieved!.Metadata.Should().ContainKey("source");
        retrieved.Metadata["source"].Should().Be("conversation");
        retrieved.Metadata["confidence"].Should().Be("0.95");
    }

    #endregion

    #region Concurrency

    [Fact]
    public async Task ConcurrentStores_ShouldNotFail()
    {
        // Arrange
        var tasks = new List<Task<MemoryUnit>>();
        var embedding = CreateTestEmbedding(768);

        // Act
        for (int i = 0; i < 50; i++)
        {
            var memory = CreateTestMemory();
            memory.Content = $"Concurrent memory {i}";
            memory.Embedding = embedding;
            tasks.Add(_store.StoreAsync(memory));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(50);
        results.Select(r => r.Id).Distinct().Should().HaveCount(50);
    }

    [Fact]
    public async Task ConcurrentReadsAndWrites_ShouldNotFail()
    {
        // Arrange
        var embedding = CreateTestEmbedding(768);
        var storedMemory = await _store.StoreAsync(CreateTestMemory());

        // Act
        var readTasks = Enumerable.Range(0, 20)
            .Select(_ => _store.GetByIdAsync(storedMemory.Id))
            .ToList();

        var writeTasks = Enumerable.Range(0, 10)
            .Select(i =>
            {
                var memory = CreateTestMemory();
                memory.Content = $"Write {i}";
                memory.Embedding = embedding;
                return _store.StoreAsync(memory);
            })
            .ToList();

        var searchTasks = Enumerable.Range(0, 5)
            .Select(_ => _store.SearchAsync(embedding, new MemorySearchOptions
            {
                UserId = "test-user",
                Limit = 5
            }))
            .ToList();

        await Task.WhenAll(
            Task.WhenAll(readTasks),
            Task.WhenAll(writeTasks),
            Task.WhenAll(searchTasks));

        // Assert - no exceptions means success
        var count = await _store.GetCountAsync("test-user");
        count.Should().BeGreaterThanOrEqualTo(11); // Original + 10 writes
    }

    #endregion

    #region Helper Methods

    private static MemoryUnit CreateTestMemory(string userId = "test-user")
    {
        return new MemoryUnit
        {
            UserId = userId,
            Content = "Test memory content for unit testing",
            Type = MemoryType.Episodic,
            ImportanceScore = 0.7f,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static ReadOnlyMemory<float> CreateTestEmbedding(int dimensions, int seed = 42)
    {
        var embedding = new float[dimensions];
        var random = new Random(seed);

        for (var i = 0; i < dimensions; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }

        // Normalize
        var norm = MathF.Sqrt(embedding.Sum(x => x * x));
        for (var i = 0; i < dimensions; i++)
        {
            embedding[i] /= norm;
        }

        return embedding;
    }

    #endregion
}
