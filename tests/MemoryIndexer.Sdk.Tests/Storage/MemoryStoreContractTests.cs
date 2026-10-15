using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Tests;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Storage;

/// <summary>
/// What every <see cref="IMemoryStore"/> must do, run against each store (a derived class per store): storing, ownership
/// (a memory never answers another user), soft and hard deletes, filters and search. A store that differs from the others
/// fails here rather than in the app that swapped stores.
/// </summary>
public abstract class MemoryStoreContractTests
{
    private IMemoryStore? _created;

    // Created on first use, not in the constructor: a derived class's fields are not set before the base constructor runs.
    private IMemoryStore _store => _created ??= CreateStore();

    /// <summary>A new, empty store for one test. Embeddings in these tests have 768 dimensions.</summary>
    protected abstract IMemoryStore CreateStore();

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
        var result = await _store.GetByIdAsync(memory.UserId, memory.Id, TestContext.Current.CancellationToken);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(memory.Id);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistingMemory_ShouldReturnNull()
    {
        // Act
        var result = await _store.GetByIdAsync("test-user", Guid.NewGuid(), TestContext.Current.CancellationToken);

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

        var updated = await _store.GetByIdAsync(memory.UserId, memory.Id, TestContext.Current.CancellationToken);
        updated!.Content.Should().Be("Updated content");
    }

    [Fact]
    public async Task DeleteAsync_SoftDelete_ShouldMarkAsDeleted()
    {
        // Arrange
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory(), TestContext.Current.CancellationToken);

        // Act
        var result = await _store.DeleteAsync(memory.UserId, memory.Id, hardDelete: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        var deleted = await _store.GetByIdAsync(memory.UserId, memory.Id, TestContext.Current.CancellationToken);
        deleted!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_HardDelete_ShouldRemoveMemory()
    {
        // Arrange
        var memory = await _store.StoreAsync(TestHelpers.CreateTestMemory(), TestContext.Current.CancellationToken);

        // Act
        var result = await _store.DeleteAsync(memory.UserId, memory.Id, hardDelete: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Should().BeTrue();

        var deleted = await _store.GetByIdAsync(memory.UserId, memory.Id, TestContext.Current.CancellationToken);
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

    #region User Isolation

    private async Task<(MemoryUnit Alice, MemoryUnit Bob)> StoreAliceAndBobAsync()
    {
        var alice = await _store.StoreAsync(
            TestHelpers.CreateTestMemory("alice", "Alice's memory"), TestContext.Current.CancellationToken);
        var bob = await _store.StoreAsync(
            TestHelpers.CreateTestMemory("bob", "Bob's memory"), TestContext.Current.CancellationToken);
        return (alice, bob);
    }

    [Fact]
    public async Task GetByIdAsync_AnotherUsersId_ReturnsNull()
    {
        var (alice, _) = await StoreAliceAndBobAsync();

        var asBob = await _store.GetByIdAsync("bob", alice.Id, TestContext.Current.CancellationToken);
        var asAlice = await _store.GetByIdAsync("alice", alice.Id, TestContext.Current.CancellationToken);

        asBob.Should().BeNull();
        asAlice.Should().NotBeNull();
        asAlice!.Id.Should().Be(alice.Id);
        asAlice.Content.Should().Be("Alice's memory");
    }

    [Fact]
    public async Task GetByIdsAsync_MixedUsers_ReturnsOnlyTheCallersMemories()
    {
        var (alice, bob) = await StoreAliceAndBobAsync();

        var results = await _store.GetByIdsAsync("bob", [alice.Id, bob.Id], TestContext.Current.CancellationToken);

        results.Should().ContainSingle().Which.Id.Should().Be(bob.Id);
    }

    [Fact]
    public async Task DeleteAsync_AnotherUsersId_DeletesNothing()
    {
        var (alice, _) = await StoreAliceAndBobAsync();

        var deleted = await _store.DeleteAsync("bob", alice.Id, hardDelete: true, TestContext.Current.CancellationToken);

        deleted.Should().BeFalse();
        var stillThere = await _store.GetByIdAsync("alice", alice.Id, TestContext.Current.CancellationToken);
        stillThere.Should().NotBeNull();
        stillThere!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_WithAnotherUserAsOwner_ChangesNothing()
    {
        var (alice, _) = await StoreAliceAndBobAsync();

        // A fresh object: the in-memory store holds references, so mutating the stored one would bypass the store
        var tampered = new MemoryUnit
        {
            Id = alice.Id,
            UserId = "bob",
            Content = "Tampered by bob"
        };

        var updated = await _store.UpdateAsync(tampered, TestContext.Current.CancellationToken);

        updated.Should().BeFalse();
        var stored = await _store.GetByIdAsync("alice", alice.Id, TestContext.Current.CancellationToken);
        stored.Should().NotBeNull();
        stored!.UserId.Should().Be("alice");
        stored.Content.Should().Be("Alice's memory");
        (await _store.GetByIdAsync("bob", alice.Id, TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_WithoutUser_Throws()
    {
        await StoreAliceAndBobAsync();
        var options = new MemorySearchOptions { UserId = "", Limit = 10 };

        var act = () => _store.SearchAsync(TestHelpers.CreateTestEmbedding(3, 1), options, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetUserIdsAsync_ListsEachUserOnce()
    {
        await StoreAliceAndBobAsync();
        await _store.StoreAsync(TestHelpers.CreateTestMemory("alice", "Second"), TestContext.Current.CancellationToken);

        var users = await _store.GetUserIdsAsync(TestContext.Current.CancellationToken);

        users.Should().Equal("alice", "bob");
    }

    [Fact]
    public async Task GetAllAsync_WithoutAUser_Throws()
    {
        await StoreAliceAndBobAsync();

        var act = () => _store.GetAllAsync("", cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Filters and bulk operations

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MemoryUnit Memory(string content, string? session = null, string? ns = null, int seed = 1)
    {
        var memory = TestHelpers.CreateTestMemory("alice", content, embedding: TestHelpers.CreateTestEmbedding(768, seed));
        memory.SessionId = session;
        memory.Namespace = ns;
        return memory;
    }

    [Fact]
    public async Task GetAllAsync_FiltersByMetadataTierAndScope_AndTheMetadataRoundTrips()
    {
        var match = Memory("match");
        match.Metadata = new() { ["project"] = "apollo", ["lang"] = "ko" };
        match.Tier = Tier.Long;
        match.Scope = Scope.User;
        var otherMetadata = Memory("other metadata", seed: 2);
        otherMetadata.Metadata = new() { ["project"] = "gemini" };
        otherMetadata.Tier = Tier.Long;
        otherMetadata.Scope = Scope.User;
        var otherTier = Memory("other tier", seed: 3);
        otherTier.Metadata = new() { ["project"] = "apollo" };
        otherTier.Tier = Tier.Short;
        otherTier.Scope = Scope.User;
        await _store.StoreBatchAsync([match, otherMetadata, otherTier], Ct);

        var found = await _store.GetAllAsync("alice", new MemoryFilterOptions
        {
            MetadataFilter = new() { ["project"] = "apollo" }, Tiers = [Tier.Long], Scopes = [Scope.User]
        }, Ct);

        found.Should().ContainSingle().Which.Content.Should().Be("match");
        found[0].Metadata.Should().BeEquivalentTo(match.Metadata);
    }

    [Fact]
    public async Task DeleteBySessionAsync_SoftDeletesThatSessionOnly()
    {
        await _store.StoreBatchAsync([Memory("s1 a", session: "s1"), Memory("s1 b", session: "s1", seed: 2), Memory("s2", session: "s2", seed: 3)], Ct);

        var deleted = await _store.DeleteBySessionAsync("alice", "s1", cancellationToken: Ct);

        deleted.Should().Be(2);
        (await _store.GetAllAsync("alice", cancellationToken: Ct)).Should().ContainSingle().Which.Content.Should().Be("s2");
        (await _store.GetAllAsync("alice", new MemoryFilterOptions { IncludeDeleted = true }, Ct)).Should().HaveCount(3);
        (await _store.GetCountAsync("alice", Ct)).Should().Be(1);
    }

    [Fact]
    public async Task DeleteByNamespaceAsync_HardDelete_RemovesThatNamespaceOnly()
    {
        await _store.StoreBatchAsync([Memory("n1", ns: "n1"), Memory("n2", ns: "n2", seed: 2)], Ct);

        var deleted = await _store.DeleteByNamespaceAsync("alice", "n1", hardDelete: true, Ct);

        deleted.Should().Be(1);
        (await _store.GetAllAsync("alice", new MemoryFilterOptions { IncludeDeleted = true }, Ct))
            .Should().ContainSingle().Which.Content.Should().Be("n2");
    }

    [Fact]
    public async Task GetTypeCountsAsync_CountsLiveMemoriesByType()
    {
        var episodic = Memory("e");
        var semantic = Memory("s", seed: 2);
        semantic.Type = MemoryType.Semantic;
        var deleted = Memory("d", seed: 3);
        deleted.Type = MemoryType.Semantic;
        await _store.StoreBatchAsync([episodic, semantic, deleted], Ct);
        await _store.DeleteAsync("alice", deleted.Id, cancellationToken: Ct);

        var counts = await _store.GetTypeCountsAsync("alice", Ct);

        counts.Should().BeEquivalentTo(new Dictionary<MemoryType, int> { [MemoryType.Episodic] = 1, [MemoryType.Semantic] = 1 });
    }

    [Fact]
    public async Task SearchAsync_LeavesSoftDeletedMemoriesOut_UnlessAsked()
    {
        var kept = Memory("kept");
        var removed = Memory("removed", seed: 2);
        await _store.StoreBatchAsync([kept, removed], Ct);
        await _store.DeleteAsync("alice", removed.Id, cancellationToken: Ct);
        var query = TestHelpers.CreateTestEmbedding(768, 2);

        var live = await _store.SearchAsync(query, new MemorySearchOptions { UserId = "alice", Limit = 10, MinScore = -1 }, Ct);
        var all = await _store.SearchAsync(query, new MemorySearchOptions { UserId = "alice", Limit = 10, MinScore = -1, IncludeDeleted = true }, Ct);

        live.Select(r => r.Memory.Content).Should().Equal("kept");
        all.Select(r => r.Memory.Content).Should().BeEquivalentTo(["kept", "removed"]);
        all[0].Memory.Content.Should().Be("removed", "the query is its embedding");
        all[0].Score.Should().BeApproximately(1f, 1e-4f);
    }

    #endregion
}
