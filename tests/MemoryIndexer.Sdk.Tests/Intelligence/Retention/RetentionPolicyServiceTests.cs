using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Retention;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Retention;

/// <summary>
/// Tests for RetentionPolicyService.
/// Phase v0.11.0: Retention Policy Engine.
/// </summary>
public class RetentionPolicyServiceTests
{
    private readonly Mock<IRetentionPolicy> _mockPolicy;
    private readonly Mock<IArchiveStore> _mockArchiveStore;
    private readonly RetentionPolicyService _service;

    public RetentionPolicyServiceTests()
    {
        _mockPolicy = new Mock<IRetentionPolicy>();
        _mockArchiveStore = new Mock<IArchiveStore>();
        _service = new RetentionPolicyService(_mockPolicy.Object, _mockArchiveStore.Object);
    }

    #region PreviewCleanupAsync Tests

    [Fact]
    public async Task PreviewCleanupAsync_ShouldReturnPreview()
    {
        // Arrange
        var entries = new List<SemanticStoreEntry>
        {
            new() { Key = "keep1", Value = "Keep", Category = SemanticStoreCategory.Fact, IsActive = true },
            new() { Key = "keep2", Value = "Keep2", Category = SemanticStoreCategory.Preference, IsActive = true },
            new() { Key = "archive", Value = "Archive", Category = SemanticStoreCategory.Preference, IsActive = true }
        };

        var decisions = new List<RetentionDecision>
        {
            new() { Entry = entries[0], ShouldRetain = true, Action = RetentionAction.Keep, Reason = RetentionReason.WithinPolicy },
            new() { Entry = entries[1], ShouldRetain = true, Action = RetentionAction.Keep, Reason = RetentionReason.WithinPolicy },
            new() { Entry = entries[2], ShouldRetain = false, Action = RetentionAction.Archive, Reason = RetentionReason.LowConfidence }
        };

        _mockArchiveStore
            .Setup(s => s.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        _mockPolicy
            .Setup(p => p.EvaluateAll(entries, null))
            .Returns(decisions);

        // Act
        var preview = await _service.PreviewCleanupAsync("user1");

        // Assert
        preview.UserId.Should().Be("user1");
        preview.TotalEntries.Should().Be(3);
        preview.RetainCount.Should().Be(2);
        preview.ArchiveCount.Should().Be(1);
        preview.DeleteCount.Should().Be(0);
        preview.CleanupDecisions.Should().HaveCount(1);
        preview.ByCategory.Should().ContainKey(SemanticStoreCategory.Fact);
        preview.ByCategory.Should().ContainKey(SemanticStoreCategory.Preference);
    }

    [Fact]
    public async Task PreviewCleanupAsync_WithEmptyStore_ShouldReturnEmptyPreview()
    {
        // Arrange
        _mockArchiveStore
            .Setup(s => s.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SemanticStoreEntry>());

        _mockPolicy
            .Setup(p => p.EvaluateAll(It.IsAny<IEnumerable<SemanticStoreEntry>>(), null))
            .Returns(new List<RetentionDecision>());

        // Act
        var preview = await _service.PreviewCleanupAsync("user1");

        // Assert
        preview.TotalEntries.Should().Be(0);
        preview.RetainCount.Should().Be(0);
        preview.ArchiveCount.Should().Be(0);
        preview.DeleteCount.Should().Be(0);
    }

    [Fact]
    public async Task PreviewCleanupAsync_WithNullUserId_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewCleanupAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewCleanupAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewCleanupAsync("  "));
    }

    #endregion

    #region ApplyAsync Tests

    [Fact]
    public async Task ApplyAsync_DryRun_ShouldNotModifyStore()
    {
        // Arrange
        var entries = new List<SemanticStoreEntry>
        {
            new() { Key = "archive", Value = "Archive", Category = SemanticStoreCategory.Preference, IsActive = true }
        };

        var decisions = new List<RetentionDecision>
        {
            new() { Entry = entries[0], ShouldRetain = false, Action = RetentionAction.Archive, Reason = RetentionReason.LowConfidence }
        };

        _mockArchiveStore
            .Setup(s => s.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        _mockPolicy
            .Setup(p => p.EvaluateAll(entries, null))
            .Returns(decisions);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: true);

        // Assert
        result.Success.Should().BeTrue();
        result.ArchivedCount.Should().Be(1);
        _mockArchiveStore.Verify(s => s.SetAsync(It.IsAny<string>(), It.IsAny<SemanticStoreEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockArchiveStore.Verify(s => s.RemoveAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_ShouldArchiveEntries()
    {
        // Arrange
        var entries = new List<SemanticStoreEntry>
        {
            new() { Key = "archive", Value = "Archive", Category = SemanticStoreCategory.Preference, IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-100) }
        };

        var decisions = new List<RetentionDecision>
        {
            new() { Entry = entries[0], ShouldRetain = false, Action = RetentionAction.Archive, Reason = RetentionReason.LowConfidence }
        };

        _mockArchiveStore
            .Setup(s => s.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        _mockPolicy
            .Setup(p => p.EvaluateAll(entries, null))
            .Returns(decisions);

        _mockArchiveStore
            .Setup(s => s.SetAsync("user1", It.IsAny<SemanticStoreEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: false);

        // Assert
        result.Success.Should().BeTrue();
        result.ArchivedCount.Should().Be(1);
        _mockArchiveStore.Verify(
            s => s.SetAsync("user1", It.Is<SemanticStoreEntry>(e => e.Key == "archive" && !e.IsActive), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_ShouldDeleteEntries()
    {
        // Arrange
        var entries = new List<SemanticStoreEntry>
        {
            new() { Key = "delete", Value = "Delete", Category = SemanticStoreCategory.Preference, IsActive = true }
        };

        var decisions = new List<RetentionDecision>
        {
            new() { Entry = entries[0], ShouldRetain = false, Action = RetentionAction.Delete, Reason = RetentionReason.AgeExceeded }
        };

        _mockArchiveStore
            .Setup(s => s.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        _mockPolicy
            .Setup(p => p.EvaluateAll(entries, null))
            .Returns(decisions);

        _mockArchiveStore
            .Setup(s => s.RemoveAsync("user1", "delete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: false);

        // Assert
        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(1);
        _mockArchiveStore.Verify(s => s.RemoveAsync("user1", "delete", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyAsync_ShouldRetainEntries()
    {
        // Arrange
        var entries = new List<SemanticStoreEntry>
        {
            new() { Key = "keep", Value = "Keep", Category = SemanticStoreCategory.Fact, IsActive = true }
        };

        var decisions = new List<RetentionDecision>
        {
            new() { Entry = entries[0], ShouldRetain = true, Action = RetentionAction.Keep, Reason = RetentionReason.WithinPolicy }
        };

        _mockArchiveStore
            .Setup(s => s.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        _mockPolicy
            .Setup(p => p.EvaluateAll(entries, null))
            .Returns(decisions);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: false);

        // Assert
        result.Success.Should().BeTrue();
        result.RetainedCount.Should().Be(1);
        result.ArchivedCount.Should().Be(0);
        result.DeletedCount.Should().Be(0);
        _mockArchiveStore.Verify(s => s.SetAsync(It.IsAny<string>(), It.IsAny<SemanticStoreEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ApplyAsync_MixedActions_ShouldProcessAll()
    {
        // Arrange
        var entries = new List<SemanticStoreEntry>
        {
            new() { Key = "keep", Value = "Keep", Category = SemanticStoreCategory.Fact, IsActive = true },
            new() { Key = "archive", Value = "Archive", Category = SemanticStoreCategory.Preference, IsActive = true, CreatedAt = DateTime.UtcNow },
            new() { Key = "delete", Value = "Delete", Category = SemanticStoreCategory.Goal, IsActive = true }
        };

        var decisions = new List<RetentionDecision>
        {
            new() { Entry = entries[0], ShouldRetain = true, Action = RetentionAction.Keep, Reason = RetentionReason.WithinPolicy },
            new() { Entry = entries[1], ShouldRetain = false, Action = RetentionAction.Archive, Reason = RetentionReason.LowConfidence },
            new() { Entry = entries[2], ShouldRetain = false, Action = RetentionAction.Delete, Reason = RetentionReason.AgeExceeded }
        };

        _mockArchiveStore
            .Setup(s => s.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        _mockPolicy
            .Setup(p => p.EvaluateAll(entries, null))
            .Returns(decisions);

        _mockArchiveStore
            .Setup(s => s.SetAsync("user1", It.IsAny<SemanticStoreEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockArchiveStore
            .Setup(s => s.RemoveAsync("user1", "delete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: false);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalProcessed.Should().Be(3);
        result.RetainedCount.Should().Be(1);
        result.ArchivedCount.Should().Be(1);
        result.DeletedCount.Should().Be(1);
        result.ProcessingTimeMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ApplyAsync_OnException_ShouldReturnError()
    {
        // Arrange
        _mockArchiveStore
            .Setup(s => s.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _service.ApplyAsync("user1");

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Database error");
        result.Errors.Should().Contain("Database error");
    }

    [Fact]
    public async Task ApplyAsync_WithCancellation_ShouldThrow()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.ApplyAsync("user1", cancellationToken: cts.Token));
    }

    #endregion

    #region Policy Property Tests

    [Fact]
    public void Policy_ShouldReturnInjectedPolicy()
    {
        // Assert
        _service.Policy.Should().BeSameAs(_mockPolicy.Object);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullPolicy_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RetentionPolicyService(null!, _mockArchiveStore.Object));
    }

    [Fact]
    public void Constructor_WithNullArchiveStore_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RetentionPolicyService(_mockPolicy.Object, null!));
    }

    #endregion
}
