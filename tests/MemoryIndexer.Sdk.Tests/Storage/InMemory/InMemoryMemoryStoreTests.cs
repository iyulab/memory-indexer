using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Tests;
using MemoryIndexer.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Storage.InMemory;

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
        var result = await _store.StoreAsync(memory, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().NotBeEmpty();
        result.Content.Should().Be(memory.Content);
    }

    [Fact]
    public async Task GetByIdAsync_ExistingMemory_ShouldReturnMemory()
    {
        // Arrange
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory(), TestContext.Current.CancellationToken);

        // Act
        var result = await _store.GetByIdAsync(memory.Id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(memory.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingMemory_ShouldReturnNull()
    {
        // Act
        var result = await _store.GetByIdAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ExistingMemory_ShouldUpdate()
    {
        // Arrange
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory(), TestContext.Current.CancellationToken);
        memory.Content = "Updated content";

        // Act
        var result = await _store.UpdateAsync(memory, TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        var updated = await _store.GetByIdAsync(memory.Id, TestContext.Current.CancellationToken);
        updated!.Content.Should().Be("Updated content");
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_ShouldMarkAsDeleted()
    {
        // Arrange
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory(), TestContext.Current.CancellationToken);

        // Act
        var result = await _store.DeleteAsync(memory.Id, hardDelete: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        var deleted = await _store.GetByIdAsync(memory.Id, TestContext.Current.CancellationToken);
        deleted!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_HardDelete_ShouldRemoveMemory()
    {
        // Arrange
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory(), TestContext.Current.CancellationToken);

        // Act
        var result = await _store.DeleteAsync(memory.Id, hardDelete: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        var deleted = await _store.GetByIdAsync(memory.Id, TestContext.Current.CancellationToken);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnSimilarMemories()
    {
        // Arrange
        var embedding = TestHelpers.CreateTestEmbedding(768);
        var memory = TestHelpers.CreateTestMemory(embedding: embedding);
        await _store.StoreAsync(memory, TestContext.Current.CancellationToken);

        var options = new MemorySearchOptions
        {
            UserId = "test-user",
            Limit = 10
        };

        // Act
        var results = await _store.SearchAsync(embedding, options, TestContext.Current.CancellationToken);

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

        await _store.StoreAsync(user1Memory, TestContext.Current.CancellationToken);
        await _store.StoreAsync(user2Memory, TestContext.Current.CancellationToken);

        // Act
        var results = await _store.GetAllAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(1);
        results[0].UserId.Should().Be("user1");
    }

    [Fact]
    public async Task GetCountAsync_ShouldReturnCorrectCount()
    {
        // Arrange
        await _store.StoreAsync(TestHelpers.CreateTestMemory("user1"), TestContext.Current.CancellationToken);
        await _store.StoreAsync(TestHelpers.CreateTestMemory("user1"), TestContext.Current.CancellationToken);
        await _store.StoreAsync(TestHelpers.CreateTestMemory("user2"), TestContext.Current.CancellationToken);

        // Act
        var count = await _store.GetCountAsync("user1", TestContext.Current.CancellationToken);

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetAllAsync_WithRoleFilter_ShouldReturnMatchingRoles()
    {
        // Arrange - Create memories with different roles
        var userMemory = TestHelpers.CreateTestMemory("user1");
        userMemory.Role = "user";

        var assistantMemory = TestHelpers.CreateTestMemory("user1");
        assistantMemory.Role = "assistant";

        var systemMemory = TestHelpers.CreateTestMemory("user1");
        systemMemory.Role = "system";

        await _store.StoreAsync(userMemory, TestContext.Current.CancellationToken);
        await _store.StoreAsync(assistantMemory, TestContext.Current.CancellationToken);
        await _store.StoreAsync(systemMemory, TestContext.Current.CancellationToken);

        // Act - Filter by user role only
        var results = await _store.GetAllAsync("user1", new MemoryFilterOptions
        {
            Roles = ["user"]
        }, TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(1);
        results[0].Role.Should().Be("user");
    }

    [Fact]
    public async Task GetAllAsync_WithMultipleRolesFilter_ShouldReturnMatchingRoles()
    {
        // Arrange - Create memories with different roles
        var userMemory = TestHelpers.CreateTestMemory("user1");
        userMemory.Role = "user";

        var assistantMemory = TestHelpers.CreateTestMemory("user1");
        assistantMemory.Role = "assistant";

        var systemMemory = TestHelpers.CreateTestMemory("user1");
        systemMemory.Role = "system";

        await _store.StoreAsync(userMemory, TestContext.Current.CancellationToken);
        await _store.StoreAsync(assistantMemory, TestContext.Current.CancellationToken);
        await _store.StoreAsync(systemMemory, TestContext.Current.CancellationToken);

        // Act - Filter by user and assistant roles
        var results = await _store.GetAllAsync("user1", new MemoryFilterOptions
        {
            Roles = ["user", "assistant"]
        }, TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(2);
        results.Select(r => r.Role).Should().BeEquivalentTo(["user", "assistant"]);
    }

    [Fact]
    public async Task SearchAsync_WithRoleFilter_ShouldReturnMatchingRoles()
    {
        // Arrange - Create memories with different roles
        var embedding = TestHelpers.CreateTestEmbedding(768);

        var userMemory = TestHelpers.CreateTestMemory("user1", embedding: embedding);
        userMemory.Role = "user";

        var assistantMemory = TestHelpers.CreateTestMemory("user1", embedding: embedding);
        assistantMemory.Role = "assistant";

        await _store.StoreAsync(userMemory, TestContext.Current.CancellationToken);
        await _store.StoreAsync(assistantMemory, TestContext.Current.CancellationToken);

        // Act - Search with user role filter
        var results = await _store.SearchAsync(embedding, new MemorySearchOptions
        {
            UserId = "user1",
            Limit = 10,
            Roles = ["user"]
        }, TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(1);
        results[0].Memory.Role.Should().Be("user");
    }

    [Fact]
    public async Task GetAllAsync_WithCustomRoleFilter_ShouldSupportMultiParty()
    {
        // Arrange - Multi-party conversation with custom roles
        var moderatorMemory = TestHelpers.CreateTestMemory("user1");
        moderatorMemory.Role = "moderator";

        var participant1Memory = TestHelpers.CreateTestMemory("user1");
        participant1Memory.Role = "participant-1";

        var participant2Memory = TestHelpers.CreateTestMemory("user1");
        participant2Memory.Role = "participant-2";

        await _store.StoreAsync(moderatorMemory, TestContext.Current.CancellationToken);
        await _store.StoreAsync(participant1Memory, TestContext.Current.CancellationToken);
        await _store.StoreAsync(participant2Memory, TestContext.Current.CancellationToken);

        // Act - Filter by moderator only
        var results = await _store.GetAllAsync("user1", new MemoryFilterOptions
        {
            Roles = ["moderator"]
        }, TestContext.Current.CancellationToken);

        // Assert
        results.Should().HaveCount(1);
        results[0].Role.Should().Be("moderator");
    }
}
