using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Promotion;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Promotion;

/// <summary>
/// Tests for FastTrackPromoterService.
/// Phase v0.9.1: Intelligent Fact Extraction.
/// </summary>
public class FastTrackPromoterServiceTests
{
    private readonly IFactExtractor _mockExtractor;
    private readonly IArchiveStore _mockArchiveStore;
    private readonly IMemoryStore _mockMemoryStore;
    private readonly ILogger<FastTrackPromoterService> _mockLogger;
    private readonly FastTrackPromoterService _service;

    public FastTrackPromoterServiceTests()
    {
        _mockExtractor = Substitute.For<IFactExtractor>();
        _mockArchiveStore = Substitute.For<IArchiveStore>();
        _mockMemoryStore = Substitute.For<IMemoryStore>();
        _mockLogger = Substitute.For<ILogger<FastTrackPromoterService>>();

        _service = new FastTrackPromoterService(
            _mockExtractor,
            _mockArchiveStore,
            _mockLogger,
            _mockMemoryStore);
    }

    #region ProcessAsync Tests

    [Fact]
    public async Task ProcessAsync_WithHighConfidenceFact_ShouldFastTrackToArchive()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "My name is John",
            UserId = "user1"
        };

        var extractionResult = new FactExtractionResult
        {
            Facts =
            [
                new UserFact
                {
                    Content = "User's name is John",
                    Category = FactCategory.Identity,
                    Confidence = 0.95f,
                    PromotionPath = PromotionPath.FastTrack,
                    Subject = "user",
                    Predicate = "name",
                    Object = "John"
                }
            ],
            OverallConfidence = 0.95f,
            ContextType = StatementContextType.Direct
        };

        var validationResult = new FactValidationResult
        {
            IsValid = true,
            ConflictType = FactConflictType.None,
            RecommendedAction = FactConflictAction.Add
        };

        _mockExtractor.ExtractAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        _mockExtractor.ValidateAsync(Arg.Any<UserFact>(), Arg.Any<IReadOnlyList<MemoryUnit>>(), Arg.Any<CancellationToken>())
            .Returns(validationResult);

        _mockMemoryStore.GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit>());

        // Act
        var result = await _service.ProcessAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.FastTrackedFacts.Should().HaveCount(1);
        result.ContextType.Should().Be(StatementContextType.Direct);

        await _mockArchiveStore.Received(1).SetAsync(
            "user1",
            Arg.Is<SemanticStoreEntry>(e => e.Value == "User's name is John"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithModerateConfidenceFact_ShouldQueueForStandardPath()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "I live in Seoul",
            UserId = "user1"
        };

        var extractionResult = new FactExtractionResult
        {
            Facts =
            [
                new UserFact
                {
                    Content = "User lives in Seoul",
                    Category = FactCategory.Location,
                    Confidence = 0.7f,
                    PromotionPath = PromotionPath.Standard,
                    Subject = "user",
                    Predicate = "lives in",
                    Object = "Seoul"
                }
            ],
            OverallConfidence = 0.7f,
            ContextType = StatementContextType.Direct
        };

        var validationResult = new FactValidationResult
        {
            IsValid = true,
            ConflictType = FactConflictType.None,
            RecommendedAction = FactConflictAction.Add
        };

        _mockExtractor.ExtractAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        _mockExtractor.ValidateAsync(Arg.Any<UserFact>(), Arg.Any<IReadOnlyList<MemoryUnit>>(), Arg.Any<CancellationToken>())
            .Returns(validationResult);

        _mockMemoryStore.GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit>());

        // Act
        var result = await _service.ProcessAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.FastTrackedFacts.Should().BeEmpty();
        result.StandardPathFacts.Should().HaveCount(1);

        await _mockArchiveStore.DidNotReceive().SetAsync(
            Arg.Any<string>(),
            Arg.Any<SemanticStoreEntry>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithQuotedContent_ShouldSkipExtraction()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "In the novel, he says 'My name is Lincoln'",
            UserId = "user1"
        };

        var extractionResult = new FactExtractionResult
        {
            Facts =
            [
                new UserFact
                {
                    Content = "Character's name is Lincoln",
                    Category = FactCategory.Identity,
                    Confidence = 0.3f,
                    PromotionPath = PromotionPath.SessionOnly
                }
            ],
            OverallConfidence = 0.3f,
            ContextType = StatementContextType.Quoted
        };

        var validationResult = new FactValidationResult
        {
            IsValid = true,
            ConflictType = FactConflictType.None
        };

        _mockExtractor.ExtractAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        _mockExtractor.ValidateAsync(Arg.Any<UserFact>(), Arg.Any<IReadOnlyList<MemoryUnit>>(), Arg.Any<CancellationToken>())
            .Returns(validationResult);

        _mockMemoryStore.GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit>());

        // Act
        var result = await _service.ProcessAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ContextType.Should().Be(StatementContextType.Quoted);
        result.FastTrackedFacts.Should().BeEmpty();
        result.SkippedFacts.Should().HaveCount(1);
        result.SkippedFacts[0].Reason.Should().Be(SkipReasonType.Other);
    }

    [Fact]
    public async Task ProcessAsync_WithDuplicateFact_ShouldSkip()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "My name is John",
            UserId = "user1"
        };

        var extractionResult = new FactExtractionResult
        {
            Facts =
            [
                new UserFact
                {
                    Content = "User's name is John",
                    Category = FactCategory.Identity,
                    Confidence = 0.95f,
                    PromotionPath = PromotionPath.FastTrack
                }
            ],
            ContextType = StatementContextType.Direct
        };

        var validationResult = new FactValidationResult
        {
            IsValid = false,
            ConflictType = FactConflictType.Duplicate,
            RecommendedAction = FactConflictAction.Skip,
            Reasoning = "Exact duplicate found"
        };

        _mockExtractor.ExtractAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        _mockExtractor.ValidateAsync(Arg.Any<UserFact>(), Arg.Any<IReadOnlyList<MemoryUnit>>(), Arg.Any<CancellationToken>())
            .Returns(validationResult);

        _mockMemoryStore.GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit>
            {
                new() { Content = "User's name is John", Type = MemoryType.Fact }
            });

        // Act
        var result = await _service.ProcessAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.FastTrackedFacts.Should().BeEmpty();
        result.SkippedFacts.Should().HaveCount(1);
        result.SkippedFacts[0].Reason.Should().Be(SkipReasonType.Duplicate);
    }

    [Fact]
    public async Task ProcessAsync_WithNoFacts_ShouldReturnEmpty()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "Hello, how are you?",
            UserId = "user1"
        };

        var extractionResult = new FactExtractionResult
        {
            Facts = [],
            OverallConfidence = 0f,
            ContextType = StatementContextType.Question
        };

        _mockExtractor.ExtractAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        // Act
        var result = await _service.ProcessAsync(context, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ExtractedFacts.Should().BeEmpty();
        result.FastTrackedFacts.Should().BeEmpty();
        result.StandardPathFacts.Should().BeEmpty();
    }

    #endregion

    #region ProcessBatchAsync Tests

    [Fact]
    public async Task ProcessBatchAsync_WithMultipleItems_ShouldProcessAll()
    {
        // Arrange
        var items = new List<SensoryMemory>
        {
            new() { Content = "My name is Alice", UserId = "user1", Role = "user" },
            new() { Content = "I live in Tokyo", UserId = "user1", Role = "user" },
            new() { Content = "Hello!", UserId = "user1", Role = "assistant" } // Should be skipped
        };

        var extractionResult = new FactExtractionResult
        {
            Facts =
            [
                new UserFact
                {
                    Content = "Test fact",
                    Confidence = 0.95f,
                    PromotionPath = PromotionPath.FastTrack
                }
            ],
            ContextType = StatementContextType.Direct
        };

        var validationResult = new FactValidationResult
        {
            IsValid = true,
            ConflictType = FactConflictType.None
        };

        _mockExtractor.ExtractAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        _mockExtractor.ValidateAsync(Arg.Any<UserFact>(), Arg.Any<IReadOnlyList<MemoryUnit>>(), Arg.Any<CancellationToken>())
            .Returns(validationResult);

        _mockMemoryStore.GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit>());

        // Act
        var result = await _service.ProcessBatchAsync(items, TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ItemsProcessed.Should().Be(3);
        // Only user messages processed (assistant skipped)
        result.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessBatchAsync_WithEmptyList_ShouldReturnEmpty()
    {
        // Act
        var result = await _service.ProcessBatchAsync([], TestContext.Current.CancellationToken);

        // Assert
        result.Success.Should().BeTrue();
        result.ItemsProcessed.Should().Be(0);
        result.Results.Should().BeEmpty();
    }

    #endregion

    #region GetUserProfileAsync Tests

    [Fact]
    public async Task GetUserProfileAsync_WithExistingFacts_ShouldReturnProfile()
    {
        // Arrange
        var entries = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "identity:user:name",
                Value = "User's name is John",
                Category = SemanticStoreCategory.Fact,
                Confidence = 0.95f,
                ConfirmationCount = 2,
                Metadata = new Dictionary<string, string>
                {
                    ["subject"] = "user",
                    ["predicate"] = "name",
                    ["object"] = "John"
                }
            },
            new()
            {
                Key = "preference:user:likes",
                Value = "User likes pizza",
                Category = SemanticStoreCategory.Preference,
                Confidence = 0.8f,
                ConfirmationCount = 1
            }
        };

        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(entries);

        // Act
        var profile = await _service.GetUserProfileAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        profile.UserId.Should().Be("user1");
        profile.TotalFacts.Should().Be(2);
        profile.Facts.Should().HaveCount(2);
        profile.FactsByCategory.Should().ContainKey(FactCategory.General);
        profile.FactsByCategory.Should().ContainKey(FactCategory.Preference);
    }

    [Fact]
    public async Task GetUserProfileAsync_WithCategoryFilter_ShouldFilterResults()
    {
        // Arrange
        var entries = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "preference:user:likes",
                Value = "User likes pizza",
                Category = SemanticStoreCategory.Preference,
                Confidence = 0.8f
            }
        };

        _mockArchiveStore.GetByCategoryAsync("user1", SemanticStoreCategory.Preference, Arg.Any<CancellationToken>())
            .Returns(entries);

        // Act
        var profile = await _service.GetUserProfileAsync("user1", FactCategory.Preference, TestContext.Current.CancellationToken);

        // Assert
        profile.TotalFacts.Should().Be(1);
        profile.Facts[0].Category.Should().Be(FactCategory.Preference);
    }

    [Fact]
    public async Task GetUserProfileAsync_WithNoFacts_ShouldReturnEmptyProfile()
    {
        // Arrange
        _mockArchiveStore.GetAllAsync("user1", Arg.Any<CancellationToken>())
            .Returns(new List<SemanticStoreEntry>());

        // Act
        var profile = await _service.GetUserProfileAsync("user1", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        profile.UserId.Should().Be("user1");
        profile.TotalFacts.Should().Be(0);
        profile.Facts.Should().BeEmpty();
    }

    #endregion

    #region Category Mapping Tests

    [Theory]
    [InlineData(FactCategory.Identity, SemanticStoreCategory.Fact)]
    [InlineData(FactCategory.Preference, SemanticStoreCategory.Preference)]
    [InlineData(FactCategory.Relationship, SemanticStoreCategory.Relationship)]
    [InlineData(FactCategory.Skill, SemanticStoreCategory.Skill)]
    [InlineData(FactCategory.Goal, SemanticStoreCategory.Goal)]
    [InlineData(FactCategory.Professional, SemanticStoreCategory.Work)]
    public void MapFactCategoryToSemanticCategory_ShouldMapCorrectly(FactCategory input, SemanticStoreCategory expected)
    {
        // Act
        var result = FastTrackPromoterService.MapFactCategoryToSemanticCategory(input);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(SemanticStoreCategory.Fact, FactCategory.General)]
    [InlineData(SemanticStoreCategory.Preference, FactCategory.Preference)]
    [InlineData(SemanticStoreCategory.Skill, FactCategory.Skill)]
    [InlineData(SemanticStoreCategory.Work, FactCategory.Professional)]
    public void MapSemanticCategoryToFactCategory_ShouldMapCorrectly(SemanticStoreCategory input, FactCategory expected)
    {
        // Act
        var result = FastTrackPromoterService.MapSemanticCategoryToFactCategory(input);

        // Assert
        result.Should().Be(expected);
    }

    #endregion
}
