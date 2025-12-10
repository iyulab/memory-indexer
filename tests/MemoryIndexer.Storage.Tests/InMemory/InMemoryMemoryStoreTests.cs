using FluentAssertions;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Tests;
using MemoryIndexer.Storage.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Storage.Tests.InMemory;

public class InMemoryMemoryStoreTests
{
    private readonly InMemoryMemoryStore _store;

    public InMemoryMemoryStoreTests()
    {
        _store = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
    }

    [Fact]
    public async Task StoreAsync_ShouldStoreMemory()
    {
        // Arrange
        var memory = TestHelpers.CreateTestMemory();

        // Act
        var result = await _store.StoreAsync(memory);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Content.Should().Be(memory.Content);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingMemory_ShouldReturnMemory()
    {
        // Arrange
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory());

        // Act
        var result = await _store.GetByIdAsync(memory.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(memory.Id);
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
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory());
        memory.Content = "Updated content";

        // Act
        var result = await _store.UpdateAsync(memory);

        // Assert
        result.Should().BeTrue();

        var updated = await _store.GetByIdAsync(memory.Id);
        updated!.Content.Should().Be("Updated content");
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_ShouldMarkAsDeleted()
    {
        // Arrange
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory());

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
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory());

        // Act
        var result = await _store.DeleteAsync(memory.Id, hardDelete: true);

        // Assert
        result.Should().BeTrue();

        var deleted = await _store.GetByIdAsync(memory.Id);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnSimilarMemories()
    {
        // Arrange
        var embedding = TestHelpers.CreateTestEmbedding(768);
        var memory = TestHelpers.CreateTestMemory(embedding: embedding);
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
        results[0].Score.Should().BeApproximately(1.0f, 0.01f); // Same embedding = similarity 1.0
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturnUserMemories()
    {
        // Arrange
        var user1Memory = TestHelpers.CreateTestMemory("user1");
        var user2Memory = TestHelpers.CreateTestMemory("user2");

        await _store.StoreAsync(user1Memory);
        await _store.StoreAsync(user2Memory);

        // Act
        var results = await _store.GetAllAsync("user1");

        // Assert
        results.Should().HaveCount(1);
        results[0].UserId.Should().Be("user1");
    }

    [Fact]
    public async Task GetCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        await _store.StoreAsync(TestHelpers.CreateTestMemory("user1"));
        await _store.StoreAsync(TestHelpers.CreateTestMemory("user1"));
        await _store.StoreAsync(TestHelpers.CreateTestMemory("user2"));

        // Act
        var count = await _store.GetCountAsync("user1");

        // Assert
        count.Should().Be(2);
    }
}
