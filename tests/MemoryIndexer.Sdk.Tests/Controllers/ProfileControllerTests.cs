using FluentAssertions;
using McpServer.Controllers;
using MemoryIndexer.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Controllers;

/// <summary>
/// Unit tests for ProfileController REST API.
/// </summary>
public class ProfileControllerTests
{
    private readonly IProfileSnapshotService _mockSnapshotService;
    private readonly IConfidenceDecayStrategy _mockDecayStrategy;
    private readonly IArchiveStore _mockArchiveStore;
    private readonly ILogger<ProfileController> _mockLogger;
    private readonly ProfileController _controller;

    public ProfileControllerTests()
    {
        _mockSnapshotService = Substitute.For<IProfileSnapshotService>();
        _mockDecayStrategy = Substitute.For<IConfidenceDecayStrategy>();
        _mockArchiveStore = Substitute.For<IArchiveStore>();
        _mockLogger = Substitute.For<ILogger<ProfileController>>();

        _controller = new ProfileController(
            _mockSnapshotService,
            _mockDecayStrategy,
            _mockArchiveStore,
            _mockLogger);
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

        _mockArchiveStore.GetAllAsync("default", Arg.Any<CancellationToken>())
            .Returns(facts);
        _mockDecayStrategy.NeedsReconfirmation(Arg.Any<SemanticStoreEntry>(), Arg.Any<float>())
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
        _mockArchiveStore.GetAllAsync("default", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

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

        _mockArchiveStore.GetAllAsync("default", Arg.Any<CancellationToken>())
            .Returns(facts);
        _mockDecayStrategy.NeedsReconfirmation(Arg.Any<SemanticStoreEntry>(), Arg.Any<float>())
            .Returns(true, false);

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
        _mockArchiveStore.GetAllAsync("custom-user", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        // Act
        await _controller.GetProfileStats(userId: "custom-user", ct: CancellationToken.None);

        // Assert
        await _mockArchiveStore.Received(1).GetAllAsync("custom-user", Arg.Any<CancellationToken>());
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

        _mockSnapshotService.CreateSnapshotAsync("default", "Test Snapshot", Arg.Any<CancellationToken>())
            .Returns(snapshot);

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

        _mockSnapshotService.CreateSnapshotAsync("custom-user", null, Arg.Any<CancellationToken>())
            .Returns(snapshot);

        var request = new CreateSnapshotRequest { UserId = "custom-user" };

        // Act
        await _controller.CreateSnapshot(request, CancellationToken.None);

        // Assert
        await _mockSnapshotService.Received(1).CreateSnapshotAsync("custom-user", null, Arg.Any<CancellationToken>());
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

        _mockSnapshotService.GetSnapshotAsync("default", snapshotId, Arg.Any<CancellationToken>())
            .Returns(snapshot);

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
        _mockSnapshotService.GetSnapshotAsync("default", snapshotId, Arg.Any<CancellationToken>())
            .Returns((ProfileSnapshot?)null);

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

        _mockSnapshotService.ListSnapshotsAsync("default", 10, Arg.Any<CancellationToken>())
            .Returns(snapshots);

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
        _mockSnapshotService.ListSnapshotsAsync("default", 5, Arg.Any<CancellationToken>())
            .Returns(new List<ProfileSnapshotSummary>());

        // Act
        await _controller.ListSnapshots(limit: 5, ct: CancellationToken.None);

        // Assert
        await _mockSnapshotService.Received(1).ListSnapshotsAsync("default", 5, Arg.Any<CancellationToken>());
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

        _mockSnapshotService.CompareSnapshotsAsync("default", olderId, newerId, Arg.Any<CancellationToken>())
            .Returns(diff);

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
        _mockSnapshotService.CompareSnapshotsAsync("default", olderId, null, Arg.Any<CancellationToken>())
            .Throws(new ArgumentException("Snapshot not found"));

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

        _mockArchiveStore.GetAllAsync("default", Arg.Any<CancellationToken>())
            .Returns(facts);
        _mockDecayStrategy.NeedsReconfirmation(Arg.Any<SemanticStoreEntry>(), 0.5f)
            .Returns(callInfo => callInfo.ArgAt<SemanticStoreEntry>(0).Key == "name");
        _mockDecayStrategy.CalculateDecayedConfidence(Arg.Any<SemanticStoreEntry>(), Arg.Any<DateTime>())
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
        _mockArchiveStore.GetAllAsync("default", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

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
        _mockSnapshotService.DeleteSnapshotAsync("default", snapshotId, Arg.Any<CancellationToken>())
            .Returns(true);

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
        _mockSnapshotService.DeleteSnapshotAsync("default", snapshotId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _controller.DeleteSnapshot(snapshotId, ct: CancellationToken.None);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion
}
