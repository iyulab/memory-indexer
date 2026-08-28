using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Mock;
using MemoryIndexer.Models;
using Xunit;

namespace MemoryIndexer.Tests.Services.Extraction;

/// <summary>
/// Tests for IFactExtractor implementations.
/// </summary>
public class FactExtractorTests
{
    private readonly MockFactExtractor _extractor = new MockFactExtractor();

    #region Context Detection Tests

    [Fact]
    public async Task ExtractAsync_WithDirectStatement_ShouldDetectDirectContext()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "My name is John",
            UserId = "user1",
            Role = "user"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.ContextType.Should().Be(StatementContextType.Direct);
        result.HasFacts.Should().BeTrue();
    }

    [Fact]
    public async Task ExtractAsync_WithQuotedContent_ShouldDetectQuotedContext()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "In the novel, he says 'My name is Lincoln'",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.ContextType.Should().Be(StatementContextType.Quoted);
        result.HasFacts.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractAsync_WithHypotheticalContent_ShouldDetectHypotheticalContext()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "If I were a doctor, I would help people",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.ContextType.Should().Be(StatementContextType.Hypothetical);
    }

    [Fact]
    public async Task ExtractAsync_WithQuestionContent_ShouldDetectQuestionContext()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "Is my name John?",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.ContextType.Should().Be(StatementContextType.Question);
    }

    #endregion

    #region Fact Extraction Tests

    [Fact]
    public async Task ExtractAsync_WithNameStatement_ShouldExtractIdentityFact()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "My name is Alice",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.Facts.Should().ContainSingle();
        var fact = result.Facts[0];
        fact.Category.Should().Be(FactCategory.Identity);
        fact.Predicate.Should().Be("name");
        fact.Object.Should().Be("Alice");
    }

    [Fact]
    public async Task ExtractAsync_WithAgeStatement_ShouldExtractIdentityFact()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "I am 30 years old",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.Facts.Should().ContainSingle();
        var fact = result.Facts[0];
        fact.Category.Should().Be(FactCategory.Identity);
        fact.Predicate.Should().Be("age");
        fact.Object.Should().Be("30");
    }

    [Fact]
    public async Task ExtractAsync_WithOccupationStatement_ShouldExtractProfessionalFact()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "I work as a software engineer",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.Facts.Should().ContainSingle();
        var fact = result.Facts[0];
        fact.Category.Should().Be(FactCategory.Professional);
        fact.Predicate.Should().Be("occupation");
    }

    [Fact]
    public async Task ExtractAsync_WithLocationStatement_ShouldExtractLocationFact()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "I live in Seoul",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.Facts.Should().ContainSingle();
        var fact = result.Facts[0];
        fact.Category.Should().Be(FactCategory.Location);
        fact.Object.Should().Be("Seoul");
    }

    #endregion

    #region Promotion Path Tests

    [Fact]
    public async Task ExtractAsync_WithHighConfidenceFact_ShouldGetFastTrackPromotion()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "My name is Bob",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.Facts.Should().ContainSingle();
        var fact = result.Facts[0];
        fact.Confidence.Should().BeGreaterThanOrEqualTo(0.9f);
        fact.PromotionPath.Should().Be(PromotionPath.FastTrack);
    }

    [Fact]
    public async Task ExtractAsync_WithModerateConfidenceFact_ShouldGetStandardPromotion()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "I live in Tokyo",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.Facts.Should().ContainSingle();
        var fact = result.Facts[0];
        fact.Confidence.Should().BeInRange(0.5f, 0.9f);
        fact.PromotionPath.Should().Be(PromotionPath.Standard);
    }

    [Fact]
    public async Task ExtractAsync_FastTrackFacts_ShouldFilterCorrectly()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "My name is Charlie",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.FastTrackFacts.Should().NotBeEmpty();
        result.FastTrackFacts.Should().OnlyContain(f => f.PromotionPath == PromotionPath.FastTrack);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ValidateAsync_WithNoExistingFacts_ShouldAllowAdd()
    {
        // Arrange
        var fact = new UserFact
        {
            Content = "User's name is John",
            Category = FactCategory.Identity,
            Confidence = 0.95f,
            Subject = "user",
            Predicate = "name",
            Object = "John"
        };

        // Act
        var result = await _extractor.ValidateAsync(fact, []);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ConflictType.Should().Be(FactConflictType.None);
        result.RecommendedAction.Should().Be(FactConflictAction.Add);
    }

    [Fact]
    public async Task ValidateAsync_WithExactDuplicate_ShouldDetectDuplicate()
    {
        // Arrange
        var fact = new UserFact
        {
            Content = "User's name is John",
            Category = FactCategory.Identity,
            Confidence = 0.95f
        };

        var existingFacts = new List<MemoryUnit>
        {
            new()
            {
                Content = "User's name is John",
                Type = MemoryType.Fact,
                Confidence = 0.9f
            }
        };

        // Act
        var result = await _extractor.ValidateAsync(fact, existingFacts);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ConflictType.Should().Be(FactConflictType.Duplicate);
        result.RecommendedAction.Should().Be(FactConflictAction.Skip);
    }

    [Fact]
    public async Task ValidateAsync_WithValueUpdate_ShouldDetectUpdate()
    {
        // Arrange
        var fact = new UserFact
        {
            Content = "User's name is Jane",
            Category = FactCategory.Identity,
            Confidence = 0.95f,
            Subject = "user",
            Predicate = "name",
            Object = "Jane"
        };

        var existingFacts = new List<MemoryUnit>
        {
            new()
            {
                Content = "User's name is John",
                Type = MemoryType.Fact,
                Confidence = 0.9f
            }
        };

        // Act
        var result = await _extractor.ValidateAsync(fact, existingFacts);

        // Assert
        result.ConflictType.Should().Be(FactConflictType.ValueUpdate);
        result.ConflictingMemory.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateAsync_WithHigherConfidence_ShouldRecommendReplace()
    {
        // Arrange
        var fact = new UserFact
        {
            Content = "User's name is Jane",
            Category = FactCategory.Identity,
            Confidence = 0.95f,
            Subject = "user",
            Predicate = "name",
            Object = "Jane"
        };

        var existingFacts = new List<MemoryUnit>
        {
            new()
            {
                Content = "User's name is John",
                Type = MemoryType.Fact,
                Confidence = 0.8f
            }
        };

        // Act
        var result = await _extractor.ValidateAsync(fact, existingFacts);

        // Assert
        result.RecommendedAction.Should().Be(FactConflictAction.Replace);
    }

    #endregion

    #region Korean Language Tests

    [Fact]
    public async Task ExtractAsync_WithKoreanNameStatement_ShouldExtractFact()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "내 이름은 김철수야",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.HasFacts.Should().BeTrue();
        result.Facts[0].Category.Should().Be(FactCategory.Identity);
    }

    [Fact]
    public async Task ExtractAsync_WithKoreanQuotedContent_ShouldDetectQuoted()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "소설에서 주인공이 '내 이름은 링컨이오'라고 말했다",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.ContextType.Should().Be(StatementContextType.Quoted);
        result.HasFacts.Should().BeFalse();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ExtractAsync_WithEmptyContent_ShouldReturnNoFacts()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.HasFacts.Should().BeFalse();
        result.OverallConfidence.Should().Be(0f);
    }

    [Fact]
    public async Task ExtractAsync_WithMultipleFacts_ShouldExtractAll()
    {
        // Arrange
        var context = new FactExtractionContext
        {
            Content = "My name is David. I am 25 years old. I live in Paris.",
            UserId = "user1"
        };

        // Act
        var result = await _extractor.ExtractAsync(context);

        // Assert
        result.Facts.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    #endregion
}
