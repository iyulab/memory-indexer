using System.Diagnostics;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Evaluation;

/// <summary>
/// Evaluator for LoCoMo (Long-term Conversation Memory) benchmark.
/// Tests memory retrieval across long conversation contexts with temporal,
/// multi-hop reasoning, and cross-session queries.
/// </summary>
/// <remarks>
/// LoCoMo evaluation tests:
/// 1. Single-Hop: Direct memory recall
/// 2. Multi-Hop: Information aggregation across multiple memories
/// 3. Temporal: Time-aware retrieval ("what did we discuss last week?")
/// 4. Cross-Session: Retrieval across conversation sessions
/// 5. Factual: Specific fact retrieval
/// </remarks>
public sealed class LoCoMoEvaluator : ILoCoMoEvaluator
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<LoCoMoEvaluator> _logger;

    public LoCoMoEvaluator(
        IEmbeddingService embeddingService,
        ILogger<LoCoMoEvaluator> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LoCoMoEvaluationResult> EvaluateAsync(
        IMemoryStore memoryStore,
        LoCoMoTestSuite testSuite,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var queryResults = new List<LoCoMoQueryResult>();

        _logger.LogInformation("Starting LoCoMo evaluation with {Count} test queries", testSuite.TestQueries.Count);

        // First, store the conversation memories if not already present
        if (testSuite.ConversationMemories.Count > 0)
        {
            await SeedConversationMemoriesAsync(memoryStore, testSuite.ConversationMemories, userId, cancellationToken);
        }

        // Evaluate each test query
        foreach (var testQuery in testSuite.TestQueries)
        {
            try
            {
                var result = await EvaluateQueryAsync(
                    memoryStore,
                    testQuery,
                    userId,
                    cancellationToken);
                queryResults.Add(result);

                _logger.LogDebug(
                    "Query '{QueryId}' ({Type}): Success={Success}, Recall={Recall:F2}, MRR={MRR:F2}",
                    testQuery.Id,
                    testQuery.QueryType,
                    result.Success,
                    result.Recall,
                    result.MeanReciprocalRank);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate query {QueryId}", testQuery.Id);
                queryResults.Add(new LoCoMoQueryResult
                {
                    QueryId = testQuery.Id,
                    QueryType = testQuery.QueryType,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        stopwatch.Stop();

        // Calculate aggregate metrics
        var successfulResults = queryResults.Where(r => r.Success).ToList();
        var metrics = CalculateAggregateMetrics(queryResults);

        return new LoCoMoEvaluationResult
        {
            TestSuiteId = testSuite.Id,
            TestSuiteName = testSuite.Name,
            TotalQueries = testSuite.TestQueries.Count,
            SuccessfulQueries = successfulResults.Count,
            FailedQueries = queryResults.Count - successfulResults.Count,
            Metrics = metrics,
            QueryResults = queryResults,
            ByQueryType = CalculateMetricsByType(queryResults),
            Duration = stopwatch.Elapsed
        };
    }

    /// <inheritdoc />
    public async Task<LoCoMoQueryResult> EvaluateQueryAsync(
        IMemoryStore memoryStore,
        LoCoMoTestQuery testQuery,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Generate embedding for query
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(testQuery.Query, cancellationToken);

        // Search for relevant memories
        var searchOptions = new MemorySearchOptions
        {
            UserId = userId,
            Limit = testQuery.TopK,
            MinScore = 0.1f,
            IncludeDeleted = false
        };

        // Add temporal filter if specified
        if (testQuery.TemporalFilter != null)
        {
            searchOptions.CreatedAfter = testQuery.TemporalFilter.After;
            searchOptions.CreatedBefore = testQuery.TemporalFilter.Before;
        }

        var searchResults = await memoryStore.SearchAsync(queryEmbedding, searchOptions, cancellationToken);
        stopwatch.Stop();

        // Calculate metrics
        var retrievedIds = searchResults.Select(r => r.Memory.Id.ToString()).ToHashSet();
        var relevantIds = testQuery.RelevantMemoryIds.ToHashSet();

        // Recall: What fraction of relevant memories were retrieved?
        var recall = relevantIds.Count > 0
            ? retrievedIds.Intersect(relevantIds).Count() / (float)relevantIds.Count
            : 0f;

        // Precision: What fraction of retrieved memories are relevant?
        var precision = retrievedIds.Count > 0
            ? retrievedIds.Intersect(relevantIds).Count() / (float)retrievedIds.Count
            : 0f;

        // F1 Score
        var f1Score = (precision + recall) > 0
            ? 2 * precision * recall / (precision + recall)
            : 0f;

        // Mean Reciprocal Rank: Rank of the first relevant result
        var mrr = CalculateMRR(searchResults, relevantIds);

        // Normalized Discounted Cumulative Gain
        var ndcg = CalculateNDCG(searchResults, relevantIds, testQuery.TopK);

        // Success: At least one relevant memory in top-k
        var success = retrievedIds.Intersect(relevantIds).Any();

        // Answer coverage: Can the query be answered from retrieved contexts?
        var answerCoverage = await CalculateAnswerCoverageAsync(
            testQuery.ExpectedAnswer,
            searchResults.Select(r => r.Memory.Content).ToList(),
            cancellationToken);

        return new LoCoMoQueryResult
        {
            QueryId = testQuery.Id,
            QueryType = testQuery.QueryType,
            Query = testQuery.Query,
            Success = success,
            Recall = recall,
            Precision = precision,
            F1Score = f1Score,
            MeanReciprocalRank = mrr,
            NDCG = ndcg,
            AnswerCoverage = answerCoverage,
            RetrievedCount = searchResults.Count,
            RelevantRetrieved = retrievedIds.Intersect(relevantIds).Count(),
            Latency = stopwatch.Elapsed,
            RetrievedMemoryIds = retrievedIds.ToList(),
            TopScores = searchResults.Take(5).Select(r => r.Score).ToList()
        };
    }

    /// <inheritdoc />
    public LoCoMoTestSuite GenerateSyntheticTestSuite(int conversationTurns = 50, int queriesPerType = 5)
    {
        var random = new Random(42); // Fixed seed for reproducibility
        var memories = new List<LoCoMoConversationMemory>();
        var queries = new List<LoCoMoTestQuery>();

        // Generate conversation memories
        var topics = new[]
        {
            ("programming", new[] { "Python", "JavaScript", "C#", "async/await", "databases", "APIs" }),
            ("travel", new[] { "Paris", "Tokyo", "New York", "beaches", "mountains", "hotels" }),
            ("food", new[] { "Italian", "Japanese", "Mexican", "cooking", "restaurants", "recipes" }),
            ("fitness", new[] { "running", "gym", "yoga", "diet", "sleep", "hydration" }),
            ("work", new[] { "meetings", "deadlines", "projects", "team", "productivity", "tools" })
        };

        var baseDate = DateTime.UtcNow.AddDays(-30);

        for (var i = 0; i < conversationTurns; i++)
        {
            var (topic, subtopics) = topics[i % topics.Length];
            var subtopic = subtopics[random.Next(subtopics.Length)];
            var sessionId = $"session-{i / 10}"; // 10 turns per session
            var memoryDate = baseDate.AddHours(i * 6); // 6 hours between messages

            memories.Add(new LoCoMoConversationMemory
            {
                Id = Guid.NewGuid().ToString(),
                Content = GenerateConversationContent(topic, subtopic, i, random),
                Topic = topic,
                SessionId = sessionId,
                Timestamp = memoryDate,
                TurnIndex = i
            });
        }

        // Generate test queries for each type
        queries.AddRange(GenerateSingleHopQueries(memories, queriesPerType, random));
        queries.AddRange(GenerateMultiHopQueries(memories, queriesPerType, random));
        queries.AddRange(GenerateTemporalQueries(memories, queriesPerType, random));
        queries.AddRange(GenerateCrossSessionQueries(memories, queriesPerType, random));
        queries.AddRange(GenerateFactualQueries(memories, queriesPerType, random));

        return new LoCoMoTestSuite
        {
            Id = $"synthetic-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            Name = "Synthetic LoCoMo Test Suite",
            Description = $"Auto-generated test suite with {conversationTurns} conversation turns and {queries.Count} queries",
            ConversationMemories = memories,
            TestQueries = queries
        };
    }

    private async Task SeedConversationMemoriesAsync(
        IMemoryStore memoryStore,
        IReadOnlyList<LoCoMoConversationMemory> memories,
        string userId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Seeding {Count} conversation memories for evaluation", memories.Count);

        var memoryUnits = new List<MemoryUnit>();

        foreach (var memory in memories)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(memory.Content, cancellationToken);

            memoryUnits.Add(new MemoryUnit
            {
                Id = Guid.TryParse(memory.Id, out var id) ? id : Guid.NewGuid(),
                UserId = userId,
                SessionId = memory.SessionId,
                Content = memory.Content,
                Type = MemoryType.Episodic,
                Topics = [memory.Topic],
                Embedding = embedding,
                CreatedAt = memory.Timestamp,
                UpdatedAt = memory.Timestamp
            });
        }

        await memoryStore.StoreBatchAsync(memoryUnits, cancellationToken);
        _logger.LogInformation("Seeded {Count} memories successfully", memoryUnits.Count);
    }

    private static float CalculateMRR(IReadOnlyList<MemorySearchResult> results, HashSet<string> relevantIds)
    {
        for (var i = 0; i < results.Count; i++)
        {
            if (relevantIds.Contains(results[i].Memory.Id.ToString()))
            {
                return 1f / (i + 1);
            }
        }
        return 0f;
    }

    private static float CalculateNDCG(IReadOnlyList<MemorySearchResult> results, HashSet<string> relevantIds, int k)
    {
        // DCG calculation
        var dcg = 0.0;
        for (var i = 0; i < Math.Min(results.Count, k); i++)
        {
            var relevant = relevantIds.Contains(results[i].Memory.Id.ToString()) ? 1 : 0;
            dcg += relevant / Math.Log2(i + 2); // +2 because i is 0-indexed
        }

        // IDCG: ideal DCG (all relevant items at top)
        var idcg = 0.0;
        for (var i = 0; i < Math.Min(relevantIds.Count, k); i++)
        {
            idcg += 1.0 / Math.Log2(i + 2);
        }

        return idcg > 0 ? (float)(dcg / idcg) : 0f;
    }

    private async Task<float> CalculateAnswerCoverageAsync(
        string? expectedAnswer,
        IReadOnlyList<string> retrievedContexts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(expectedAnswer) || retrievedContexts.Count == 0)
            return 0f;

        var answerEmbedding = await _embeddingService.GenerateEmbeddingAsync(expectedAnswer, cancellationToken);

        var maxSimilarity = 0f;
        foreach (var context in retrievedContexts)
        {
            var contextEmbedding = await _embeddingService.GenerateEmbeddingAsync(context, cancellationToken);
            var similarity = VectorMath.CosineSimilarity(answerEmbedding.Span, contextEmbedding.Span);
            maxSimilarity = Math.Max(maxSimilarity, similarity);
        }

        return maxSimilarity;
    }

    private static LoCoMoAggregateMetrics CalculateAggregateMetrics(IReadOnlyList<LoCoMoQueryResult> results)
    {
        var successful = results.Where(r => r.Success).ToList();
        if (successful.Count == 0)
        {
            return new LoCoMoAggregateMetrics();
        }

        return new LoCoMoAggregateMetrics
        {
            OverallRecall = successful.Average(r => r.Recall),
            OverallPrecision = successful.Average(r => r.Precision),
            OverallF1Score = successful.Average(r => r.F1Score),
            OverallMRR = successful.Average(r => r.MeanReciprocalRank),
            OverallNDCG = successful.Average(r => r.NDCG),
            OverallAnswerCoverage = successful.Average(r => r.AnswerCoverage),
            AverageLatency = TimeSpan.FromMilliseconds(successful.Average(r => r.Latency.TotalMilliseconds)),
            SuccessRate = successful.Count / (float)results.Count
        };
    }

    private static Dictionary<LoCoMoQueryType, LoCoMoAggregateMetrics> CalculateMetricsByType(
        IReadOnlyList<LoCoMoQueryResult> results)
    {
        return results
            .GroupBy(r => r.QueryType)
            .ToDictionary(
                g => g.Key,
                g => CalculateAggregateMetrics(g.ToList()));
    }

    private static string GenerateConversationContent(string topic, string subtopic, int turnIndex, Random random)
    {
        var templates = topic switch
        {
            "programming" => new[]
            {
                $"We discussed {subtopic} programming concepts today. The key points were about best practices and patterns.",
                $"I mentioned that {subtopic} is important for modern development. We covered several examples.",
                $"During our chat about {subtopic}, we explored various implementation strategies.",
                $"The conversation about {subtopic} revealed some interesting optimization techniques."
            },
            "travel" => new[]
            {
                $"I shared my experiences visiting {subtopic}. The trip was memorable.",
                $"We talked about planning a trip to {subtopic}. There are many things to see.",
                $"The discussion about {subtopic} covered accommodation options and activities.",
                $"I recommended visiting {subtopic} during off-season for better prices."
            },
            "food" => new[]
            {
                $"We discussed {subtopic} cuisine today. Some great restaurants were mentioned.",
                $"The conversation about {subtopic} food covered various recipes and techniques.",
                $"I shared a favorite {subtopic} recipe that's been in my family for years.",
                $"We explored the cultural aspects of {subtopic} cooking and traditions."
            },
            "fitness" => new[]
            {
                $"Today we talked about {subtopic} and its health benefits.",
                $"The discussion about {subtopic} included some practical tips and routines.",
                $"I mentioned that {subtopic} has been part of my daily routine for months.",
                $"We covered the importance of {subtopic} for overall wellbeing."
            },
            "work" => new[]
            {
                $"We discussed {subtopic} at work today. Some interesting challenges were mentioned.",
                $"The conversation about {subtopic} highlighted areas for improvement.",
                $"I shared strategies for managing {subtopic} more effectively.",
                $"We explored tools and techniques for better {subtopic} handling."
            },
            _ => new[] { $"We had a conversation about {topic} and {subtopic}." }
        };

        return templates[random.Next(templates.Length)] + $" [Turn {turnIndex}]";
    }

    private static List<LoCoMoTestQuery> GenerateSingleHopQueries(
        List<LoCoMoConversationMemory> memories,
        int count,
        Random random)
    {
        return memories
            .OrderBy(_ => random.Next())
            .Take(count)
            .Select(m => new LoCoMoTestQuery
            {
                Id = $"single-{Guid.NewGuid():N}",
                Query = $"What did we discuss about {m.Topic}?",
                QueryType = LoCoMoQueryType.SingleHop,
                RelevantMemoryIds = [m.Id],
                ExpectedAnswer = m.Content,
                TopK = 5
            })
            .ToList();
    }

    private static List<LoCoMoTestQuery> GenerateMultiHopQueries(
        List<LoCoMoConversationMemory> memories,
        int count,
        Random random)
    {
        var queries = new List<LoCoMoTestQuery>();
        var byTopic = memories.GroupBy(m => m.Topic).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var topic in byTopic.Keys.Take(count))
        {
            var topicMemories = byTopic[topic].OrderBy(_ => random.Next()).Take(3).ToList();
            if (topicMemories.Count >= 2)
            {
                queries.Add(new LoCoMoTestQuery
                {
                    Id = $"multi-{Guid.NewGuid():N}",
                    Query = $"Summarize all our conversations about {topic}.",
                    QueryType = LoCoMoQueryType.MultiHop,
                    RelevantMemoryIds = topicMemories.Select(m => m.Id).ToList(),
                    ExpectedAnswer = string.Join(" ", topicMemories.Select(m => m.Content)),
                    TopK = 10
                });
            }
        }

        return queries;
    }

    private static List<LoCoMoTestQuery> GenerateTemporalQueries(
        List<LoCoMoConversationMemory> memories,
        int count,
        Random random)
    {
        var queries = new List<LoCoMoTestQuery>();
        var sortedMemories = memories.OrderBy(m => m.Timestamp).ToList();

        // Recent memories query
        var recentMemories = sortedMemories.TakeLast(5).ToList();
        queries.Add(new LoCoMoTestQuery
        {
            Id = $"temporal-recent-{Guid.NewGuid():N}",
            Query = "What did we discuss recently?",
            QueryType = LoCoMoQueryType.Temporal,
            RelevantMemoryIds = recentMemories.Select(m => m.Id).ToList(),
            ExpectedAnswer = string.Join(" ", recentMemories.Select(m => m.Content)),
            TopK = 10,
            TemporalFilter = new TemporalFilter
            {
                After = recentMemories.First().Timestamp.AddHours(-1)
            }
        });

        // Older memories query
        var olderMemories = sortedMemories.Take(5).ToList();
        queries.Add(new LoCoMoTestQuery
        {
            Id = $"temporal-old-{Guid.NewGuid():N}",
            Query = "What were our earliest conversations about?",
            QueryType = LoCoMoQueryType.Temporal,
            RelevantMemoryIds = olderMemories.Select(m => m.Id).ToList(),
            ExpectedAnswer = string.Join(" ", olderMemories.Select(m => m.Content)),
            TopK = 10,
            TemporalFilter = new TemporalFilter
            {
                Before = olderMemories.Last().Timestamp.AddHours(1)
            }
        });

        // Add more random temporal queries
        for (var i = 0; i < Math.Max(0, count - 2); i++)
        {
            var midPoint = sortedMemories.Count / 2 + random.Next(-5, 5);
            var rangeMemories = sortedMemories.Skip(midPoint).Take(5).ToList();
            if (rangeMemories.Count > 0)
            {
                queries.Add(new LoCoMoTestQuery
                {
                    Id = $"temporal-{i}-{Guid.NewGuid():N}",
                    Query = $"What did we discuss around {rangeMemories.First().Timestamp:yyyy-MM-dd}?",
                    QueryType = LoCoMoQueryType.Temporal,
                    RelevantMemoryIds = rangeMemories.Select(m => m.Id).ToList(),
                    ExpectedAnswer = string.Join(" ", rangeMemories.Select(m => m.Content)),
                    TopK = 10,
                    TemporalFilter = new TemporalFilter
                    {
                        After = rangeMemories.First().Timestamp.AddDays(-1),
                        Before = rangeMemories.Last().Timestamp.AddDays(1)
                    }
                });
            }
        }

        return queries;
    }

    private static List<LoCoMoTestQuery> GenerateCrossSessionQueries(
        List<LoCoMoConversationMemory> memories,
        int count,
        Random random)
    {
        var queries = new List<LoCoMoTestQuery>();
        var bySession = memories.GroupBy(m => m.SessionId).ToDictionary(g => g.Key, g => g.ToList());

        // Cross-session topic continuity
        var topics = memories.Select(m => m.Topic).Distinct().ToList();
        foreach (var topic in topics.Take(count))
        {
            var topicMemories = memories.Where(m => m.Topic == topic).ToList();
            var sessions = topicMemories.Select(m => m.SessionId).Distinct().ToList();

            if (sessions.Count >= 2)
            {
                queries.Add(new LoCoMoTestQuery
                {
                    Id = $"cross-{Guid.NewGuid():N}",
                    Query = $"Across all our sessions, what have we discussed about {topic}?",
                    QueryType = LoCoMoQueryType.CrossSession,
                    RelevantMemoryIds = topicMemories.Select(m => m.Id).ToList(),
                    ExpectedAnswer = string.Join(" ", topicMemories.Select(m => m.Content)),
                    TopK = 15
                });
            }
        }

        return queries;
    }

    private static List<LoCoMoTestQuery> GenerateFactualQueries(
        List<LoCoMoConversationMemory> memories,
        int count,
        Random random)
    {
        return memories
            .OrderBy(_ => random.Next())
            .Take(count)
            .Select(m => new LoCoMoTestQuery
            {
                Id = $"factual-{Guid.NewGuid():N}",
                Query = $"Tell me the specific details about our {m.Topic} discussion in session {m.SessionId}.",
                QueryType = LoCoMoQueryType.Factual,
                RelevantMemoryIds = [m.Id],
                ExpectedAnswer = m.Content,
                TopK = 5
            })
            .ToList();
    }
}
