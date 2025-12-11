using FluentAssertions;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Intelligence.Evaluation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Intelligence.Tests.Evaluation;

/// <summary>
/// Tests for LoCoMo (Long-term Conversation Memory) benchmark evaluator.
/// </summary>
[Trait("Category", "Evaluation")]
public class LoCoMoEvaluatorTests
{
    private readonly Mock<IEmbeddingService> _mockEmbeddingService;
    private readonly LoCoMoEvaluator _evaluator;

    public LoCoMoEvaluatorTests()
    {
        _mockEmbeddingService = new Mock<IEmbeddingService>();
        _evaluator = new LoCoMoEvaluator(
            _mockEmbeddingService.Object,
            NullLogger<LoCoMoEvaluator>.Instance);

        // Setup default embedding generation
        _mockEmbeddingService
            .Setup(x => x.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => CreateMockEmbedding(text));
    }

    private static Memory<float> CreateMockEmbedding(string text)
    {
        var random = new Random(text.GetHashCode());
        var embedding = new float[384];
        for (var i = 0; i < embedding.Length; i++)
        {
            embedding[i] = (float)(random.NextDouble() * 2 - 1);
        }
        return embedding;
    }

    #region Synthetic Test Suite Generation

    [Fact]
    public void GenerateSyntheticTestSuite_ShouldCreateValidTestSuite()
    {
        // Arrange
        const int conversationTurns = 50;
        const int queriesPerType = 5;

        // Act
        var testSuite = _evaluator.GenerateSyntheticTestSuite(conversationTurns, queriesPerType);

        // Assert
        testSuite.Should().NotBeNull();
        testSuite.Id.Should().StartWith("synthetic-");
        testSuite.Name.Should().Be("Synthetic LoCoMo Test Suite");
        testSuite.ConversationMemories.Should().HaveCount(conversationTurns);
        testSuite.TestQueries.Should().NotBeEmpty();
    }

    [Fact]
    public void GenerateSyntheticTestSuite_ShouldIncludeAllQueryTypes()
    {
        // Arrange & Act
        var testSuite = _evaluator.GenerateSyntheticTestSuite(50, 5);

        // Assert
        var queryTypes = testSuite.TestQueries.Select(q => q.QueryType).Distinct().ToList();

        queryTypes.Should().Contain(LoCoMoQueryType.SingleHop);
        queryTypes.Should().Contain(LoCoMoQueryType.MultiHop);
        queryTypes.Should().Contain(LoCoMoQueryType.Temporal);
        queryTypes.Should().Contain(LoCoMoQueryType.CrossSession);
        queryTypes.Should().Contain(LoCoMoQueryType.Factual);
    }

    [Fact]
    public void GenerateSyntheticTestSuite_ShouldGenerateUniqueIds()
    {
        // Arrange & Act
        var testSuite = _evaluator.GenerateSyntheticTestSuite(50, 5);

        // Assert
        var memoryIds = testSuite.ConversationMemories.Select(m => m.Id).ToList();
        memoryIds.Should().OnlyHaveUniqueItems();

        var queryIds = testSuite.TestQueries.Select(q => q.Id).ToList();
        queryIds.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GenerateSyntheticTestSuite_ShouldHaveTemporalFiltersForTemporalQueries()
    {
        // Arrange & Act
        var testSuite = _evaluator.GenerateSyntheticTestSuite(50, 5);

        // Assert
        var temporalQueries = testSuite.TestQueries
            .Where(q => q.QueryType == LoCoMoQueryType.Temporal)
            .ToList();

        temporalQueries.Should().NotBeEmpty();
        temporalQueries.Should().AllSatisfy(q =>
            q.TemporalFilter.Should().NotBeNull("temporal queries should have temporal filters"));
    }

    [Fact]
    public void GenerateSyntheticTestSuite_ShouldGenerateMultipleSessionsForCrossSessionQueries()
    {
        // Arrange & Act
        var testSuite = _evaluator.GenerateSyntheticTestSuite(50, 5);

        // Assert
        var sessions = testSuite.ConversationMemories
            .Select(m => m.SessionId)
            .Distinct()
            .ToList();

        sessions.Should().HaveCountGreaterThan(1, "should have multiple sessions for cross-session testing");
    }

    #endregion

    #region Query Evaluation

    [Fact]
    public async Task EvaluateQueryAsync_WithRelevantResults_ShouldReturnSuccess()
    {
        // Arrange
        var mockMemoryStore = new Mock<IMemoryStore>();
        var memoryId = Guid.NewGuid();

        var testQuery = new LoCoMoTestQuery
        {
            Id = "test-1",
            Query = "What did we discuss about Python?",
            QueryType = LoCoMoQueryType.SingleHop,
            RelevantMemoryIds = [memoryId.ToString()],
            ExpectedAnswer = "We discussed Python programming",
            TopK = 5
        };

        var searchResults = new List<MemorySearchResult>
        {
            new()
            {
                Memory = new MemoryUnit
                {
                    Id = memoryId,
                    UserId = "test-user",
                    Content = "We discussed Python programming",
                    Type = MemoryType.Episodic
                },
                Score = 0.95f
            }
        };

        mockMemoryStore
            .Setup(x => x.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<MemorySearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var result = await _evaluator.EvaluateQueryAsync(mockMemoryStore.Object, testQuery, "test-user");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Recall.Should().Be(1.0f);
        result.Precision.Should().Be(1.0f);
        result.MeanReciprocalRank.Should().Be(1.0f);
    }

    [Fact]
    public async Task EvaluateQueryAsync_WithNoRelevantResults_ShouldReturnFailure()
    {
        // Arrange
        var mockMemoryStore = new Mock<IMemoryStore>();
        var relevantId = Guid.NewGuid();
        var irrelevantId = Guid.NewGuid();

        var testQuery = new LoCoMoTestQuery
        {
            Id = "test-2",
            Query = "What did we discuss about Python?",
            QueryType = LoCoMoQueryType.SingleHop,
            RelevantMemoryIds = [relevantId.ToString()],
            TopK = 5
        };

        var searchResults = new List<MemorySearchResult>
        {
            new()
            {
                Memory = new MemoryUnit
                {
                    Id = irrelevantId,
                    UserId = "test-user",
                    Content = "Some other content",
                    Type = MemoryType.Episodic
                },
                Score = 0.9f
            }
        };

        mockMemoryStore
            .Setup(x => x.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<MemorySearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var result = await _evaluator.EvaluateQueryAsync(mockMemoryStore.Object, testQuery, "test-user");

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Recall.Should().Be(0f);
        result.Precision.Should().Be(0f);
    }

    [Fact]
    public async Task EvaluateQueryAsync_ShouldCalculateCorrectMRR()
    {
        // Arrange
        var mockMemoryStore = new Mock<IMemoryStore>();
        var relevantId = Guid.NewGuid();
        var irrelevantId1 = Guid.NewGuid();
        var irrelevantId2 = Guid.NewGuid();

        var testQuery = new LoCoMoTestQuery
        {
            Id = "test-mrr",
            Query = "Test query",
            QueryType = LoCoMoQueryType.SingleHop,
            RelevantMemoryIds = [relevantId.ToString()],
            TopK = 10
        };

        // Relevant result at position 3 (0-indexed: 2)
        var searchResults = new List<MemorySearchResult>
        {
            CreateSearchResult(irrelevantId1, 0.95f),
            CreateSearchResult(irrelevantId2, 0.90f),
            CreateSearchResult(relevantId, 0.85f)
        };

        mockMemoryStore
            .Setup(x => x.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<MemorySearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var result = await _evaluator.EvaluateQueryAsync(mockMemoryStore.Object, testQuery, "test-user");

        // Assert
        result.MeanReciprocalRank.Should().BeApproximately(1f / 3f, 0.01f, "MRR should be 1/3 for relevant at position 3");
    }

    [Fact]
    public async Task EvaluateQueryAsync_ShouldApplyTemporalFilter()
    {
        // Arrange
        var mockMemoryStore = new Mock<IMemoryStore>();
        var afterDate = DateTime.UtcNow.AddDays(-7);
        var beforeDate = DateTime.UtcNow;

        var testQuery = new LoCoMoTestQuery
        {
            Id = "test-temporal",
            Query = "Recent discussions",
            QueryType = LoCoMoQueryType.Temporal,
            RelevantMemoryIds = [],
            TopK = 10,
            TemporalFilter = new TemporalFilter
            {
                After = afterDate,
                Before = beforeDate
            }
        };

        mockMemoryStore
            .Setup(x => x.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<MemorySearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>());

        // Act
        await _evaluator.EvaluateQueryAsync(mockMemoryStore.Object, testQuery, "test-user");

        // Assert
        mockMemoryStore.Verify(x => x.SearchAsync(
            It.IsAny<ReadOnlyMemory<float>>(),
            It.Is<MemorySearchOptions>(opt =>
                opt.CreatedAfter == afterDate &&
                opt.CreatedBefore == beforeDate),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Full Evaluation

    [Fact]
    public async Task EvaluateAsync_ShouldReturnAggregateMetrics()
    {
        // Arrange
        var mockMemoryStore = new Mock<IMemoryStore>();
        var memoryId1 = Guid.NewGuid();
        var memoryId2 = Guid.NewGuid();

        var testSuite = new LoCoMoTestSuite
        {
            Id = "test-suite-1",
            Name = "Test Suite",
            ConversationMemories = [],
            TestQueries =
            [
                new LoCoMoTestQuery
                {
                    Id = "q1",
                    Query = "Query 1",
                    QueryType = LoCoMoQueryType.SingleHop,
                    RelevantMemoryIds = [memoryId1.ToString()],
                    TopK = 5
                },
                new LoCoMoTestQuery
                {
                    Id = "q2",
                    Query = "Query 2",
                    QueryType = LoCoMoQueryType.SingleHop,
                    RelevantMemoryIds = [memoryId2.ToString()],
                    TopK = 5
                }
            ]
        };

        // Setup: First query succeeds, second fails
        var callCount = 0;
        mockMemoryStore
            .Setup(x => x.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<MemorySearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new List<MemorySearchResult> { CreateSearchResult(memoryId1, 0.9f) }
                    : new List<MemorySearchResult> { CreateSearchResult(Guid.NewGuid(), 0.8f) };
            });

        // Act
        var result = await _evaluator.EvaluateAsync(mockMemoryStore.Object, testSuite, "test-user");

        // Assert
        result.Should().NotBeNull();
        result.TestSuiteId.Should().Be("test-suite-1");
        result.TotalQueries.Should().Be(2);
        result.SuccessfulQueries.Should().Be(1);
        result.FailedQueries.Should().Be(1);
        result.Metrics.SuccessRate.Should().Be(0.5f);
        result.QueryResults.Should().HaveCount(2);
        result.ByQueryType.Should().ContainKey(LoCoMoQueryType.SingleHop);
    }

    [Fact]
    public async Task EvaluateAsync_ShouldGroupMetricsByQueryType()
    {
        // Arrange
        var mockMemoryStore = new Mock<IMemoryStore>();

        var testSuite = new LoCoMoTestSuite
        {
            Id = "test-suite-2",
            Name = "Multi-Type Test Suite",
            ConversationMemories = [],
            TestQueries =
            [
                new LoCoMoTestQuery
                {
                    Id = "sh1",
                    Query = "Single hop query",
                    QueryType = LoCoMoQueryType.SingleHop,
                    RelevantMemoryIds = [Guid.NewGuid().ToString()],
                    TopK = 5
                },
                new LoCoMoTestQuery
                {
                    Id = "mh1",
                    Query = "Multi hop query",
                    QueryType = LoCoMoQueryType.MultiHop,
                    RelevantMemoryIds = [Guid.NewGuid().ToString()],
                    TopK = 5
                },
                new LoCoMoTestQuery
                {
                    Id = "tmp1",
                    Query = "Temporal query",
                    QueryType = LoCoMoQueryType.Temporal,
                    RelevantMemoryIds = [Guid.NewGuid().ToString()],
                    TopK = 5,
                    TemporalFilter = new TemporalFilter { After = DateTime.UtcNow.AddDays(-1) }
                }
            ]
        };

        mockMemoryStore
            .Setup(x => x.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<MemorySearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MemorySearchResult>());

        // Act
        var result = await _evaluator.EvaluateAsync(mockMemoryStore.Object, testSuite, "test-user");

        // Assert
        result.ByQueryType.Should().ContainKey(LoCoMoQueryType.SingleHop);
        result.ByQueryType.Should().ContainKey(LoCoMoQueryType.MultiHop);
        result.ByQueryType.Should().ContainKey(LoCoMoQueryType.Temporal);
    }

    #endregion

    #region NDCG Calculation

    [Fact]
    public async Task EvaluateQueryAsync_ShouldCalculateNDCG_PerfectRanking()
    {
        // Arrange
        var mockMemoryStore = new Mock<IMemoryStore>();
        var relevantId1 = Guid.NewGuid();
        var relevantId2 = Guid.NewGuid();

        var testQuery = new LoCoMoTestQuery
        {
            Id = "ndcg-test",
            Query = "Test query",
            QueryType = LoCoMoQueryType.MultiHop,
            RelevantMemoryIds = [relevantId1.ToString(), relevantId2.ToString()],
            TopK = 10
        };

        // Perfect ranking: all relevant items at top
        var searchResults = new List<MemorySearchResult>
        {
            CreateSearchResult(relevantId1, 0.95f),
            CreateSearchResult(relevantId2, 0.90f),
            CreateSearchResult(Guid.NewGuid(), 0.85f)
        };

        mockMemoryStore
            .Setup(x => x.SearchAsync(It.IsAny<ReadOnlyMemory<float>>(), It.IsAny<MemorySearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var result = await _evaluator.EvaluateQueryAsync(mockMemoryStore.Object, testQuery, "test-user");

        // Assert
        result.NDCG.Should().Be(1.0f, "NDCG should be 1.0 for perfect ranking");
    }

    #endregion

    #region Helper Methods

    private static MemorySearchResult CreateSearchResult(Guid id, float score)
    {
        return new MemorySearchResult
        {
            Memory = new MemoryUnit
            {
                Id = id,
                UserId = "test-user",
                Content = $"Content for {id}",
                Type = MemoryType.Episodic
            },
            Score = score
        };
    }

    #endregion
}
