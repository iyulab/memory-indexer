using MemoryIndexer.Configuration;
using MemoryIndexer.Models;
using MemoryIndexer.Scoring;
using Microsoft.Extensions.Options;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Scoring;

public class DefaultScoringServiceTests
{
    private readonly DefaultScoringService _scoringService;

    public DefaultScoringServiceTests()
    {
        var options = Options.Create(new MemoryIndexerOptions
        {
            Scoring = new ScoringOptions
            {
                RecencyWeight = 1.0f,
                ImportanceWeight = 1.0f,
                RelevanceWeight = 1.0f,
                DecayFactor = 0.99f,
                MaxExpectedAccessCount = 100
            }
        });
        _scoringService = new DefaultScoringService(options);
    }

    #region CalculateKeywordBoost Tests

    [Fact]
    public void CalculateKeywordBoost_MatchingKeywords_ReturnsPositiveScore()
    {
        // Arrange
        var query = "Is it edible food?";
        var memoryContent = "CONFIRMED: The secret is edible and can be eaten as food.";

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        Assert.True(score > 0f, $"Score {score} should be positive for matching keywords");
    }

    [Fact]
    public void CalculateKeywordBoost_NoMatchingKeywords_ReturnsZero()
    {
        // Arrange
        var query = "Is it a vehicle?";
        var memoryContent = "The item is a fruit that grows on trees.";

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        Assert.Equal(0f, score);
    }

    [Fact]
    public void CalculateKeywordBoost_AllKeywordsMatch_ReturnsOne()
    {
        // Arrange
        var query = "apple fruit red";
        var memoryContent = "This is a red apple fruit.";

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        Assert.Equal(1f, score);
    }

    [Fact]
    public void CalculateKeywordBoost_StopWordsIgnored_OnlyCountsMeaningfulWords()
    {
        // Arrange
        var query = "the and for are what is this";  // All stop words except "what"
        var memoryContent = "Something completely different.";

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        // "what" is a stop word, so no meaningful keywords
        Assert.Equal(0f, score);
    }

    [Fact]
    public void CalculateKeywordBoost_ShortWordsIgnored_MinLength3()
    {
        // Arrange
        var query = "a an is it ok";  // All short words
        var memoryContent = "a an is it ok here.";

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        Assert.Equal(0f, score);  // No keywords >= 3 chars
    }

    [Fact]
    public void CalculateKeywordBoost_CaseInsensitive()
    {
        // Arrange
        var query = "APPLE RED FRUIT";
        var memoryContent = "This is a red apple fruit.";

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        Assert.Equal(1f, score);
    }

    [Fact]
    public void CalculateKeywordBoost_EmptyQuery_ReturnsZero()
    {
        // Arrange
        var query = "";
        var memoryContent = "Some content here.";

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        Assert.Equal(0f, score);
    }

    [Fact]
    public void CalculateKeywordBoost_EmptyContent_ReturnsZero()
    {
        // Arrange
        var query = "apple fruit";
        var memoryContent = "";

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        Assert.Equal(0f, score);
    }

    [Fact]
    public void CalculateKeywordBoost_PartialMatch_ReturnsProportionalScore()
    {
        // Arrange
        var query = "apple banana cherry";  // 3 keywords
        var memoryContent = "This is an apple.";  // Only 1 matches

        // Act
        var score = _scoringService.CalculateKeywordBoost(query, memoryContent);

        // Assert
        Assert.True(Math.Abs(score - (1f / 3f)) < 0.01f, $"Score {score} should be ~0.33 for 1/3 match");
    }

    #endregion

    #region CalculateContentTypeBoost Tests

    [Theory]
    [InlineData("CONFIRMED: The secret is edible", 0.3f)]
    [InlineData("Yes, it is natural", 0.3f)]
    [InlineData("The item is a fruit", 0.3f)]
    [InlineData("It can be eaten", 0.3f)]
    public void CalculateContentTypeBoost_PositiveIndicators_Returns03(string content, float expectedBoost)
    {
        // Act
        var score = _scoringService.CalculateContentTypeBoost(content);

        // Assert
        Assert.Equal(expectedBoost, score);
    }

    [Theory]
    [InlineData("RULED OUT: Not a vehicle", 0.1f)]
    [InlineData("No, it does not fly", 0.1f)]
    [InlineData("The item cannot be eaten", 0.1f)]
    [InlineData("It doesn't have wheels", 0.1f)]
    public void CalculateContentTypeBoost_NegativeIndicators_Returns01(string content, float expectedBoost)
    {
        // Act
        var score = _scoringService.CalculateContentTypeBoost(content);

        // Assert
        Assert.Equal(expectedBoost, score);
    }

    [Fact]
    public void CalculateContentTypeBoost_NeutralContent_ReturnsZero()
    {
        // Arrange
        var content = "The weather today is sunny.";

        // Act
        var score = _scoringService.CalculateContentTypeBoost(content);

        // Assert
        Assert.Equal(0f, score);
    }

    [Fact]
    public void CalculateContentTypeBoost_EmptyContent_ReturnsZero()
    {
        // Act
        var score = _scoringService.CalculateContentTypeBoost("");

        // Assert
        Assert.Equal(0f, score);
    }

    [Fact]
    public void CalculateContentTypeBoost_PositiveTakesPrecedenceOverNegative()
    {
        // Arrange - Contains both positive and negative indicators
        var content = "CONFIRMED: The item is a fruit, not a vegetable";

        // Act
        var score = _scoringService.CalculateContentTypeBoost(content);

        // Assert
        Assert.Equal(0.3f, score);  // Positive takes precedence
    }

    #endregion

    #region CalculateHybridScore Tests

    [Fact]
    public void CalculateHybridScore_CombinesAllFactors()
    {
        // Arrange
        var memory = CreateMemory("CONFIRMED: The secret is a red apple fruit.");
        var query = "apple fruit";

        // Act
        var hybridScore = _scoringService.CalculateHybridScore(memory, query, null);
        var baseScore = _scoringService.CalculateScore(memory, null);

        // Assert
        Assert.True(hybridScore > baseScore,
            $"Hybrid score ({hybridScore}) should be higher than base score ({baseScore})");
    }

    [Fact]
    public void CalculateHybridScore_ConfirmedContent_ScoresHigherThanRuledOut()
    {
        // Arrange
        var confirmedMemory = CreateMemory("CONFIRMED: The secret HAS the property of being edible.");
        var ruledOutMemory = CreateMemory("RULED OUT: The secret does NOT have the property of being alive.");
        var query = "Is it edible?";

        // Act
        var confirmedScore = _scoringService.CalculateHybridScore(confirmedMemory, query, null);
        var ruledOutScore = _scoringService.CalculateHybridScore(ruledOutMemory, query, null);

        // Assert
        Assert.True(confirmedScore > ruledOutScore,
            $"Confirmed score ({confirmedScore}) should be higher than ruled out ({ruledOutScore})");
    }

    [Fact]
    public void CalculateHybridScore_KeywordMatchBoosted()
    {
        // Arrange
        var matchingMemory = CreateMemory("The apple is red and juicy.");
        var nonMatchingMemory = CreateMemory("The vehicle has four wheels.");
        var query = "red apple";

        // Act
        var matchingScore = _scoringService.CalculateHybridScore(matchingMemory, query, null);
        var nonMatchingScore = _scoringService.CalculateHybridScore(nonMatchingMemory, query, null);

        // Assert
        Assert.True(matchingScore > nonMatchingScore,
            $"Matching score ({matchingScore}) should be higher than non-matching ({nonMatchingScore})");
    }

    [Fact]
    public void CalculateHybridScore_WithEmbedding_IncludesSemanticSimilarity()
    {
        // Arrange
        var memory = CreateMemoryWithEmbedding("Test content", new float[] { 1, 0, 0, 0 });
        var queryEmbedding = new float[] { 1, 0, 0, 0 };  // Identical
        var query = "test";

        // Act
        var scoreWithEmbedding = _scoringService.CalculateHybridScore(memory, query, queryEmbedding);
        var scoreWithoutEmbedding = _scoringService.CalculateHybridScore(memory, query, null);

        // Assert
        Assert.True(scoreWithEmbedding > scoreWithoutEmbedding,
            $"Score with embedding ({scoreWithEmbedding}) should be higher than without ({scoreWithoutEmbedding})");
    }

    #endregion

    #region Integration Tests for Memory Recall Scenario

    [Fact]
    public void HybridScoring_TwentyQuestionsScenario_ConfirmedRanksHigher()
    {
        // Arrange - Simulate a 20 Questions game scenario
        var memories = new[]
        {
            CreateMemory("CONFIRMED: The secret HAS the property asked in Q4: Is it natural?"),
            CreateMemory("CONFIRMED: The secret HAS the property asked in Q9: Can you hold it?"),
            CreateMemory("CONFIRMED: The secret HAS the property asked in Q14: Is it organic?"),
            CreateMemory("RULED OUT: The secret does NOT have the property asked in Q1: Is it alive?"),
            CreateMemory("RULED OUT: The secret does NOT have the property asked in Q5: Is it a rock?"),
            CreateMemory("RULED OUT: The secret does NOT have the property asked in Q8: Is it a gas?"),
        };

        var query = "What do I know about the secret?";

        // Act
        var scores = memories
            .Select(m => new { Memory = m, Score = _scoringService.CalculateHybridScore(m, query, null) })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Assert - CONFIRMED memories should rank in top 3
        var top3 = scores.Take(3).ToList();
        Assert.All(top3, item => Assert.Contains("CONFIRMED", item.Memory.Content));
    }

    [Fact]
    public void HybridScoring_KeywordQueryScenario_RelevantContentRanksHigher()
    {
        // Arrange
        var memories = new[]
        {
            CreateMemory("The item is edible and tastes sweet."),
            CreateMemory("The color is bright red."),
            CreateMemory("It grows on trees in orchards."),
            CreateMemory("The weather was nice yesterday."),
            CreateMemory("The meeting is scheduled for Tuesday."),
        };

        var query = "Is it edible?";

        // Act
        var scores = memories
            .Select(m => new { Memory = m, Score = _scoringService.CalculateHybridScore(m, query, null) })
            .OrderByDescending(x => x.Score)
            .ToList();

        // Assert - "edible" memory should rank first
        Assert.Contains("edible", scores.First().Memory.Content);
    }

    #endregion

    #region Helper Methods

    private static MemoryUnit CreateMemory(string content)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = "test-user",
            Content = content,
            Type = MemoryType.Episodic,
            ImportanceScore = 0.5f,
            CreatedAt = DateTime.UtcNow,
            AccessCount = 1
        };
    }

    private static MemoryUnit CreateMemoryWithEmbedding(string content, float[] embedding)
    {
        var memory = CreateMemory(content);
        memory.Embedding = embedding;
        return memory;
    }

    #endregion
}
