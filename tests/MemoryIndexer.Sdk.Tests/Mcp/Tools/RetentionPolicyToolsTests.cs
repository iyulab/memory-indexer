using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Mcp.Tools;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp.Tools;

/// <summary>
/// Tests for RetentionPolicyTools MCP tools.
/// Phase v0.11.0: Retention Policy Engine.
/// </summary>
public class RetentionPolicyToolsTests
{
    private readonly IRetentionPolicyService _mockRetentionService;
    private readonly IRetentionPolicy _mockPolicy;
    private readonly RetentionPolicyTools _tools;

    public RetentionPolicyToolsTests()
    {
        _mockRetentionService = Substitute.For<IRetentionPolicyService>();
        _mockPolicy = Substitute.For<IRetentionPolicy>();
        _mockRetentionService.Policy.Returns(_mockPolicy);
        _tools = new RetentionPolicyTools(_mockRetentionService, Options.Create(new MemoryIndexerOptions()));
    }

    #region PreviewCleanup Tests

    [Fact]
    public async Task PreviewCleanup_ShouldReturnPreviewResult()
    {
        // Arrange
        var preview = new RetentionPreview
        {
            UserId = "user1",
            TotalEntries = 10,
            RetainCount = 7,
            ArchiveCount = 2,
            DeleteCount = 1,
            ByCategory = new Dictionary<SemanticStoreCategory, CategoryPreview>
            {
                [SemanticStoreCategory.Fact] = new() { Category = SemanticStoreCategory.Fact, TotalCount = 5, RetainCount = 5, CleanupCount = 0 },
                [SemanticStoreCategory.Preference] = new() { Category = SemanticStoreCategory.Preference, TotalCount = 5, RetainCount = 2, CleanupCount = 3 }
            },
            CleanupDecisions =
            [
                new RetentionDecision
                {
                    Entry = new SemanticStoreEntry { Key = "old-pref", Value = "Old preference", Category = SemanticStoreCategory.Preference, Confidence = 0.3f },
                    ShouldRetain = false,
                    Action = RetentionAction.Archive,
                    Reason = RetentionReason.LowConfidence,
                    DecayedConfidence = 0.15f
                }
            ]
        };

        _mockRetentionService.PreviewCleanupAsync("user1", Arg.Any<CancellationToken>())
            .Returns(preview);

        // Act
        var result = await _tools.PreviewCleanup("user1", TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().Be("user1");
        result.TotalEntries.Should().Be(10);
        result.RetainCount.Should().Be(7);
        result.ArchiveCount.Should().Be(2);
        result.DeleteCount.Should().Be(1);
        result.ByCategory.Should().HaveCount(2);
        result.CleanupItems.Should().HaveCount(1);
        result.CleanupItems![0].Key.Should().Be("old-pref");
    }

    [Fact]
    public async Task PreviewCleanup_WithDefaultUserId_ShouldUseOptionsDefaultUserId()
    {
        // Arrange
        var preview = new RetentionPreview
        {
            UserId = "default",
            TotalEntries = 0,
            ByCategory = new Dictionary<SemanticStoreCategory, CategoryPreview>(),
            CleanupDecisions = []
        };

        _mockRetentionService.PreviewCleanupAsync("default", Arg.Any<CancellationToken>())
            .Returns(preview);

        // Act
        var result = await _tools.PreviewCleanup(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        await _mockRetentionService.Received(1).PreviewCleanupAsync("default", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewCleanup_OnError_ShouldReturnFailure()
    {
        // Arrange
        _mockRetentionService.PreviewCleanupAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Database error"));

        // Act
        var result = await _tools.PreviewCleanup("user1", TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Database error");
    }

    #endregion

    #region ApplyRetentionPolicy Tests

    [Fact]
    public async Task ApplyRetentionPolicy_DryRun_ShouldNotModify()
    {
        // Arrange
        var applyResult = new RetentionResult
        {
            Success = true,
            TotalProcessed = 10,
            RetainedCount = 7,
            ArchivedCount = 2,
            DeletedCount = 1,
            ProcessingTimeMs = 50
        };

        _mockRetentionService.ApplyAsync("user1", true, Arg.Any<CancellationToken>())
            .Returns(applyResult);

        // Act
        var result = await _tools.ApplyRetentionPolicy("user1", dryRun: true, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.DryRun.Should().BeTrue();
        result.TotalProcessed.Should().Be(10);
        result.Message.Should().Contain("Dry run");
    }

    [Fact]
    public async Task ApplyRetentionPolicy_WithApply_ShouldModify()
    {
        // Arrange
        var applyResult = new RetentionResult
        {
            Success = true,
            TotalProcessed = 10,
            RetainedCount = 7,
            ArchivedCount = 2,
            DeletedCount = 1,
            ProcessingTimeMs = 100
        };

        _mockRetentionService.ApplyAsync("user1", false, Arg.Any<CancellationToken>())
            .Returns(applyResult);

        // Act
        var result = await _tools.ApplyRetentionPolicy("user1", dryRun: false, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.DryRun.Should().BeFalse();
        result.ArchivedCount.Should().Be(2);
        result.DeletedCount.Should().Be(1);
        result.Message.Should().Contain("Policy applied");
    }

    [Fact]
    public async Task ApplyRetentionPolicy_OnFailure_ShouldReturnError()
    {
        // Arrange
        var applyResult = new RetentionResult
        {
            Success = false,
            ErrorMessage = "Storage connection failed"
        };

        _mockRetentionService.ApplyAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(applyResult);

        // Act
        var result = await _tools.ApplyRetentionPolicy("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Storage connection failed");
    }

    #endregion

    #region GetRetentionRules Tests

    [Fact]
    public async Task GetRetentionRules_ShouldReturnAllRules()
    {
        // Arrange
        var rules = new Dictionary<SemanticStoreCategory, RetentionRule>
        {
            [SemanticStoreCategory.Fact] = new() { Category = SemanticStoreCategory.Fact, MaxAgeDays = null, MinConfidence = 0.05f },
            [SemanticStoreCategory.Preference] = new() { Category = SemanticStoreCategory.Preference, MaxAgeDays = 365, MinConfidence = 0.2f }
        };

        _mockPolicy.GetAllRules().Returns(rules);

        // Act
        var result = await _tools.GetRetentionRules();

        // Assert
        result.Success.Should().BeTrue();
        result.RuleCount.Should().Be(2);
        result.Rules.Should().HaveCount(2);
    }

    #endregion

    #region GetRetentionRule Tests

    [Fact]
    public async Task GetRetentionRule_ValidCategory_ShouldReturnRule()
    {
        // Arrange
        var rule = new RetentionRule
        {
            Category = SemanticStoreCategory.Fact,
            MaxAgeDays = null,
            MinConfidence = 0.05f,
            ProtectedConfirmationCount = 2,
            StaleAfterDays = null,
            Action = RetentionAction.Archive,
            Description = "Identity facts never expire"
        };

        _mockPolicy.GetRule(SemanticStoreCategory.Fact).Returns(rule);

        // Act
        var result = await _tools.GetRetentionRule("Fact");

        // Assert
        result.Success.Should().BeTrue();
        result.Category.Should().Be("Fact");
        result.MaxAgeDays.Should().BeNull();
        result.MaxAgeDaysDescription.Should().Be("Never expires");
    }

    [Fact]
    public async Task GetRetentionRule_InvalidCategory_ShouldReturnError()
    {
        // Act
        var result = await _tools.GetRetentionRule("InvalidCategory");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid category");
    }

    [Fact]
    public async Task GetRetentionRule_CaseInsensitive_ShouldWork()
    {
        // Arrange
        var rule = new RetentionRule { Category = SemanticStoreCategory.Goal, MaxAgeDays = 180 };
        _mockPolicy.GetRule(SemanticStoreCategory.Goal).Returns(rule);

        // Act
        var result = await _tools.GetRetentionRule("goal");

        // Assert
        result.Success.Should().BeTrue();
        result.Category.Should().Be("Goal");
    }

    #endregion

    #region EvaluateRetention Tests

    [Fact]
    public async Task EvaluateRetention_EntryInCleanup_ShouldReturnDecision()
    {
        // Arrange
        var preview = new RetentionPreview
        {
            UserId = "user1",
            ByCategory = new Dictionary<SemanticStoreCategory, CategoryPreview>(),
            CleanupDecisions =
            [
                new RetentionDecision
                {
                    Entry = new SemanticStoreEntry
                    {
                        Key = "test-key",
                        Value = "Test preference",
                        Category = SemanticStoreCategory.Preference,
                        Confidence = 0.3f,
                        CreatedAt = DateTime.UtcNow.AddDays(-100),
                        LastAccessedAt = DateTime.UtcNow.AddDays(-50)
                    },
                    ShouldRetain = false,
                    Action = RetentionAction.Archive,
                    Reason = RetentionReason.LowConfidence,
                    DecayedConfidence = 0.15f
                }
            ]
        };

        _mockRetentionService.PreviewCleanupAsync("user1", Arg.Any<CancellationToken>())
            .Returns(preview);

        // Act
        var result = await _tools.EvaluateRetention("test-key", "user1", TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Key.Should().Be("test-key");
        result.ShouldRetain.Should().BeFalse();
        result.Reason.Should().Be("LowConfidence");
        result.Action.Should().Be("Archive");
    }

    [Fact]
    public async Task EvaluateRetention_EntryNotInCleanup_ShouldAssumeRetained()
    {
        // Arrange
        var preview = new RetentionPreview
        {
            UserId = "user1",
            ByCategory = new Dictionary<SemanticStoreCategory, CategoryPreview>(),
            CleanupDecisions = []
        };

        _mockRetentionService.PreviewCleanupAsync("user1", Arg.Any<CancellationToken>())
            .Returns(preview);

        // Act
        var result = await _tools.EvaluateRetention("missing-key", "user1", TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.Key.Should().Be("missing-key");
        result.ShouldRetain.Should().BeTrue();
    }

    #endregion
}
