using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Profile;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Profile;

/// <summary>
/// Tests for ProfileSnapshotService.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public class ProfileSnapshotServiceTests
{
    private readonly Mock<IArchiveStore> _mockArchiveStore;
    private readonly Mock<IConfidenceDecayStrategy> _mockDecayStrategy;
    private readonly Mock<ILogger<ProfileSnapshotService>> _mockLogger;
    private readonly ProfileSnapshotService _service;

    public ProfileSnapshotServiceTests()
    {
        _mockArchiveStore = new Mock<IArchiveStore>();
        _mockDecayStrategy = new Mock<IConfidenceDecayStrategy>();
        _mockLogger = new Mock<ILogger<ProfileSnapshotService>>();

        _mockDecayStrategy.Setup(d => d.NeedsReconfirmation(It.IsAny<SemanticStoreEntry>(), It.IsAny<float>()))
            .Returns(false);

        _service = new ProfileSnapshotService(
            _mockArchiveStore.Object,
            _mockDecayStrategy.Object,
            _mockLogger.Object);
    }

    #region CreateSnapshotAsync Tests

    [Fact]
    public async Task CreateSnapshotAsync_ShouldCreateSnapshot()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Value 1", IsActive = true, Confidence = 0.9f },
            new() { Key = "fact2", Value = "Value 2", IsActive = true, Confidence = 0.8f }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        // Act
        var snapshot = await _service.CreateSnapshotAsync("user1", "Test snapshot");

        // Assert
        snapshot.Should().NotBeNull();
        snapshot.UserId.Should().Be("user1");
        snapshot.Label.Should().Be("Test snapshot");
        snapshot.Facts.Should().HaveCount(2);
        snapshot.Stats.TotalFacts.Should().Be(2);
    }

    [Fact]
    public async Task CreateSnapshotAsync_ShouldCalculateStats()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Value 1", IsActive = true, Confidence = 0.9f, ConfirmationCount = 3, Category = SemanticStoreCategory.Preference },
            new() { Key = "fact2", Value = "Value 2", IsActive = true, Confidence = 0.8f, ConfirmationCount = 2, Category = SemanticStoreCategory.Skill }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        // Act
        var snapshot = await _service.CreateSnapshotAsync("user1");

        // Assert
        snapshot.Stats.TotalFacts.Should().Be(2);
        snapshot.Stats.AverageConfidence.Should().BeApproximately(0.85f, 0.01f);
        snapshot.Stats.ByCategory.Should().ContainKey(SemanticStoreCategory.Preference);
        snapshot.Stats.ByCategory.Should().ContainKey(SemanticStoreCategory.Skill);
        snapshot.Stats.CompletenessScore.Should().BeGreaterThan(0);
    }

    #endregion

    #region GetSnapshotAsync Tests

    [Fact]
    public async Task GetSnapshotAsync_ExistingSnapshot_ShouldReturn()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        var created = await _service.CreateSnapshotAsync("user1");

        // Act
        var retrieved = await _service.GetSnapshotAsync("user1", created.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetSnapshotAsync_NonExistent_ShouldReturnNull()
    {
        // Act
        var snapshot = await _service.GetSnapshotAsync("user1", Guid.NewGuid());

        // Assert
        snapshot.Should().BeNull();
    }

    #endregion

    #region ListSnapshotsAsync Tests

    [Fact]
    public async Task ListSnapshotsAsync_ShouldListAllSnapshots()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        await _service.CreateSnapshotAsync("user1", "Snapshot 1");
        await _service.CreateSnapshotAsync("user1", "Snapshot 2");

        // Act
        var snapshots = await _service.ListSnapshotsAsync("user1");

        // Assert
        snapshots.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListSnapshotsAsync_ShouldRespectLimit()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        for (int i = 0; i < 5; i++)
        {
            await _service.CreateSnapshotAsync("user1", $"Snapshot {i}");
        }

        // Act
        var snapshots = await _service.ListSnapshotsAsync("user1", limit: 3);

        // Assert
        snapshots.Should().HaveCount(3);
    }

    [Fact]
    public async Task ListSnapshotsAsync_ShouldOrderByCreatedAtDesc()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        await _service.CreateSnapshotAsync("user1", "First");
        await Task.Delay(10);
        await _service.CreateSnapshotAsync("user1", "Second");

        // Act
        var snapshots = await _service.ListSnapshotsAsync("user1");

        // Assert
        snapshots.Should().HaveCount(2);
        snapshots[0].Label.Should().Be("Second"); // Newest first
        snapshots[1].Label.Should().Be("First");
    }

    #endregion

    #region CompareSnapshotsAsync Tests

    [Fact]
    public async Task CompareSnapshotsAsync_ShouldDetectAddedFacts()
    {
        // Arrange
        var olderFacts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Value 1", IsActive = true }
        };

        var newerFacts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Value 1", IsActive = true },
            new() { Key = "fact2", Value = "Value 2", IsActive = true }
        };

        _mockArchiveStore.SetupSequence(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(olderFacts)
            .ReturnsAsync(newerFacts);

        var olderSnapshot = await _service.CreateSnapshotAsync("user1");

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newerFacts);

        // Act
        var diff = await _service.CompareSnapshotsAsync("user1", olderSnapshot.Id);

        // Assert
        diff.HasChanges.Should().BeTrue();
        diff.AddedFacts.Should().HaveCount(1);
        diff.AddedFacts[0].Key.Should().Be("fact2");
        diff.Summary.AddedCount.Should().Be(1);
    }

    [Fact]
    public async Task CompareSnapshotsAsync_ShouldDetectRemovedFacts()
    {
        // Arrange
        var olderFacts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Value 1", IsActive = true },
            new() { Key = "fact2", Value = "Value 2", IsActive = true }
        };

        var newerFacts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Value 1", IsActive = true }
        };

        _mockArchiveStore.SetupSequence(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(olderFacts)
            .ReturnsAsync(newerFacts);

        var olderSnapshot = await _service.CreateSnapshotAsync("user1");

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newerFacts);

        // Act
        var diff = await _service.CompareSnapshotsAsync("user1", olderSnapshot.Id);

        // Assert
        diff.HasChanges.Should().BeTrue();
        diff.RemovedFacts.Should().HaveCount(1);
        diff.RemovedFacts[0].Key.Should().Be("fact2");
        diff.Summary.RemovedCount.Should().Be(1);
    }

    [Fact]
    public async Task CompareSnapshotsAsync_ShouldDetectModifiedFacts()
    {
        // Arrange
        var olderFacts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Old value", Confidence = 0.8f, IsActive = true }
        };

        var newerFacts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "New value", Confidence = 0.9f, IsActive = true }
        };

        _mockArchiveStore.SetupSequence(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(olderFacts)
            .ReturnsAsync(newerFacts);

        var olderSnapshot = await _service.CreateSnapshotAsync("user1");

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(newerFacts);

        // Act
        var diff = await _service.CompareSnapshotsAsync("user1", olderSnapshot.Id);

        // Assert
        diff.HasChanges.Should().BeTrue();
        diff.ModifiedFacts.Should().HaveCount(1);
        diff.ModifiedFacts[0].OldValue.Should().Be("Old value");
        diff.ModifiedFacts[0].NewValue.Should().Be("New value");
        diff.Summary.ModifiedCount.Should().Be(1);
    }

    [Fact]
    public async Task CompareSnapshotsAsync_NoChanges_ShouldReturnEmpty()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "fact1", Value = "Value 1", Confidence = 0.9f, IsActive = true }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        var olderSnapshot = await _service.CreateSnapshotAsync("user1");

        // Act
        var diff = await _service.CompareSnapshotsAsync("user1", olderSnapshot.Id);

        // Assert
        diff.HasChanges.Should().BeFalse();
        diff.AddedFacts.Should().BeEmpty();
        diff.RemovedFacts.Should().BeEmpty();
        diff.ModifiedFacts.Should().BeEmpty();
    }

    #endregion

    #region DeleteSnapshotAsync Tests

    [Fact]
    public async Task DeleteSnapshotAsync_ExistingSnapshot_ShouldDelete()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        var snapshot = await _service.CreateSnapshotAsync("user1");

        // Act
        var deleted = await _service.DeleteSnapshotAsync("user1", snapshot.Id);

        // Assert
        deleted.Should().BeTrue();

        var retrieved = await _service.GetSnapshotAsync("user1", snapshot.Id);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteSnapshotAsync_NonExistent_ShouldReturnFalse()
    {
        // Act
        var deleted = await _service.DeleteSnapshotAsync("user1", Guid.NewGuid());

        // Assert
        deleted.Should().BeFalse();
    }

    #endregion
}
