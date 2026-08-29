using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Conflict;

/// <summary>
/// Tests for FactValidatorService.
/// Phase v0.9.2: Fact Conflict Resolution.
/// </summary>
public class FactValidatorServiceTests
{
    private readonly IEmbeddingService _mockEmbeddingService;
    private readonly ILogger<FactValidatorService> _mockLogger;
    private readonly FactValidatorService _service;

    private readonly ReadOnlyMemory<float> _testEmbedding = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };

    public FactValidatorServiceTests()
    {
        _mockEmbeddingService = Substitute.For<IEmbeddingService>();
        _mockLogger = Substitute.For<ILogger<FactValidatorService>>();

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_testEmbedding);

        _service = new FactValidatorService(_mockEmbeddingService, _mockLogger);
    }

    #region ValidateAsync Tests

    [Fact]
    public async Task ValidateAsync_WithNoExistingFacts_ShouldReturnValid()
    {
        // Arrange
        var newFact = new UserFact
        {
            Content = "User's name is John",
            Category = FactCategory.Identity,
            Confidence = 0.95f
        };

        // Act
        var result = await _service.ValidateAsync(newFact, [], cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.IsValid.Should().BeTrue();
        result.RecommendedAction.Should().Be(FactConflictAction.Add);
        result.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_WithExactDuplicate_ShouldReturnSkip()
    {
        // Arrange
        var newFact = new UserFact
        {
            Content = "User's name is John",
            Category = FactCategory.Identity,
            Confidence = 0.95f
        };

        var existingFacts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "identity:user:name",
                Value = "User's name is John",
                Category = SemanticStoreCategory.Fact,
                Confidence = 0.9f,
                IsActive = true
            }
        };

        // Act
        var result = await _service.ValidateAsync(newFact, existingFacts, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.IsValid.Should().BeFalse();
        result.RecommendedAction.Should().Be(FactConflictAction.Skip);
        result.Conflicts.Should().HaveCount(1);
        result.Conflicts[0].ConflictType.Should().Be(FactConflictType.Duplicate);
    }

    [Fact]
    public async Task ValidateAsync_IdentityFact_WithContradiction_ShouldRequireConfirmation()
    {
        // Arrange
        var newFact = new UserFact
        {
            Content = "User's name is Mike",
            Category = FactCategory.Identity,
            Subject = "user",
            Predicate = "name",
            Object = "Mike",
            Confidence = 0.9f
        };

        var existingFacts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "identity:user:name",
                Value = "User's name is John",
                Category = SemanticStoreCategory.Fact,
                Confidence = 0.85f,
                IsActive = true,
                Metadata = new Dictionary<string, string>
                {
                    ["subject"] = "user",
                    ["predicate"] = "name",
                    ["object"] = "John"
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(newFact, existingFacts, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.RecommendedAction.Should().Be(FactConflictAction.RequireReview);
        result.RequiresConfirmation.Should().BeTrue();
        result.ConfirmationQuestion.Should().Contain("Update");
    }

    [Fact]
    public async Task ValidateAsync_PreferenceFact_WithUpdate_ShouldReplaceOrArchive()
    {
        // Arrange
        var newFact = new UserFact
        {
            Content = "User likes pizza",
            Category = FactCategory.Preference,
            Subject = "user",
            Predicate = "likes",
            Object = "pizza",
            Confidence = 0.9f
        };

        var existingFacts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "preference:user:likes",
                Value = "User likes burgers",
                Category = SemanticStoreCategory.Preference,
                Confidence = 0.7f,
                IsActive = true,
                Metadata = new Dictionary<string, string>
                {
                    ["subject"] = "user",
                    ["predicate"] = "likes",
                    ["object"] = "burgers"
                }
            }
        };

        // Act
        var result = await _service.ValidateAsync(newFact, existingFacts, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Preference allows update without confirmation
        result.RequiresConfirmation.Should().BeFalse();
        result.RecommendedAction.Should().BeOneOf(
            FactConflictAction.Replace,
            FactConflictAction.ArchiveAndAdd,
            FactConflictAction.Add);
    }

    [Fact]
    public async Task ValidateAsync_WithHighConfidenceDifferential_ShouldAutoReplace()
    {
        // Arrange
        var newFact = new UserFact
        {
            Content = "User works at Samsung",
            Category = FactCategory.Professional,
            Confidence = 0.95f
        };

        var existingFacts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "professional:user:company",
                Value = "User works at LG",
                Category = SemanticStoreCategory.Work,
                Confidence = 0.5f, // Low confidence
                IsActive = true,
                Embedding = _testEmbedding
            }
        };

        var options = new FactValidationOptions
        {
            ConfidenceDifferentialThreshold = 0.2f,
            AllowAutoResolution = true
        };

        // Act
        var result = await _service.ValidateAsync(newFact, existingFacts, options, TestContext.Current.CancellationToken);

        // Assert - High confidence differential should enable auto-resolution
        // (confidence diff = 0.95 - 0.5 = 0.45, which is > 0.2)
        result.RequiresConfirmation.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_SkipsInactiveEntries()
    {
        // Arrange
        var newFact = new UserFact
        {
            Content = "User's name is John",
            Category = FactCategory.Identity,
            Confidence = 0.95f
        };

        var existingFacts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "identity:user:name",
                Value = "User's name is John",
                Category = SemanticStoreCategory.Fact,
                Confidence = 0.9f,
                IsActive = false // Inactive entry
            }
        };

        // Act
        var result = await _service.ValidateAsync(newFact, existingFacts, cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Should not detect conflict with inactive entry
        result.IsValid.Should().BeTrue();
        result.Conflicts.Should().BeEmpty();
    }

    #endregion

    #region DetectConflictsAsync Tests

    [Fact]
    public async Task DetectConflictsAsync_WithSpoMatch_ShouldDetectValueUpdate()
    {
        // Arrange
        var newFact = new UserFact
        {
            Content = "User lives in Busan",
            Category = FactCategory.Location,
            Subject = "user",
            Predicate = "lives in",
            Object = "Busan"
        };

        var existingFacts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "location:user:residence",
                Value = "User lives in Seoul",
                Category = SemanticStoreCategory.Fact,
                IsActive = true,
                Metadata = new Dictionary<string, string>
                {
                    ["subject"] = "user",
                    ["predicate"] = "lives in",
                    ["object"] = "Seoul"
                }
            }
        };

        // Act
        var conflicts = await _service.DetectConflictsAsync(newFact, existingFacts, TestContext.Current.CancellationToken);

        // Assert
        conflicts.Should().HaveCount(1);
        conflicts[0].ConflictType.Should().Be(FactConflictType.ValueUpdate);
        conflicts[0].SpoMatch.Should().BeTrue();
    }

    [Fact]
    public async Task DetectConflictsAsync_WithContradictionPattern_ShouldDetect()
    {
        // Arrange
        var newFact = new UserFact
        {
            Content = "User likes coffee",
            Category = FactCategory.Preference
        };

        // Create embeddings with ~0.87 similarity (in the 0.8-0.95 range for contradiction detection)
        // Cosine similarity = dot(a,b) / (|a| * |b|)
        // Using orthogonal-ish vectors to get lower similarity
        var newEmbedding = new float[] { 1.0f, 0.0f, 0.0f, 0.0f };
        var existingEmbedding = new float[] { 0.85f, 0.52f, 0.0f, 0.0f }; // ~0.85 similarity

        _mockEmbeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(newEmbedding));

        var existingFacts = new List<SemanticStoreEntry>
        {
            new()
            {
                Key = "preference:user:coffee",
                Value = "User dislikes coffee", // Contradiction pattern: likes vs dislikes
                Category = SemanticStoreCategory.Preference,
                IsActive = true,
                Embedding = new ReadOnlyMemory<float>(existingEmbedding)
            }
        };

        // Act
        var conflicts = await _service.DetectConflictsAsync(newFact, existingFacts, TestContext.Current.CancellationToken);

        // Assert - Should detect contradiction based on pattern matching
        conflicts.Should().NotBeEmpty();
        conflicts.Should().Contain(c => c.ConflictType == FactConflictType.Contradiction);
    }

    #endregion

    #region ResolveConflictAsync Tests

    [Theory]
    [InlineData(FactResolutionStrategy.RecencyFirst, FactConflictAction.Replace)]
    [InlineData(FactResolutionStrategy.ConfidenceFirst, FactConflictAction.Replace)]
    [InlineData(FactResolutionStrategy.TemporalPartition, FactConflictAction.ArchiveAndAdd)]
    [InlineData(FactResolutionStrategy.KeepBoth, FactConflictAction.Add)]
    [InlineData(FactResolutionStrategy.RequireConfirmation, FactConflictAction.RequireReview)]
    public async Task ResolveConflictAsync_WithStrategy_ShouldApplyCorrectAction(
        FactResolutionStrategy strategy,
        FactConflictAction expectedAction)
    {
        // Arrange
        var conflict = new FactConflict
        {
            ConflictType = FactConflictType.ValueUpdate,
            Category = FactCategory.General,
            ExistingFact = new SemanticStoreEntry
            {
                Key = "test",
                Value = "old value"
            }
        };

        // Act
        var result = await _service.ResolveConflictAsync(conflict, strategy, TestContext.Current.CancellationToken);

        // Assert
        result.Action.Should().Be(expectedAction);
        result.AppliedStrategy.Should().Be(strategy);
    }

    #endregion

    #region GetCategoryRule Tests

    [Theory]
    [InlineData(FactCategory.Identity, FactResolutionStrategy.RequireConfirmation, true)]
    [InlineData(FactCategory.Preference, FactResolutionStrategy.RecencyFirst, false)]
    [InlineData(FactCategory.Health, FactResolutionStrategy.RequireConfirmation, true)]
    [InlineData(FactCategory.Location, FactResolutionStrategy.TemporalPartition, false)]
    [InlineData(FactCategory.Professional, FactResolutionStrategy.TemporalPartition, false)]
    public void GetCategoryRule_ReturnsCorrectDefaults(
        FactCategory category,
        FactResolutionStrategy expectedStrategy,
        bool expectedRequiresConfirmation)
    {
        // Act
        var rule = _service.GetCategoryRule(category);

        // Assert
        rule.Category.Should().Be(category);
        rule.DefaultStrategy.Should().Be(expectedStrategy);
        rule.RequiresExplicitConfirmation.Should().Be(expectedRequiresConfirmation);
    }

    [Fact]
    public void GetCategoryRule_UnknownCategory_ReturnGeneralDefault()
    {
        // Act
        var rule = _service.GetCategoryRule((FactCategory)999);

        // Assert
        rule.Category.Should().Be(FactCategory.General);
    }

    #endregion

    #region Category Resolution Rules Tests

    [Fact]
    public void CategoryResolutionRuleDefaults_ShouldHaveAllCategories()
    {
        // Assert
        CategoryResolutionRule.Defaults.Should().HaveCount(10);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Identity);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Preference);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Relationship);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Location);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Temporal);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Skill);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Goal);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Health);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.Professional);
        CategoryResolutionRule.Defaults.Should().ContainKey(FactCategory.General);
    }

    [Fact]
    public void CategoryResolutionRuleDefaults_IdentityAndHealth_ShouldRequireConfirmation()
    {
        // Assert
        CategoryResolutionRule.Defaults[FactCategory.Identity].RequiresExplicitConfirmation.Should().BeTrue();
        CategoryResolutionRule.Defaults[FactCategory.Health].RequiresExplicitConfirmation.Should().BeTrue();
    }

    [Fact]
    public void CategoryResolutionRuleDefaults_LocationAndProfessional_ShouldPreserveHistory()
    {
        // Assert
        CategoryResolutionRule.Defaults[FactCategory.Location].PreserveHistory.Should().BeTrue();
        CategoryResolutionRule.Defaults[FactCategory.Professional].PreserveHistory.Should().BeTrue();
        CategoryResolutionRule.Defaults[FactCategory.Relationship].PreserveHistory.Should().BeTrue();
    }

    #endregion
}
