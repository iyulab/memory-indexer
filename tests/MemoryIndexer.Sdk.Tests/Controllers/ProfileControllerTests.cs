using FluentAssertions;
using McpServer.Controllers;
using MemoryIndexer.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Controllers;

/// <summary>
/// Unit tests for ProfileController REST API.
/// </summary>
public class ProfileControllerTests
{
    private readonly Mock<IProfileSnapshotService> _mockSnapshotService;
    private readonly Mock<IConfidenceDecayStrategy> _mockDecayStrategy;
    private readonly Mock<IArchiveStore> _mockArchiveStore;
    private readonly Mock<ILogger<ProfileController>> _mockLogger;
    private readonly ProfileController _controller;

    public ProfileControllerTests()
    {
        _mockSnapshotService = new Mock<IProfileSnapshotService>();
        _mockDecayStrategy = new Mock<IConfidenceDecayStrategy>();
        _mockArchiveStore = new Mock<IArchiveStore>();
        _mockLogger = new Mock<ILogger<ProfileController>>();

        _controller = new ProfileController(
            _mockSnapshotService.Object,
            _mockDecayStrategy.Object,
            _mockArchiveStore.Object,
            _mockLogger.Object);
    }

    #region GetProfileStats Tests

    [Fact]
    public async Task GetProfileStats_WithFacts_ReturnsStats()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "name", Value = "John", IsActive = true, Confidence = 0.9f, Category = SemanticStoreCategory.Fact, ConfirmationCount = 3 },
            new() { Key = "hobby", Value = "Coding", IsActive = true, Confidence = 0.8f, Category = SemanticStoreCategory.Preference, ConfirmationCount = 0 },
            new() { Key = "old_name", Value = "Jane", IsActive = false, Confidence = 0.7f, Category = SemanticStoreCategory.Fact }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);
        _mockDecayStrategy.Setup(d => d.NeedsReconfirmation(It.IsAny<SemanticStoreEntry>(), It.IsAny<float>()))
            .Returns(false);

        // Act
        var result = await _controller.GetProfileStats(ct: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ProfileStatsResponse>().Subject;
        response.TotalFacts.Should().Be(3);
        response.ActiveFacts.Should().Be(2);
    }

    [Fact]
    public async Task GetProfileStats_WithNoFacts_ReturnsEmptyStats()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAllAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        // Act
        var result = await _controller.GetProfileStats(ct: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ProfileStatsResponse>().Subject;
        response.TotalFacts.Should().Be(0);
        response.AverageConfidence.Should().Be(0);
    }

    [Fact]
    public async Task GetProfileStats_WithStaleFacts_CountsCorrectly()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "name", Value = "John", IsActive = true, Confidence = 0.9f, UpdatedAt = DateTime.UtcNow.AddDays(-100) },
            new() { Key = "hobby", Value = "Coding", IsActive = true, Confidence = 0.8f, UpdatedAt = DateTime.UtcNow }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);
        _mockDecayStrategy.SetupSequence(d => d.NeedsReconfirmation(It.IsAny<SemanticStoreEntry>(), It.IsAny<float>()))
            .Returns(true)
            .Returns(false);

        // Act
        var result = await _controller.GetProfileStats(ct: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ProfileStatsResponse>().Subject;
        response.StaleFacts.Should().Be(1);
    }

    [Fact]
    public async Task GetProfileStats_WithCustomUserId_UsesProvidedUserId()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAllAsync("custom-user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        // Act
        await _controller.GetProfileStats(userId: "custom-user", ct: CancellationToken.None);

        // Assert
        _mockArchiveStore.Verify(a => a.GetAllAsync("custom-user", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CreateSnapshot Tests

    [Fact]
    public async Task CreateSnapshot_WithValidRequest_ReturnsCreatedSnapshot()
    {
        // Arrange
        var snapshotId = Guid.NewGuid();
        var snapshot = new ProfileSnapshot
        {
            Id = snapshotId,
            UserId = "default",
            Label = "Test Snapshot",
            CreatedAt = DateTime.UtcNow,
            Facts = new List<SemanticStoreEntry> { new() { Key = "name", Value = "John" } },
            Stats = new ProfileStats
            {
                TotalFacts = 1,
                ConfirmedFacts = 1,
                AverageConfidence = 0.9f,
                CompletenessScore = 0.5f,
                StaleFactCount = 0
            }
        };

        _mockSnapshotService.Setup(s => s.CreateSnapshotAsync("default", "Test Snapshot", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var request = new CreateSnapshotRequest { Label = "Test Snapshot" };

        // Act
        var result = await _controller.CreateSnapshot(request, CancellationToken.None);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        var response = createdResult.Value.Should().BeOfType<SnapshotResponse>().Subject;
        response.Id.Should().Be(snapshotId.ToString());
        response.FactCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateSnapshot_WithCustomUserId_UsesProvidedUserId()
    {
        // Arrange
        var snapshot = new ProfileSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = "custom-user",
            CreatedAt = DateTime.UtcNow,
            Facts = new List<SemanticStoreEntry>(),
            Stats = new ProfileStats()
        };

        _mockSnapshotService.Setup(s => s.CreateSnapshotAsync("custom-user", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var request = new CreateSnapshotRequest { UserId = "custom-user" };

        // Act
        await _controller.CreateSnapshot(request, CancellationToken.None);

        // Assert
        _mockSnapshotService.Verify(s => s.CreateSnapshotAsync("custom-user", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetSnapshot Tests

    [Fact]
    public async Task GetSnapshot_WithExistingSnapshot_ReturnsSnapshot()
    {
        // Arrange
        var snapshotId = Guid.NewGuid();
        var snapshot = new ProfileSnapshot
        {
            Id = snapshotId,
            UserId = "default",
            Label = "Test",
            CreatedAt = DateTime.UtcNow,
            Facts = new List<SemanticStoreEntry> { new() { Key = "test", Value = "value" } },
            Stats = new ProfileStats { TotalFacts = 1 }
        };

        _mockSnapshotService.Setup(s => s.GetSnapshotAsync("default", snapshotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _controller.GetSnapshot(snapshotId, ct: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<SnapshotResponse>().Subject;
        response.Id.Should().Be(snapshotId.ToString());
    }

    [Fact]
    public async Task GetSnapshot_WithNonExistentSnapshot_ReturnsNotFound()
    {
        // Arrange
        var snapshotId = Guid.NewGuid();
        _mockSnapshotService.Setup(s => s.GetSnapshotAsync("default", snapshotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProfileSnapshot?)null);

        // Act
        var result = await _controller.GetSnapshot(snapshotId, ct: CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion

    #region ListSnapshots Tests

    [Fact]
    public async Task ListSnapshots_WithSnapshots_ReturnsList()
    {
        // Arrange
        var snapshots = new List<ProfileSnapshotSummary>
        {
            new() { Id = Guid.NewGuid(), UserId = "default", Label = "Snapshot 1", CreatedAt = DateTime.UtcNow, FactCount = 5 },
            new() { Id = Guid.NewGuid(), UserId = "default", Label = "Snapshot 2", CreatedAt = DateTime.UtcNow.AddDays(-1), FactCount = 3 }
        };

        _mockSnapshotService.Setup(s => s.ListSnapshotsAsync("default", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        // Act
        var result = await _controller.ListSnapshots(ct: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<SnapshotListResponse>().Subject;
        response.TotalCount.Should().Be(2);
        response.Snapshots.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListSnapshots_WithLimit_RespectsLimit()
    {
        // Arrange
        _mockSnapshotService.Setup(s => s.ListSnapshotsAsync("default", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProfileSnapshotSummary>());

        // Act
        await _controller.ListSnapshots(limit: 5, ct: CancellationToken.None);

        // Assert
        _mockSnapshotService.Verify(s => s.ListSnapshotsAsync("default", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CompareSnapshots Tests

    [Fact]
    public async Task CompareSnapshots_WithValidSnapshots_ReturnsDiff()
    {
        // Arrange
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var diff = new ProfileDiff
        {
            UserId = "default",
            OlderSnapshotId = olderId,
            NewerSnapshotId = newerId,
            OlderTimestamp = DateTime.UtcNow.AddDays(-1),
            NewerTimestamp = DateTime.UtcNow,
            Summary = new DiffSummary { AddedCount = 2, RemovedCount = 1, ModifiedCount = 1 },
            AddedFacts = new List<SemanticStoreEntry> { new() { Key = "new_fact", Value = "New Fact" } },
            RemovedFacts = new List<SemanticStoreEntry> { new() { Key = "removed_fact", Value = "Removed Fact" } },
            ModifiedFacts = new List<FactChange>
            {
                new() { Key = "name", OldValue = "Jane", NewValue = "John", ChangeType = FactChangeType.ValueChange }
            }
        };

        _mockSnapshotService.Setup(s => s.CompareSnapshotsAsync("default", olderId, newerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(diff);

        var request = new CompareSnapshotsRequest { OlderSnapshotId = olderId, NewerSnapshotId = newerId };

        // Act
        var result = await _controller.CompareSnapshots(request, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<SnapshotDiffResponse>().Subject;
        response.HasChanges.Should().BeTrue();
        response.AddedCount.Should().Be(2);
        response.TotalChanges.Should().Be(4);
    }

    [Fact]
    public async Task CompareSnapshots_WithInvalidSnapshot_ReturnsBadRequest()
    {
        // Arrange
        var olderId = Guid.NewGuid();
        _mockSnapshotService.Setup(s => s.CompareSnapshotsAsync("default", olderId, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("Snapshot not found"));

        var request = new CompareSnapshotsRequest { OlderSnapshotId = olderId };

        // Act
        var result = await _controller.CompareSnapshots(request, CancellationToken.None);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region GetStaleFacts Tests

    [Fact]
    public async Task GetStaleFacts_WithStaleFacts_ReturnsList()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "name", Value = "John", IsActive = true, Confidence = 0.9f, UpdatedAt = DateTime.UtcNow.AddDays(-100), Category = SemanticStoreCategory.Fact },
            new() { Key = "hobby", Value = "Coding", IsActive = true, Confidence = 0.8f, UpdatedAt = DateTime.UtcNow, Category = SemanticStoreCategory.Preference }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);
        _mockDecayStrategy.Setup(d => d.NeedsReconfirmation(It.IsAny<SemanticStoreEntry>(), 0.5f))
            .Returns((SemanticStoreEntry f, float _) => f.Key == "name");
        _mockDecayStrategy.Setup(d => d.CalculateDecayedConfidence(It.IsAny<SemanticStoreEntry>(), It.IsAny<DateTime>()))
            .Returns(0.4f);

        // Act
        var result = await _controller.GetStaleFacts(ct: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<StaleFactsResponse>().Subject;
        response.StaleCount.Should().Be(1);
        response.TotalActiveFacts.Should().Be(2);
    }

    [Fact]
    public async Task GetStaleFacts_WithCustomThreshold_UsesThreshold()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAllAsync("default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        // Act
        var result = await _controller.GetStaleFacts(threshold: 0.7f, ct: CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<StaleFactsResponse>().Subject;
        response.Threshold.Should().Be(0.7f);
    }

    #endregion

    #region DeleteSnapshot Tests

    [Fact]
    public async Task DeleteSnapshot_WithExistingSnapshot_ReturnsNoContent()
    {
        // Arrange
        var snapshotId = Guid.NewGuid();
        _mockSnapshotService.Setup(s => s.DeleteSnapshotAsync("default", snapshotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.DeleteSnapshot(snapshotId, ct: CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteSnapshot_WithNonExistentSnapshot_ReturnsNotFound()
    {
        // Arrange
        var snapshotId = Guid.NewGuid();
        _mockSnapshotService.Setup(s => s.DeleteSnapshotAsync("default", snapshotId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.DeleteSnapshot(snapshotId, ct: CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
