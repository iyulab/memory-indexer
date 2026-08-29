using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Retention;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Retention;

/// <summary>
/// Tests for RetentionPolicyService.
/// Phase v0.11.0: Retention Policy Engine.
/// </summary>
public class RetentionPolicyServiceTests
{
    private readonly IRetentionPolicy _mockPolicy;
    private readonly IArchiveStore _mockArchiveStore;
    private readonly RetentionPolicyService _service;

    public RetentionPolicyServiceTests()
    {
        _mockPolicy = Substitute.For<IRetentionPolicy>();
        _mockArchiveStore = Substitute.For<IArchiveStore>();
        _service = new RetentionPolicyService(_mockPolicy, _mockArchiveStore);
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

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(entries);

        _mockPolicy.EvaluateAll(entries, null)
            .Returns(decisions);

        // Act
        var preview = await _service.PreviewCleanupAsync("user1", TestContext.Current.CancellationToken);

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
        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        _mockPolicy.EvaluateAll(Arg.Any<IEnumerable<SemanticStoreEntry>>(), null)
            .Returns(new List<RetentionDecision>());

        // Act
        var preview = await _service.PreviewCleanupAsync("user1", TestContext.Current.CancellationToken);

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
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewCleanupAsync(null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewCleanupAsync("", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.PreviewCleanupAsync("  ", TestContext.Current.CancellationToken));
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

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(entries);

        _mockPolicy.EvaluateAll(entries, null)
            .Returns(decisions);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ArchivedCount.Should().Be(1);
        await _mockArchiveStore.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<SemanticStoreEntry>(), Arg.Any<CancellationToken>());
        await _mockArchiveStore.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(entries);

        _mockPolicy.EvaluateAll(entries, null)
            .Returns(decisions);

        _mockArchiveStore.SetAsync("user1", Arg.Any<SemanticStoreEntry>(), Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ArchivedCount.Should().Be(1);
        await _mockArchiveStore.Received(1).SetAsync("user1", Arg.Is<SemanticStoreEntry>(e => e.Key == "archive" && !e.IsActive), Arg.Any<CancellationToken>());
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

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(entries);

        _mockPolicy.EvaluateAll(entries, null)
            .Returns(decisions);

        _mockArchiveStore.RemoveAsync("user1", "delete", Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(1);
        await _mockArchiveStore.Received(1).RemoveAsync("user1", "delete", Arg.Any<CancellationToken>());
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

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(entries);

        _mockPolicy.EvaluateAll(entries, null)
            .Returns(decisions);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.RetainedCount.Should().Be(1);
        result.ArchivedCount.Should().Be(0);
        result.DeletedCount.Should().Be(0);
        await _mockArchiveStore.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<SemanticStoreEntry>(), Arg.Any<CancellationToken>());
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

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(entries);

        _mockPolicy.EvaluateAll(entries, null)
            .Returns(decisions);

        _mockArchiveStore.SetAsync("user1", Arg.Any<SemanticStoreEntry>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _mockArchiveStore.RemoveAsync("user1", "delete", Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _service.ApplyAsync("user1", dryRun: false, cancellationToken: TestContext.Current.CancellationToken);

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
        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = await _service.ApplyAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

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
        _service.Policy.Should().BeSameAs(_mockPolicy);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullPolicy_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RetentionPolicyService(null!, _mockArchiveStore));
    }

    [Fact]
    public void Constructor_WithNullArchiveStore_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RetentionPolicyService(_mockPolicy, null!));
    }

    #endregion
}
