using FluentAssertions;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Tests;
using MemoryIndexer.Storage.InMemory;
using MemoryIndexer.Storage.Migration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Storage.Tests.Migration;

/// <summary>
/// Unit tests for MemoryStoreMigrator.
/// </summary>
public sealed class MemoryStoreMigratorTests
{
    private readonly MemoryStoreMigrator _migrator;

    public MemoryStoreMigratorTests()
    {
        _migrator = new MemoryStoreMigrator(NullLogger<MemoryStoreMigrator>.Instance);
    }

    private static MemoryUnit CreateTestMemory(string userId, string content)
        => TestHelpers.CreateTestMemoryWithId(userId, content);

    private static InMemoryMemoryStore CreateInMemoryStore()
    {
        return new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);
    }

    [Fact]
    public async Task MigrateAsync_ShouldMigrateMemoriesSuccessfully()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        var memories = new[]
        {
            CreateTestMemory(userId, "Memory 1"),
            CreateTestMemory(userId, "Memory 2"),
            CreateTestMemory(userId, "Memory 3")
        };

        foreach (var memory in memories)
        {
            await source.StoreAsync(memory);
        }

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [userId]);

        // Assert
        result.Status.Should().Be(MigrationStatus.Success);
        result.TotalMigrated.Should().Be(3);
        result.TotalFailed.Should().Be(0);
        result.TotalSkipped.Should().Be(0);
        result.UsersMigrated.Should().Contain(userId);

        var destCount = await destination.GetCountAsync(userId);
        destCount.Should().Be(3);
    }

    [Fact]
    public async Task MigrateAsync_ShouldSkipExistingMemories()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        var memory1 = CreateTestMemory(userId, "Memory 1");
        var memory2 = CreateTestMemory(userId, "Memory 2");

        await source.StoreAsync(memory1);
        await source.StoreAsync(memory2);

        // Pre-populate destination with memory1
        await destination.StoreAsync(memory1);

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [userId]);

        // Assert
        result.Status.Should().Be(MigrationStatus.Success);
        result.TotalMigrated.Should().Be(1);
        result.TotalSkipped.Should().Be(1);
        result.TotalFailed.Should().Be(0);
    }

    [Fact]
    public async Task MigrateAsync_ShouldReturnSkippedWhenNoUserIds()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: null);

        // Assert
        result.Status.Should().Be(MigrationStatus.Skipped);
        result.Message.Should().Contain("No user IDs");
    }

    [Fact]
    public async Task MigrateAsync_ShouldReturnSkippedWhenEmptyUserIds()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: []);

        // Assert
        result.Status.Should().Be(MigrationStatus.Skipped);
    }

    [Fact]
    public async Task MigrateAsync_ShouldHandleMultipleUsers()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();

        var user1 = "user-1";
        var user2 = "user-2";

        await source.StoreAsync(CreateTestMemory(user1, "User1 Memory 1"));
        await source.StoreAsync(CreateTestMemory(user1, "User1 Memory 2"));
        await source.StoreAsync(CreateTestMemory(user2, "User2 Memory 1"));

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [user1, user2]);

        // Assert
        result.Status.Should().Be(MigrationStatus.Success);
        result.TotalMigrated.Should().Be(3);
        result.UsersMigrated.Should().HaveCount(2);
        result.UsersMigrated.Should().Contain(user1);
        result.UsersMigrated.Should().Contain(user2);
    }

    [Fact]
    public async Task MigrateAsync_ShouldReportProgress()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        for (var i = 0; i < 5; i++)
        {
            await source.StoreAsync(CreateTestMemory(userId, $"Memory {i}"));
        }

        var progressCalls = new List<(long current, long total)>();

        // Act
        await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [userId],
            progress: (current, total) => progressCalls.Add((current, total)));

        // Assert
        progressCalls.Should().NotBeEmpty();
    }

    [Fact]
    public async Task MigrateAsync_ShouldRespectBatchSize()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        for (var i = 0; i < 10; i++)
        {
            await source.StoreAsync(CreateTestMemory(userId, $"Memory {i}"));
        }

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [userId],
            batchSize: 3);

        // Assert
        result.Status.Should().Be(MigrationStatus.Success);
        result.TotalMigrated.Should().Be(10);
    }

    [Fact]
    public async Task MigrateAsync_ShouldHandleCancellation()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        for (var i = 0; i < 10; i++)
        {
            await source.StoreAsync(CreateTestMemory(userId, $"Memory {i}"));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [userId],
            cancellationToken: cts.Token);

        // Assert
        result.Status.Should().Be(MigrationStatus.Cancelled);
        result.Message.Should().Contain("cancelled");
    }

    [Fact]
    public async Task MigrateAsync_ShouldRecordDuration()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        await source.StoreAsync(CreateTestMemory(userId, "Memory 1"));

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [userId]);

        // Assert
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnValidWhenCountsMatch()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        var memories = new[]
        {
            CreateTestMemory(userId, "Memory 1"),
            CreateTestMemory(userId, "Memory 2")
        };

        foreach (var memory in memories)
        {
            await source.StoreAsync(memory);
            await destination.StoreAsync(memory);
        }

        // Act
        var result = await _migrator.ValidateAsync(source, destination, [userId]);

        // Assert
        result.IsValid.Should().BeTrue();
        result.UserCounts[userId].Match.Should().BeTrue();
        result.UserCounts[userId].SourceCount.Should().Be(2);
        result.UserCounts[userId].DestinationCount.Should().Be(2);
    }

    [Fact]
    public async Task ValidateAsync_ShouldReturnInvalidWhenCountsMismatch()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        await source.StoreAsync(CreateTestMemory(userId, "Memory 1"));
        await source.StoreAsync(CreateTestMemory(userId, "Memory 2"));
        await destination.StoreAsync(CreateTestMemory(userId, "Memory 1"));

        // Act
        var result = await _migrator.ValidateAsync(source, destination, [userId]);

        // Assert
        result.IsValid.Should().BeFalse();
        result.UserCounts[userId].Match.Should().BeFalse();
        result.UserCounts[userId].SourceCount.Should().Be(2);
        result.UserCounts[userId].DestinationCount.Should().Be(1);
    }

    [Fact]
    public async Task ValidateAsync_ShouldHandleMultipleUsers()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();

        var user1 = "user-1";
        var user2 = "user-2";

        // User1: matching counts
        var memory1 = CreateTestMemory(user1, "Memory 1");
        await source.StoreAsync(memory1);
        await destination.StoreAsync(memory1);

        // User2: mismatching counts
        await source.StoreAsync(CreateTestMemory(user2, "Memory 1"));
        await source.StoreAsync(CreateTestMemory(user2, "Memory 2"));
        await destination.StoreAsync(CreateTestMemory(user2, "Only one"));

        // Act
        var result = await _migrator.ValidateAsync(source, destination, [user1, user2]);

        // Assert
        result.IsValid.Should().BeFalse();
        result.UserCounts[user1].Match.Should().BeTrue();
        result.UserCounts[user2].Match.Should().BeFalse();
    }

    [Fact]
    public async Task MigrateAsync_ShouldHandleEmptySource()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        // Source has no memories for this user

        // Act
        var result = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [userId]);

        // Assert
        result.Status.Should().Be(MigrationStatus.Success);
        result.TotalMigrated.Should().Be(0);
        result.TotalFailed.Should().Be(0);
        result.TotalSkipped.Should().Be(0);
    }

    [Fact]
    public async Task MigrationResult_ShouldHaveCorrectDefaults()
    {
        // Arrange & Act
        var result = new MigrationResult();

        // Assert
        result.Status.Should().Be(MigrationStatus.Pending);
        result.Message.Should().BeEmpty();
        result.TotalMigrated.Should().Be(0);
        result.TotalFailed.Should().Be(0);
        result.TotalSkipped.Should().Be(0);
        result.Duration.Should().Be(TimeSpan.Zero);
        result.UsersMigrated.Should().BeEmpty();
        result.FailedMemoryIds.Should().BeEmpty();
    }

    [Fact]
    public async Task MigrateAndValidate_EndToEndScenario()
    {
        // Arrange
        var source = CreateInMemoryStore();
        var destination = CreateInMemoryStore();
        var userId = "user-123";

        for (var i = 0; i < 5; i++)
        {
            await source.StoreAsync(CreateTestMemory(userId, $"Memory {i}"));
        }

        // Act - Migrate
        var migrateResult = await _migrator.MigrateAsync(
            source,
            destination,
            userIds: [userId]);

        // Act - Validate
        var validateResult = await _migrator.ValidateAsync(
            source,
            destination,
            [userId]);

        // Assert
        migrateResult.Status.Should().Be(MigrationStatus.Success);
        migrateResult.TotalMigrated.Should().Be(5);
        validateResult.IsValid.Should().BeTrue();
    }
}
