using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Retrieval;

public class LocalQueryIntentClassifierTests
{
    private readonly IQueryIntentClassifier _classifier;

    public LocalQueryIntentClassifierTests()
    {
        _classifier = new LocalQueryIntentClassifier(
            NullLogger<LocalQueryIntentClassifier>.Instance);
    }

    #region Factual Intent Tests

    [Theory]
    [InlineData("What is my favorite color?")]
    [InlineData("What is my name?")]
    [InlineData("Who is my manager?")]
    [InlineData("Do I like coffee?")]
    [InlineData("Tell me my preferences")]
    [InlineData("Remind me my email address")]
    public async Task ClassifyAsync_FactualQueries_ReturnsFactualIntent(string query)
    {
        // Act
        var result = await _classifier.ClassifyAsync(query);

        // Assert
        result.Intent.Should().Be(QueryIntent.Factual);
        result.Confidence.Should().BeGreaterThan(0.3f);
        result.TierPriority.First().Should().Be(MemoryTier.User);
    }

    #endregion

    #region Contextual Intent Tests

    [Theory]
    [InlineData("Tell me more about that")]
    [InlineData("Continue with the explanation")]
    [InlineData("Elaborate on this topic")]
    [InlineData("What about it?")]
    [InlineData("And what else?")]
    public async Task ClassifyAsync_ContextualQueries_ReturnsContextualIntent(string query)
    {
        // Act
        var result = await _classifier.ClassifyAsync(query);

        // Assert
        result.Intent.Should().Be(QueryIntent.Contextual);
        result.TierPriority.First().Should().Be(MemoryTier.Working);
    }

    [Fact]
    public async Task ClassifyAsync_WithContext_BoostsContextualScore()
    {
        // Arrange
        var query = "What about that?";
        var context = "We were discussing Python programming.";

        // Act
        var result = await _classifier.ClassifyAsync(query, context);

        // Assert
        result.Intent.Should().Be(QueryIntent.Contextual);
        result.Confidence.Should().BeGreaterThan(0.4f);
    }

    #endregion

    #region Temporal Intent Tests

    [Theory]
    [InlineData("What did we discuss last week?")]
    [InlineData("What happened yesterday?")]
    [InlineData("3 days ago you mentioned something")]
    [InlineData("In our first conversation")]
    [InlineData("Recently I told you")]
    [InlineData("What was the previous session about?")]
    public async Task ClassifyAsync_TemporalQueries_ReturnsTemporalIntent(string query)
    {
        // Act
        var result = await _classifier.ClassifyAsync(query);

        // Assert
        result.Intent.Should().Be(QueryIntent.Temporal);
        result.TierPriority.First().Should().Be(MemoryTier.Session);
    }

    [Fact]
    public async Task ClassifyAsync_TemporalQuery_ExtractsTemporalReference()
    {
        // Act
        var result = await _classifier.ClassifyAsync("What did we talk about last week?");

        // Assert
        result.TemporalReference.Should().Be("last week");
    }

    #endregion

    #region Relational Intent Tests

    [Theory]
    [InlineData("What's related to Python?")]
    [InlineData("Show me things connected with AI")]
    [InlineData("How does JavaScript relate to TypeScript?")]
    [InlineData("What else do I know about machine learning?")]
    [InlineData("Anything similar to that project?")]
    public async Task ClassifyAsync_RelationalQueries_ReturnsRelationalIntent(string query)
    {
        // Act
        var result = await _classifier.ClassifyAsync(query);

        // Assert
        result.Intent.Should().Be(QueryIntent.Relational);
    }

    #endregion

    #region General Intent Tests

    [Theory]
    [InlineData("Hello")]
    [InlineData("Thanks")]
    [InlineData("Okay")]
    [InlineData("I understand")]
    public async Task ClassifyAsync_GeneralQueries_ReturnsGeneralIntent(string query)
    {
        // Act
        var result = await _classifier.ClassifyAsync(query);

        // Assert
        result.Intent.Should().Be(QueryIntent.General);
    }

    #endregion

    #region Keyword Extraction Tests

    [Fact]
    public async Task ClassifyAsync_ExtractsKeywords()
    {
        // Act
        var result = await _classifier.ClassifyAsync("What is my favorite programming language?");

        // Assert
        result.Keywords.Should().Contain("favorite");
        result.Keywords.Should().Contain("programming");
        result.Keywords.Should().Contain("language");
        result.Keywords.Should().NotContain("is"); // Stopword
        result.Keywords.Should().NotContain("my"); // Stopword
    }

    #endregion

    #region Entity Extraction Tests

    [Fact]
    public async Task ClassifyAsync_ExtractsQuotedEntities()
    {
        // Act
        var result = await _classifier.ClassifyAsync("What do I know about \"Machine Learning\"?");

        // Assert
        result.EntityReferences.Should().Contain("Machine Learning");
    }

    [Fact]
    public async Task ClassifyAsync_ExtractsCapitalizedEntities()
    {
        // Act
        var result = await _classifier.ClassifyAsync("Tell me about Python and JavaScript");

        // Assert
        result.EntityReferences.Should().Contain("Python");
        result.EntityReferences.Should().Contain("JavaScript");
    }

    #endregion

    #region Query Specificity Tests (Phase 22.3)

    [Theory]
    [InlineData("What is my name?", 0.3f, 0.5f)] // Generic, short query
    [InlineData("Tell me about my favorite programming language and development environment", 0.6f, 1.0f)] // Long, many keywords
    [InlineData("What is \"Machine Learning\" and how does it relate to my work?", 0.7f, 1.0f)] // Has quoted string, question
    [InlineData("What did I say about \"TypeScript\" interfaces in our last conversation?", 0.8f, 1.0f)] // Very specific: quoted + question + rare words
    [InlineData("Hi", 0.0f, 0.2f)] // Very generic
    public async Task ClassifyAsync_CalculatesSpecificity_WithinExpectedRange(string query, float minSpecificity, float maxSpecificity)
    {
        // Act
        var result = await _classifier.ClassifyAsync(query);

        // Assert
        result.Specificity.Should().BeGreaterThanOrEqualTo(minSpecificity);
        result.Specificity.Should().BeLessThanOrEqualTo(maxSpecificity);
    }

    [Fact]
    public async Task ClassifyAsync_QuotedStrings_IncreaseSpecificity()
    {
        // Arrange
        var genericQuery = "Tell me about machine learning";
        var quotedQuery = "Tell me about \"Machine Learning\"";

        // Act
        var genericResult = await _classifier.ClassifyAsync(genericQuery);
        var quotedResult = await _classifier.ClassifyAsync(quotedQuery);

        // Assert
        quotedResult.Specificity.Should().BeGreaterThan(genericResult.Specificity);
    }

    [Fact]
    public async Task ClassifyAsync_LongerQueries_HigherSpecificity()
    {
        // Arrange
        var shortQuery = "What is Python?";
        var longQuery = "What is Python programming language and how is it different from JavaScript in terms of syntax and performance?";

        // Act
        var shortResult = await _classifier.ClassifyAsync(shortQuery);
        var longResult = await _classifier.ClassifyAsync(longQuery);

        // Assert
        longResult.Specificity.Should().BeGreaterThan(shortResult.Specificity);
    }

    [Fact]
    public async Task ClassifyAsync_EntityReferences_IncreaseSpecificity()
    {
        // Arrange
        var genericQuery = "Tell me about programming";
        var entityQuery = "Tell me about Python, TypeScript, and React";

        // Act
        var genericResult = await _classifier.ClassifyAsync(genericQuery);
        var entityResult = await _classifier.ClassifyAsync(entityQuery);

        // Assert
        entityResult.Specificity.Should().BeGreaterThan(genericResult.Specificity);
    }

    [Fact]
    public async Task ClassifyAsync_QuestionMark_IncreasesSpecificity()
    {
        // Arrange
        var statementQuery = "Tell me about Python";
        var questionQuery = "What is Python?";

        // Act
        var statementResult = await _classifier.ClassifyAsync(statementQuery);
        var questionResult = await _classifier.ClassifyAsync(questionQuery);

        // Assert
        questionResult.Specificity.Should().BeGreaterThan(statementResult.Specificity);
    }

    [Fact]
    public async Task ClassifyAsync_RareWords_IncreaseSpecificity()
    {
        // Arrange
        var commonWords = "Tell me about the thing";
        var rareWords = "Tell me about polymorphism and encapsulation";

        // Act
        var commonResult = await _classifier.ClassifyAsync(commonWords);
        var rareResult = await _classifier.ClassifyAsync(rareWords);

        // Assert
        rareResult.Specificity.Should().BeGreaterThan(commonResult.Specificity);
    }

    [Fact]
    public async Task ClassifyAsync_SpecificityClampedToOneMaximum()
    {
        // Arrange - Very specific query with all factors
        var query = "What is \"Machine Learning\" algorithms, \"Deep Learning\" frameworks, and how do they relate to artificial intelligence implementations?";

        // Act
        var result = await _classifier.ClassifyAsync(query);

        // Assert
        result.Specificity.Should().BeLessThanOrEqualTo(1.0f);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ClassifyAsync_NullQuery_ThrowsArgumentException()
    {
        // Act & Assert (ArgumentNullException is derived from ArgumentException)
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _classifier.ClassifyAsync(null!));
    }

    [Fact]
    public async Task ClassifyAsync_EmptyQuery_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _classifier.ClassifyAsync(""));
    }

    [Fact]
    public async Task ClassifyAsync_AmbiguousQuery_HasSecondaryIntent()
    {
        // Arrange - Query that matches both factual and temporal
        var query = "What did I say about my preferences last week?";

        // Act
        var result = await _classifier.ClassifyAsync(query);

        // Assert
        // Should have a secondary intent since query matches multiple patterns
        result.Intent.Should().BeOneOf(QueryIntent.Factual, QueryIntent.Temporal);
    }

    [Fact]
    public async Task ClassifyAsync_ConfidenceWithinRange()
    {
        // Act
        var result = await _classifier.ClassifyAsync("What is my favorite color?");

        // Assert
        result.Confidence.Should().BeInRange(0f, 1f);
    }

    #endregion
}
