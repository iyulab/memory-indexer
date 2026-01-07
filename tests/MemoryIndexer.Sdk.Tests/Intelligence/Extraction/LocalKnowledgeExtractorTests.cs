using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Extraction;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Extraction;

/// <summary>
/// Tests for LocalKnowledgeExtractor (Phase 25).
/// </summary>
public sealed class LocalKnowledgeExtractorTests
{
    private readonly LocalKnowledgeExtractor _extractor;

    public LocalKnowledgeExtractorTests()
    {
        _extractor = new LocalKnowledgeExtractor(NullLogger<LocalKnowledgeExtractor>.Instance);
    }

    #region IsIt Pattern Tests

    [Fact]
    public async Task ExtractAsync_IsItPattern_YesAnswer_ShouldExtractPositiveAssertion()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is blue", facts[0].Content);
        Assert.Equal(0.8f, facts[0].Confidence);
        Assert.Equal(0.7f, facts[0].Importance);
        Assert.Equal("Pattern:IsIt_Yes", facts[0].Source);
    }

    [Fact]
    public async Task ExtractAsync_IsItPattern_NoAnswer_ShouldExtractNegativeAssertion()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it small?",
            Answer = "no",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is not small", facts[0].Content);
        Assert.Equal(0.9f, facts[0].Confidence);
        Assert.Equal(0.6f, facts[0].Importance);
        Assert.Equal("Pattern:IsIt_No", facts[0].Source);
    }

    [Fact]
    public async Task ExtractAsync_IsItPattern_MaybeAnswer_ShouldExtractUncertainAssertion()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it dangerous?",
            Answer = "maybe",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean may be dangerous", facts[0].Content);
        Assert.Equal(0.5f, facts[0].Confidence);
        Assert.Equal(0.5f, facts[0].Importance);
        Assert.Equal("Pattern:IsIt_Maybe", facts[0].Source);
    }

    [Fact]
    public async Task ExtractAsync_IsItPattern_WithParentheses_ShouldStillMatch()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it salty (taste)?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is salty", facts[0].Content);
    }

    #endregion

    #region IsItA Pattern Tests

    [Fact]
    public async Task ExtractAsync_IsItAPattern_YesAnswer_ShouldExtractCategoryAssertion()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it a liquid?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is a liquid", facts[0].Content);
        Assert.Equal(0.85f, facts[0].Confidence);
        Assert.Equal(0.75f, facts[0].Importance);
        Assert.Equal("Pattern:IsItA_Yes", facts[0].Source);
    }

    [Fact]
    public async Task ExtractAsync_IsItAnPattern_YesAnswer_ShouldExtractCategoryAssertion()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it an object?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is an object", facts[0].Content);
        Assert.Equal(0.85f, facts[0].Confidence);
        Assert.Equal(0.75f, facts[0].Importance);
        Assert.Equal("Pattern:IsItA_Yes", facts[0].Source);
    }

    [Fact]
    public async Task ExtractAsync_IsItAPattern_NoAnswer_ShouldExtractNegativeCategoryAssertion()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it a person?",
            Answer = "no",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is not a person", facts[0].Content);
        Assert.Equal(0.9f, facts[0].Confidence);
        Assert.Equal(0.7f, facts[0].Importance);
        Assert.Equal("Pattern:IsItA_No", facts[0].Source);
    }

    #endregion

    #region DoesItHave Pattern Tests

    [Fact]
    public async Task ExtractAsync_DoesItHavePattern_YesAnswer_ShouldExtractPossessionAssertion()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Does it have waves?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean has waves", facts[0].Content);
        Assert.Equal(0.8f, facts[0].Confidence);
        Assert.Equal(0.65f, facts[0].Importance);
        Assert.Equal("Pattern:DoesItHave_Yes", facts[0].Source);
    }

    [Fact]
    public async Task ExtractAsync_DoesItHavePattern_NoAnswer_ShouldExtractNegativePossession()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Does it have legs?",
            Answer = "no",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean does not have legs", facts[0].Content);
        Assert.Equal(0.85f, facts[0].Confidence);
        Assert.Equal(0.6f, facts[0].Importance);
        Assert.Equal("Pattern:DoesItHave_No", facts[0].Source);
    }

    #endregion

    #region CanIt Pattern Tests

    [Fact]
    public async Task ExtractAsync_CanItPattern_YesAnswer_ShouldExtractCapabilityAssertion()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Can it move?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean can move", facts[0].Content);
        Assert.Equal(0.75f, facts[0].Confidence);
        Assert.Equal(0.6f, facts[0].Importance);
        Assert.Equal("Pattern:CanIt_Yes", facts[0].Source);
    }

    [Fact]
    public async Task ExtractAsync_CanItPattern_NoAnswer_ShouldExtractNegativeCapability()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Can it fly?",
            Answer = "no",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean cannot fly", facts[0].Content);
        Assert.Equal(0.8f, facts[0].Confidence);
        Assert.Equal(0.65f, facts[0].Importance);
        Assert.Equal("Pattern:CanIt_No", facts[0].Source);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ExtractAsync_NoSubject_ShouldUseDefaultIt()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = null,
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("It is blue", facts[0].Content);
    }

    [Fact]
    public async Task ExtractAsync_UnknownAnswer_ShouldReturnEmpty()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "i dont know",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Empty(facts);
    }

    [Fact]
    public async Task ExtractAsync_NoPatternMatch_ShouldReturnEmpty()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "What color is it?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Empty(facts);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("y")]
    [InlineData("true")]
    [InlineData("YES")]
    [InlineData("Yes")]
    public async Task ExtractAsync_YesVariations_ShouldAllBeRecognized(string answer)
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = answer,
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Contains("is blue", facts[0].Content);
    }

    [Theory]
    [InlineData("no")]
    [InlineData("n")]
    [InlineData("false")]
    [InlineData("NO")]
    [InlineData("No")]
    public async Task ExtractAsync_NoVariations_ShouldAllBeRecognized(string answer)
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = answer,
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Contains("is not blue", facts[0].Content);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("m")]
    [InlineData("uncertain")]
    [InlineData("not sure")]
    [InlineData("MAYBE")]
    public async Task ExtractAsync_MaybeVariations_ShouldAllBeRecognized(string answer)
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = answer,
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Contains("may be blue", facts[0].Content);
    }

    [Fact]
    public async Task ExtractAsync_QuestionWithQuestionMark_ShouldStillMatch()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is blue", facts[0].Content);
    }

    [Fact]
    public async Task ExtractAsync_QuestionWithoutQuestionMark_ShouldStillMatch()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "Is it blue",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is blue", facts[0].Content);
    }

    [Fact]
    public async Task ExtractAsync_CaseInsensitiveQuestion_ShouldMatch()
    {
        // Arrange
        var context = new KnowledgeExtractionContext
        {
            Question = "IS IT BLUE?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var facts = await _extractor.ExtractAsync(context);

        // Assert
        Assert.Single(facts);
        Assert.Equal("The ocean is BLUE", facts[0].Content);
    }

    #endregion

    #region Confidence and Importance Tests

    [Fact]
    public async Task ExtractAsync_YesAnswer_ShouldHaveHigherConfidenceThanMaybe()
    {
        // Arrange
        var yesContext = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        var maybeContext = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "maybe",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var yesFacts = await _extractor.ExtractAsync(yesContext);
        var maybeFacts = await _extractor.ExtractAsync(maybeContext);

        // Assert
        Assert.True(yesFacts[0].Confidence > maybeFacts[0].Confidence);
    }

    [Fact]
    public async Task ExtractAsync_NoAnswer_ShouldHaveHighestConfidence()
    {
        // Arrange
        var noContext = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "no",
            Subject = "the ocean",
            UserId = "test-user"
        };

        var yesContext = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var noFacts = await _extractor.ExtractAsync(noContext);
        var yesFacts = await _extractor.ExtractAsync(yesContext);

        // Assert
        Assert.True(noFacts[0].Confidence >= yesFacts[0].Confidence);
    }

    [Fact]
    public async Task ExtractAsync_CategoryAssertion_ShouldHaveHigherImportance()
    {
        // Arrange
        var categoryContext = new KnowledgeExtractionContext
        {
            Question = "Is it a liquid?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        var propertyContext = new KnowledgeExtractionContext
        {
            Question = "Is it blue?",
            Answer = "yes",
            Subject = "the ocean",
            UserId = "test-user"
        };

        // Act
        var categoryFacts = await _extractor.ExtractAsync(categoryContext);
        var propertyFacts = await _extractor.ExtractAsync(propertyContext);

        // Assert
        Assert.True(categoryFacts[0].Importance > propertyFacts[0].Importance);
    }

    #endregion
}
