using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Profile;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Profile;

/// <summary>
/// Tests for ArchiveStoreService temporal query methods.
/// Phase v0.9.2: Fact Conflict Resolution - Bi-Temporal Model.
/// </summary>
public class ArchiveStoreServiceTemporalTests
{
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ILogger<ArchiveStoreService> _mockLogger;
    private readonly ArchiveStoreService _service;

    private readonly ReadOnlyMemory<float> _testEmbedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };

    public ArchiveStoreServiceTemporalTests()
    {
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockLogger = Substitute.For<ILogger<ArchiveStoreService>>();

        var options = Options.Create(new SemanticStoreOptions
        {
            EnableSemanticSearch = true,
            MaxEntriesPerUser = 500
        });

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_testEmbedding);

        _service = new ArchiveStoreService(
            _mockEmbeddingService,
            options,
            _mockLogger);
    }

    #region GetHistoryAsync Tests

    [Fact]
    public async Task GetHistoryAsync_WithNoHistory_ShouldReturnSingleEntry()
    {
        // Arrange
        var userId = "user1";
        var entry = new SemanticStoreEntry
        {
            Key = "identity:user:name",
            Value = "User's name is John",
            Version = 1
        };

        await _service.SetAsync(userId, entry);

        // Act
        var history = await _service.GetHistoryAsync(userId, "identity:user:name");

        // Assert
        history.Should().HaveCount(1);
        history[0].Value.Should().Be("User's name is John");
        history[0].Version.Should().Be(1);
    }

    [Fact]
    public async Task GetHistoryAsync_WithVersionChain_ShouldReturnAllVersions()
    {
        // Arrange
        var userId = "user1";
        var entry1 = new SemanticStoreEntry
        {
            Key = "identity:user:name",
            Value = "User's name is John",
            Version = 1
        };

        await _service.SetAsync(userId, entry1);
        await _service.ArchiveAndUpdateAsync(userId, "identity:user:name", "User's name is Mike");

        // Act
        var history = await _service.GetHistoryAsync(userId, "identity:user:name");

        // Assert
        history.Should().HaveCountGreaterThanOrEqualTo(2);
        history.Should().Contain(e => e.Value == "User's name is John");
        history.Should().Contain(e => e.Value == "User's name is Mike");
    }

    [Fact]
    public async Task GetHistoryAsync_NonExistentKey_ShouldReturnEmpty()
    {
        // Act
        var history = await _service.GetHistoryAsync("user1", "nonexistent:key");

        // Assert
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_NonExistentUser_ShouldReturnEmpty()
    {
        // Act
        var history = await _service.GetHistoryAsync("nonexistent", "identity:user:name");

        // Assert
        history.Should().BeEmpty();
    }

    #endregion

    #region GetValidAtAsync Tests

    [Fact]
    public async Task GetValidAtAsync_WithCurrentFacts_ShouldReturnAll()
    {
        // Arrange
        var userId = "user1";
        var now = DateTime.UtcNow;

        var entry = new SemanticStoreEntry
        {
            Key = "identity:user:name",
            Value = "User's name is John",
            ValidFrom = now.AddDays(-7),
            ValidTo = null,
            IsActive = true
        };

        await _service.SetAsync(userId, entry);

        // Act
        var facts = await _service.GetValidAtAsync(userId, now);

        // Assert
        facts.Should().HaveCount(1);
        facts[0].Value.Should().Be("User's name is John");
    }

    [Fact]
    public async Task GetValidAtAsync_WithExpiredFact_ShouldExclude()
    {
        // Arrange
        var userId = "user1";
        var now = DateTime.UtcNow;

        var expiredEntry = new SemanticStoreEntry
        {
            Key = "location:user:residence",
            Value = "User lives in Seoul",
            ValidFrom = now.AddDays(-30),
            ValidTo = now.AddDays(-7), // Expired
            IsActive = false
        };

        await _service.SetAsync(userId, expiredEntry);

        // Act
        var facts = await _service.GetValidAtAsync(userId, now);

        // Assert - Should not include expired fact
        facts.Should().NotContain(f => f.Value == "User lives in Seoul");
    }

    [Fact]
    public async Task GetValidAtAsync_AtHistoricalDate_ShouldReturnValidFacts()
    {
        // Arrange
        var userId = "user1";
        var now = DateTime.UtcNow;

        var pastEntry = new SemanticStoreEntry
        {
            Key = "location:user:residence:v1",
            Value = "User lived in Seoul",
            ValidFrom = now.AddDays(-60),
            ValidTo = now.AddDays(-30),
            IsActive = false
        };

        await _service.SetAsync(userId, pastEntry);

        // Act - Query at a date when the fact was valid
        var queryDate = now.AddDays(-45);
        var facts = await _service.GetValidAtAsync(userId, queryDate);

        // Assert
        facts.Should().HaveCount(1);
        facts[0].Value.Should().Be("User lived in Seoul");
    }

    [Fact]
    public async Task GetValidAtAsync_NonExistentUser_ShouldReturnEmpty()
    {
        // Act
        var facts = await _service.GetValidAtAsync("nonexistent", DateTime.UtcNow);

        // Assert
        facts.Should().BeEmpty();
    }

    #endregion

    #region ArchiveAndUpdateAsync Tests

    [Fact]
    public async Task ArchiveAndUpdateAsync_ExistingEntry_ShouldCreateNewVersion()
    {
        // Arrange
        var userId = "user1";
        var originalEntry = new SemanticStoreEntry
        {
            Key = "identity:user:name",
            Value = "User's name is John",
            Confidence = 0.9f,
            Version = 1
        };

        await _service.SetAsync(userId, originalEntry);

        // Act
        var updatedEntry = await _service.ArchiveAndUpdateAsync(
            userId,
            "identity:user:name",
            "User's name is Mike");

        // Assert
        updatedEntry.Should().NotBeNull();
        updatedEntry!.Value.Should().Be("User's name is Mike");
        updatedEntry.Version.Should().Be(2);
        updatedEntry.SupersedesKey.Should().Contain("v1");
        updatedEntry.IsActive.Should().BeTrue();
        updatedEntry.ValidFrom.Should().NotBeNull();
    }

    [Fact]
    public async Task ArchiveAndUpdateAsync_ShouldPreserveOldVersion()
    {
        // Arrange
        var userId = "user1";
        var originalEntry = new SemanticStoreEntry
        {
            Key = "identity:user:name",
            Value = "User's name is John",
            Version = 1
        };

        await _service.SetAsync(userId, originalEntry);

        // Act
        await _service.ArchiveAndUpdateAsync(userId, "identity:user:name", "User's name is Mike");

        // Assert - Old version should be archived
        var archivedEntry = await _service.GetAsync(userId, "identity:user:name:v1");
        archivedEntry.Should().NotBeNull();
        archivedEntry!.Value.Should().Be("User's name is John");
        archivedEntry.IsActive.Should().BeFalse();
        archivedEntry.ValidTo.Should().NotBeNull();
    }

    [Fact]
    public async Task ArchiveAndUpdateAsync_NonExistentEntry_ShouldReturnNull()
    {
        // Act
        var result = await _service.ArchiveAndUpdateAsync("user1", "nonexistent", "new value");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ArchiveAndUpdateAsync_NonExistentUser_ShouldReturnNull()
    {
        // Act
        var result = await _service.ArchiveAndUpdateAsync("nonexistent", "key", "value");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task ArchiveAndUpdateAsync_ShouldResetConfirmationCount()
    {
        // Arrange
        var userId = "user1";
        var originalEntry = new SemanticStoreEntry
        {
            Key = "identity:user:name",
            Value = "User's name is John",
            ConfirmationCount = 5
        };

        await _service.SetAsync(userId, originalEntry);

        // Act
        var updatedEntry = await _service.ArchiveAndUpdateAsync(
            userId,
            "identity:user:name",
            "User's name is Mike");

        // Assert - New version should have reset confirmation count
        updatedEntry!.ConfirmationCount.Should().Be(1);
    }

    #endregion

    #region SemanticStoreEntry Bitemporal Tests

    [Fact]
    public void SemanticStoreEntry_WasValidAt_CurrentlyValid_ShouldReturnTrue()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "test",
            ValidFrom = DateTime.UtcNow.AddDays(-7),
            ValidTo = null
        };

        // Act & Assert
        entry.WasValidAt(DateTime.UtcNow).Should().BeTrue();
        entry.WasValidAt(DateTime.UtcNow.AddDays(-5)).Should().BeTrue();
    }

    [Fact]
    public void SemanticStoreEntry_WasValidAt_Expired_ShouldReturnFalse()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "test",
            ValidFrom = DateTime.UtcNow.AddDays(-30),
            ValidTo = DateTime.UtcNow.AddDays(-7)
        };

        // Act & Assert
        entry.WasValidAt(DateTime.UtcNow).Should().BeFalse();
        entry.WasValidAt(DateTime.UtcNow.AddDays(-20)).Should().BeTrue();
    }

    [Fact]
    public void SemanticStoreEntry_WasValidAt_NotYetValid_ShouldReturnFalse()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "test",
            ValidFrom = DateTime.UtcNow.AddDays(7), // Future
            ValidTo = null
        };

        // Act & Assert
        entry.WasValidAt(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void SemanticStoreEntry_IsCurrentlyValid_Active_ShouldReturnTrue()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "test",
            IsActive = true,
            ValidTo = null
        };

        // Act & Assert
        entry.IsCurrentlyValid.Should().BeTrue();
    }

    [Fact]
    public void SemanticStoreEntry_IsCurrentlyValid_Inactive_ShouldReturnFalse()
    {
        // Arrange
        var entry = new SemanticStoreEntry
        {
            Key = "test",
            Value = "test",
            IsActive = false,
            ValidTo = null
        };

        // Act & Assert
        entry.IsCurrentlyValid.Should().BeFalse();
    }

    [Fact]
    public void SemanticStoreEntry_CreateSupersedingVersion_ShouldCreateNewVersion()
    {
        // Arrange
        var original = new SemanticStoreEntry
        {
            Key = "test:key",
            Value = "original value",
            Category = SemanticStoreCategory.Fact,
            Confidence = 0.9f,
            Version = 1,
            IsActive = true
        };

        // Act
        var superseding = original.CreateSupersedingVersion("new value");

        // Assert - Original should be marked as invalid
        original.IsActive.Should().BeFalse();
        original.ValidTo.Should().NotBeNull();

        // Assert - New version should be valid
        superseding.Value.Should().Be("new value");
        superseding.Version.Should().Be(2);
        superseding.SupersedesKey.Should().Be("test:key");
        superseding.IsActive.Should().BeTrue();
        superseding.ValidFrom.Should().NotBeNull();
        superseding.ValidTo.Should().BeNull();
        superseding.ConfirmationCount.Should().Be(1);
    }

    #endregion
}
