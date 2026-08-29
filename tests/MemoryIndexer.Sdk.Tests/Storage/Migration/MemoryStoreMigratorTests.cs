using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Tests;
using MemoryIndexer.InMemory;
using MemoryIndexer.Sdk.Storage.Migration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Storage.Migration;

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
            await source.StoreAsync(memory, TestContext.Current.CancellationToken);
        }

        // Act
        var result = await _migrator.MigrateAsync(source, destination, userIds: [userId], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Status.Should().Be(MigrationStatus.Success);
        result.TotalMigrated.Should().Be(3);
        result.TotalFailed.Should().Be(0);
        result.TotalSkipped.Should().Be(0);
        result.UsersMigrated.Should().Contain(userId);

        var destCount = await destination.GetCountAsync(userId, TestContext.Current.CancellationToken);
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

        await source.StoreAsync(memory1, TestContext.Current.CancellationToken);
        await source.StoreAsync(memory2, TestContext.Current.CancellationToken);

        // Pre-populate destination with memory1
        await destination.StoreAsync(memory1, TestContext.Current.CancellationToken);

        // Act
        var result = await _migrator.MigrateAsync(source, destination, userIds: [userId], cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _migrator.MigrateAsync(source, destination, userIds: null, cancellationToken: TestContext.Current.CancellationToken);

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
        var result = await _migrator.MigrateAsync(source, destination, userIds: [], cancellationToken: TestContext.Current.CancellationToken);

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

        await source.StoreAsync(CreateTestMemory(user1, "User1 Memory 1"), TestContext.Current.CancellationToken);
        await source.StoreAsync(CreateTestMemory(user1, "User1 Memory 2"), TestContext.Current.CancellationToken);
        await source.StoreAsync(CreateTestMemory(user2, "User2 Memory 1"), TestContext.Current.CancellationToken);

        // Act
        var result = await _migrator.MigrateAsync(source, destination, userIds: [user1, user2], cancellationToken: TestContext.Current.CancellationToken);

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
            await source.StoreAsync(CreateTestMemory(userId, $"Memory {i}"), TestContext.Current.CancellationToken);
        }

        var progressCalls = new List<(long current, long total)>();

        // Act
        await _migrator.MigrateAsync(source, destination, userIds: [userId], progress: (current, total) => progressCalls.Add((current, total)), cancellationToken: TestContext.Current.CancellationToken);

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
            await source.StoreAsync(CreateTestMemory(userId, $"Memory {i}"), TestContext.Current.CancellationToken);
        }

        // Act
        var result = await _migrator.MigrateAsync(source, destination, userIds: [userId], batchSize: 3, cancellationToken: TestContext.Current.CancellationToken);

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
            await source.StoreAsync(CreateTestMemory(userId, $"Memory {i}"), TestContext.Current.CancellationToken);
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

        await source.StoreAsync(CreateTestMemory(userId, "Memory 1"), TestContext.Current.CancellationToken);

        // Act
        var result = await _migrator.MigrateAsync(source, destination, userIds: [userId], cancellationToken: TestContext.Current.CancellationToken);

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
            await source.StoreAsync(memory, TestContext.Current.CancellationToken);
            await destination.StoreAsync(memory, TestContext.Current.CancellationToken);
        }

        // Act
        var result = await _migrator.ValidateAsync(source, destination, [userId], TestContext.Current.CancellationToken);

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

        await source.StoreAsync(CreateTestMemory(userId, "Memory 1"), TestContext.Current.CancellationToken);
        await source.StoreAsync(CreateTestMemory(userId, "Memory 2"), TestContext.Current.CancellationToken);
        await destination.StoreAsync(CreateTestMemory(userId, "Memory 1"), TestContext.Current.CancellationToken);

        // Act
        var result = await _migrator.ValidateAsync(source, destination, [userId], TestContext.Current.CancellationToken);

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
        await source.StoreAsync(memory1, TestContext.Current.CancellationToken);
        await destination.StoreAsync(memory1, TestContext.Current.CancellationToken);

        // User2: mismatching counts
        await source.StoreAsync(CreateTestMemory(user2, "Memory 1"), TestContext.Current.CancellationToken);
        await source.StoreAsync(CreateTestMemory(user2, "Memory 2"), TestContext.Current.CancellationToken);
        await destination.StoreAsync(CreateTestMemory(user2, "Only one"), TestContext.Current.CancellationToken);

        // Act
        var result = await _migrator.ValidateAsync(source, destination, [user1, user2], TestContext.Current.CancellationToken);

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
        var result = await _migrator.MigrateAsync(source, destination, userIds: [userId], cancellationToken: TestContext.Current.CancellationToken);

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
            await source.StoreAsync(CreateTestMemory(userId, $"Memory {i}"), TestContext.Current.CancellationToken);
        }

        // Act - Migrate
        var migrateResult = await _migrator.MigrateAsync(source, destination, userIds: [userId], cancellationToken: TestContext.Current.CancellationToken);

        // Act - Validate
        var validateResult = await _migrator.ValidateAsync(source, destination, [userId], TestContext.Current.CancellationToken);

        // Assert
        migrateResult.Status.Should().Be(MigrationStatus.Success);
        migrateResult.TotalMigrated.Should().Be(5);
        validateResult.IsValid.Should().BeTrue();
    }
}
