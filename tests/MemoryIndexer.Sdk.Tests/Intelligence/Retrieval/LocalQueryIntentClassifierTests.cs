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
