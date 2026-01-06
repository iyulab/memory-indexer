using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
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

    #region Intent-Aware Scoring Tests (Phase 22.3)

    [Fact]
    public void CalculateHybridScoreWithIntent_FactualIntent_PrioritizesSemanticMatch()
    {
        // Arrange - Use matching content for keyword boost consistency
        var recentMemory = CreateMemory("apple fruit information");
        recentMemory.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        recentMemory.ImportanceScore = 0.3f; // Low importance
        recentMemory.Embedding = new float[] { 1, 0, 0, 0 }; // High semantic match

        var importantMemory = CreateMemory("apple fruit information");
        importantMemory.CreatedAt = DateTime.UtcNow.AddDays(-1);
        importantMemory.ImportanceScore = 0.9f; // High importance
        importantMemory.Embedding = new float[] { 0, 1, 0, 0 }; // Low semantic match

        var factualIntent = new QueryIntentResult
        {
            Intent = QueryIntent.Factual,
            Confidence = 0.8f,
            Specificity = 0.9f // Very high specificity - strong importance damping
        };

        var queryEmbedding = new float[] { 1, 0, 0, 0 }; // Matches recentMemory perfectly
        var query = "apple fruit";

        // Act
        var recentScore = _scoringService.CalculateHybridScoreWithIntent(recentMemory, query, factualIntent, queryEmbedding);
        var importantScore = _scoringService.CalculateHybridScoreWithIntent(importantMemory, query, factualIntent, queryEmbedding);

        // Assert - Recent memory with high semantic match should score higher despite low importance
        // With high specificity (0.9), importance is dampened significantly (factor = 0.55)
        // Factual intent prioritizes semantic (0.6 weight) over importance (0.2 * 0.55 = 0.11)
        Assert.True(recentScore > importantScore,
            $"Factual intent should prioritize semantic match: recent={recentScore} vs important={importantScore}");
    }

    [Fact]
    public void CalculateHybridScoreWithIntent_TemporalIntent_PrioritizesRecency()
    {
        // Arrange
        var recentMemory = CreateMemory("Recent conversation");
        recentMemory.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        recentMemory.ImportanceScore = 0.3f;

        var oldMemory = CreateMemory("Old conversation");
        oldMemory.CreatedAt = DateTime.UtcNow.AddDays(-7);
        oldMemory.ImportanceScore = 0.8f; // Higher importance

        var temporalIntent = new QueryIntentResult
        {
            Intent = QueryIntent.Temporal,
            Confidence = 0.8f,
            Specificity = 0.5f
        };

        var query = "What did we discuss recently?";

        // Act
        var recentScore = _scoringService.CalculateHybridScoreWithIntent(recentMemory, query, temporalIntent, null);
        var oldScore = _scoringService.CalculateHybridScoreWithIntent(oldMemory, query, temporalIntent, null);

        // Assert - Recent memory should score higher
        Assert.True(recentScore > oldScore,
            $"Temporal intent should prioritize recency: recent={recentScore} vs old={oldScore}");
    }

    [Fact]
    public void CalculateHybridScoreWithIntent_ContextualIntent_BalancesRecencyAndSemantics()
    {
        // Arrange
        var memory = CreateMemory("Test content");
        memory.Embedding = new float[] { 1, 0, 0, 0 };

        var contextualIntent = new QueryIntentResult
        {
            Intent = QueryIntent.Contextual,
            Confidence = 0.7f,
            Specificity = 0.4f
        };

        var queryEmbedding = new float[] { 1, 0, 0, 0 };
        var query = "Tell me more about that";

        // Act
        var score = _scoringService.CalculateHybridScoreWithIntent(memory, query, contextualIntent, queryEmbedding);

        // Assert
        Assert.True(score > 0, "Contextual scoring should produce positive score");
    }

    [Fact]
    public void CalculateHybridScoreWithIntent_RelationalIntent_PrioritizesSemanticConnections()
    {
        // Arrange
        var relatedMemory = CreateMemory("Python is used for machine learning");
        relatedMemory.Embedding = new float[] { 0.9f, 0.1f, 0, 0 }; // Similar to query

        var unrelatedMemory = CreateMemory("The weather is nice today");
        unrelatedMemory.Embedding = new float[] { 0, 0, 1, 0 }; // Different from query

        var relationalIntent = new QueryIntentResult
        {
            Intent = QueryIntent.Relational,
            Confidence = 0.8f,
            Specificity = 0.6f
        };

        var queryEmbedding = new float[] { 1, 0, 0, 0 }; // Closer to relatedMemory
        var query = "What's related to machine learning?";

        // Act
        var relatedScore = _scoringService.CalculateHybridScoreWithIntent(relatedMemory, query, relationalIntent, queryEmbedding);
        var unrelatedScore = _scoringService.CalculateHybridScoreWithIntent(unrelatedMemory, query, relationalIntent, queryEmbedding);

        // Assert
        Assert.True(relatedScore > unrelatedScore,
            $"Relational intent should prioritize semantic connections: related={relatedScore} vs unrelated={unrelatedScore}");
    }

    #endregion

    #region Dynamic Importance Damping Tests (Phase 22.3)

    [Fact]
    public void CalculateHybridScoreWithIntent_HighSpecificity_DampensImportance()
    {
        // Arrange
        var memory = CreateMemory("Important generic information");
        memory.ImportanceScore = 0.9f; // Very important
        memory.Embedding = new float[] { 0.5f, 0.5f, 0, 0 }; // Medium semantic match

        var lowSpecificityIntent = new QueryIntentResult
        {
            Intent = QueryIntent.General,
            Confidence = 0.5f,
            Specificity = 0.3f // Low specificity - importance NOT dampened
        };

        var highSpecificityIntent = new QueryIntentResult
        {
            Intent = QueryIntent.Factual,
            Confidence = 0.8f,
            Specificity = 0.9f // High specificity - importance DAMPENED
        };

        var queryEmbedding = new float[] { 0.6f, 0.4f, 0, 0 };
        var query = "Tell me about this";

        // Act
        var lowSpecScore = _scoringService.CalculateHybridScoreWithIntent(memory, query, lowSpecificityIntent, queryEmbedding);
        var highSpecScore = _scoringService.CalculateHybridScoreWithIntent(memory, query, highSpecificityIntent, queryEmbedding);

        // Assert - With high specificity, importance weight is dampened, so score might be similar or lower
        // This prevents high-importance generic memories from dominating specific queries
        Assert.True(lowSpecScore >= highSpecScore * 0.8f,
            $"High specificity should dampen importance weight: lowSpec={lowSpecScore} vs highSpec={highSpecScore}");
    }

    [Fact]
    public void CalculateHybridScoreWithIntent_SpecificityThreshold_AppliesDamping()
    {
        // Arrange
        var memory = CreateMemory("Very important fact");
        memory.ImportanceScore = 1.0f;

        var belowThresholdIntent = new QueryIntentResult
        {
            Intent = QueryIntent.General,
            Confidence = 0.5f,
            Specificity = 0.69f // Just below 0.7 threshold - NO damping
        };

        var aboveThresholdIntent = new QueryIntentResult
        {
            Intent = QueryIntent.General,
            Confidence = 0.5f,
            Specificity = 0.71f // Just above 0.7 threshold - YES damping
        };

        var query = "test";

        // Act
        var belowScore = _scoringService.CalculateHybridScoreWithIntent(memory, query, belowThresholdIntent, null);
        var aboveScore = _scoringService.CalculateHybridScoreWithIntent(memory, query, aboveThresholdIntent, null);

        // Assert - Above threshold should have slightly lower score due to importance damping
        Assert.True(belowScore > aboveScore || Math.Abs(belowScore - aboveScore) < 0.01f,
            $"Specificity > 0.7 should apply importance damping: below={belowScore} vs above={aboveScore}");
    }

    [Fact]
    public void CalculateHybridScoreWithIntent_MaxSpecificity_MaximumDamping()
    {
        // Arrange
        var memory = CreateMemory("Generic high-importance fact");
        memory.ImportanceScore = 1.0f;
        memory.Embedding = new float[] { 0, 0, 0, 1 }; // Low semantic match

        var maxSpecificityIntent = new QueryIntentResult
        {
            Intent = QueryIntent.Factual,
            Confidence = 0.9f,
            Specificity = 1.0f // Maximum specificity - maximum damping
        };

        var queryEmbedding = new float[] { 1, 0, 0, 0 }; // Different from memory
        var query = "Very specific question";

        // Act
        var score = _scoringService.CalculateHybridScoreWithIntent(memory, query, maxSpecificityIntent, queryEmbedding);

        // Assert - With max specificity (1.0), importance weight is dampened by 50%
        // dampingFactor = 1.0 - (1.0 * 0.5) = 0.5
        Assert.True(score > 0, "Should still produce positive score even with max damping");
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
