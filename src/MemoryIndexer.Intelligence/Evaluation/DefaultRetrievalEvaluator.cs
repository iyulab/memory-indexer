using System.Diagnostics;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Evaluation;

/// <summary>
/// Default implementation of retrieval evaluator.
/// Uses embedding-based similarity for basic metrics, with optional LLM integration for advanced metrics.
/// </summary>
public sealed class DefaultRetrievalEvaluator : IRetrievalEvaluator
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<DefaultRetrievalEvaluator> _logger;
    private readonly QualityTargets _targets;

    public DefaultRetrievalEvaluator(
        IEmbeddingService embeddingService,
        ILogger<DefaultRetrievalEvaluator> logger,
        QualityTargets? targets = null)
    {
        _embeddingService = embeddingService;
        _logger = logger;
        _targets = targets ?? new QualityTargets();
    }

    public async Task<RetrievalEvaluationResult> EvaluateAsync(
        string query,
        IReadOnlyList<string> retrievedContexts,
        string? generatedAnswer = null,
        string? groundTruth = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        if (retrievedContexts.Count == 0)
        {
            return CreateEmptyResult(stopwatch.Elapsed);
        }

        // Generate embeddings for query and contexts
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);
        var contextEmbeddings = new List<ReadOnlyMemory<float>>();

        foreach (var context in retrievedContexts)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(context, cancellationToken);
            contextEmbeddings.Add(embedding);
        }

        // Calculate context scores
        var contextScores = new List<ContextScore>();
        for (var i = 0; i < retrievedContexts.Count; i++)
        {
            var similarity = VectorMath.CosineSimilarity(queryEmbedding.Span, contextEmbeddings[i].Span);
            contextScores.Add(new ContextScore
            {
                Index = i,
                Relevance = similarity,
                IsUseful = similarity > 0.5f,
                Explanation = similarity > 0.7f ? "Highly relevant" :
                    similarity > 0.5f ? "Moderately relevant" : "Low relevance"
            });
        }

        // Calculate metrics
        var contextRelevance = contextScores.Average(s => s.Relevance);
        var semanticSimilarity = contextRelevance; // Same for embedding-based evaluation

        // Context precision: Are better contexts ranked higher?
        var precision = CalculatePrecision(contextScores);

        // Answerability: Can the question be answered from the contexts?
        var answerability = CalculateAnswerability(contextScores);

        // Optional metrics requiring generated answer
        float? faithfulness = null;
        float? answerRelevance = null;
        float? contextRecall = null;

        if (!string.IsNullOrEmpty(generatedAnswer))
        {
            var answerEmbedding = await _embeddingService.GenerateEmbeddingAsync(generatedAnswer, cancellationToken);

            // Answer relevance: Does answer address the question?
            answerRelevance = VectorMath.CosineSimilarity(queryEmbedding.Span, answerEmbedding.Span);

            // Faithfulness: Is answer grounded in contexts?
            faithfulness = CalculateFaithfulness(answerEmbedding, contextEmbeddings);
        }

        if (!string.IsNullOrEmpty(groundTruth))
        {
            var truthEmbedding = await _embeddingService.GenerateEmbeddingAsync(groundTruth, cancellationToken);

            // Context recall: How much of ground truth is covered?
            contextRecall = CalculateContextRecall(truthEmbedding, contextEmbeddings);
        }

        // Calculate overall score
        var overallScore = CalculateOverallScore(
            contextRelevance, contextRecall, faithfulness, answerRelevance, answerability);

        stopwatch.Stop();

        return new RetrievalEvaluationResult
        {
            ContextRelevance = contextRelevance,
            ContextRecall = contextRecall,
            Faithfulness = faithfulness,
            AnswerRelevance = answerRelevance,
            Answerability = answerability,
            ContextPrecision = precision,
            SemanticSimilarity = semanticSimilarity,
            OverallScore = overallScore,
            ContextCount = retrievedContexts.Count,
            ContextScores = contextScores,
            Metadata = new EvaluationMetadata
            {
                Duration = stopwatch.Elapsed,
                UsedLlmEvaluation = false
            }
        };
    }

    public async Task<BatchEvaluationResult> EvaluateBatchAsync(
        IReadOnlyList<EvaluationCase> evaluations,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var caseResults = new List<CaseResult>();
        var successfulResults = new List<RetrievalEvaluationResult>();

        foreach (var evaluation in evaluations)
        {
            try
            {
                var result = await EvaluateAsync(
                    evaluation.Query,
                    evaluation.RetrievedContexts,
                    evaluation.GeneratedAnswer,
                    evaluation.GroundTruth,
                    cancellationToken);

                caseResults.Add(new CaseResult
                {
                    CaseId = evaluation.Id,
                    Success = true,
                    Result = result
                });
                successfulResults.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate case {CaseId}", evaluation.Id);
                caseResults.Add(new CaseResult
                {
                    CaseId = evaluation.Id,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        stopwatch.Stop();

        if (successfulResults.Count == 0)
        {
            return new BatchEvaluationResult
            {
                TotalCases = evaluations.Count,
                SuccessfulEvaluations = 0,
                FailedEvaluations = evaluations.Count,
                CaseResults = caseResults,
                TotalDuration = stopwatch.Elapsed
            };
        }

        // Calculate aggregated metrics
        var metrics = new AggregatedMetrics
        {
            AvgContextRelevance = successfulResults.Average(r => r.ContextRelevance),
            AvgContextRecall = successfulResults.Where(r => r.ContextRecall.HasValue).Select(r => r.ContextRecall!.Value).DefaultIfEmpty().Average(),
            AvgFaithfulness = successfulResults.Where(r => r.Faithfulness.HasValue).Select(r => r.Faithfulness!.Value).DefaultIfEmpty().Average(),
            AvgAnswerRelevance = successfulResults.Where(r => r.AnswerRelevance.HasValue).Select(r => r.AnswerRelevance!.Value).DefaultIfEmpty().Average(),
            AvgAnswerability = successfulResults.Average(r => r.Answerability),
            AvgOverallScore = successfulResults.Average(r => r.OverallScore),
            CasesMeetingTargets = successfulResults.Count(r => r.OverallScore >= _targets.OverallScore),
            PercentMeetingTargets = successfulResults.Count(r => r.OverallScore >= _targets.OverallScore) / (float)successfulResults.Count
        };

        // Calculate distribution
        var scores = successfulResults.Select(r => r.OverallScore).OrderBy(s => s).ToList();
        var distribution = new ScoreDistribution
        {
            Min = scores.Min(),
            Max = scores.Max(),
            Median = GetPercentile(scores, 0.5f),
            P25 = GetPercentile(scores, 0.25f),
            P75 = GetPercentile(scores, 0.75f),
            P95 = GetPercentile(scores, 0.95f),
            StdDev = CalculateStdDev(scores)
        };

        return new BatchEvaluationResult
        {
            TotalCases = evaluations.Count,
            SuccessfulEvaluations = successfulResults.Count,
            FailedEvaluations = caseResults.Count(r => !r.Success),
            Metrics = metrics,
            CaseResults = caseResults,
            Distribution = distribution,
            TotalDuration = stopwatch.Elapsed
        };
    }

    public async Task<NeedleInHaystackResult> RunNeedleInHaystackTestAsync(
        string needleContent,
        int contextSize,
        int iterations = 10,
        CancellationToken cancellationToken = default)
    {
        var needleEmbedding = await _embeddingService.GenerateEmbeddingAsync(needleContent, cancellationToken);
        var iterationResults = new List<NeedleIterationResult>();
        var random = new Random();

        for (var i = 0; i < iterations; i++)
        {
            // Generate distractor contexts (haystack)
            var distractors = GenerateDistractors(contextSize, random);

            // Insert needle at random position
            var needlePosition = random.Next(contextSize + 1);
            var allContexts = new List<string>(distractors);
            allContexts.Insert(needlePosition, needleContent);

            // Generate embeddings for all contexts
            var contextEmbeddings = new List<(string Content, ReadOnlyMemory<float> Embedding, int OriginalIndex)>();
            for (var j = 0; j < allContexts.Count; j++)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(allContexts[j], cancellationToken);
                contextEmbeddings.Add((allContexts[j], embedding, j));
            }

            // Search for needle
            var searchQuery = GenerateSearchQuery(needleContent, random);
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(searchQuery, cancellationToken);

            // Rank by similarity
            var ranked = contextEmbeddings
                .Select(c => (c.Content, c.OriginalIndex, Score: VectorMath.CosineSimilarity(queryEmbedding.Span, c.Embedding.Span)))
                .OrderByDescending(c => c.Score)
                .ToList();

            // Find needle in results
            var needleRankIndex = ranked.FindIndex(r => r.OriginalIndex == needlePosition);
            var needleFound = needleRankIndex >= 0 && needleRankIndex < 5; // Top 5
            var needleScore = needleRankIndex >= 0 ? ranked[needleRankIndex].Score : 0;

            iterationResults.Add(new NeedleIterationResult
            {
                Iteration = i + 1,
                NeedleFound = needleFound,
                NeedleRank = needleRankIndex + 1, // 1-based
                NeedleScore = needleScore,
                Query = searchQuery
            });
        }

        var successfulIterations = iterationResults.Where(r => r.NeedleFound).ToList();

        return new NeedleInHaystackResult
        {
            TotalIterations = iterations,
            SuccessfulRetrievals = successfulIterations.Count,
            SuccessRate = successfulIterations.Count / (float)iterations,
            AvgNeedleRank = successfulIterations.Count > 0 ? (float)successfulIterations.Average(r => r.NeedleRank) : 0,
            AvgNeedleScore = successfulIterations.Count > 0 ? (float)successfulIterations.Average(r => r.NeedleScore) : 0,
            ContextSize = contextSize,
            IterationResults = iterationResults
        };
    }

    private static RetrievalEvaluationResult CreateEmptyResult(TimeSpan duration)
    {
        return new RetrievalEvaluationResult
        {
            ContextRelevance = 0,
            Answerability = 0,
            ContextPrecision = 0,
            SemanticSimilarity = 0,
            OverallScore = 0,
            ContextCount = 0,
            Metadata = new EvaluationMetadata { Duration = duration }
        };
    }

    private static float CalculatePrecision(List<ContextScore> scores)
    {
        if (scores.Count == 0) return 0;

        // Check if scores are in descending order of relevance
        var sortedByRelevance = scores.OrderByDescending(s => s.Relevance).ToList();
        var matchingPositions = 0;

        for (var i = 0; i < scores.Count; i++)
        {
            if (scores[i].Index == sortedByRelevance[i].Index)
            {
                matchingPositions++;
            }
        }

        return matchingPositions / (float)scores.Count;
    }

    private static float CalculateAnswerability(List<ContextScore> scores)
    {
        if (scores.Count == 0) return 0;

        // Answerability based on how many contexts are useful
        var usefulCount = scores.Count(s => s.IsUseful);
        var maxRelevance = scores.Max(s => s.Relevance);

        // Combine useful ratio with max relevance
        return (usefulCount / (float)scores.Count * 0.5f) + (maxRelevance * 0.5f);
    }

    private static float CalculateFaithfulness(
        ReadOnlyMemory<float> answerEmbedding,
        List<ReadOnlyMemory<float>> contextEmbeddings)
    {
        if (contextEmbeddings.Count == 0) return 0;

        // Faithfulness: Max similarity between answer and any context
        var maxSimilarity = contextEmbeddings
            .Max(c => VectorMath.CosineSimilarity(answerEmbedding.Span, c.Span));

        return maxSimilarity;
    }

    private static float CalculateContextRecall(
        ReadOnlyMemory<float> truthEmbedding,
        List<ReadOnlyMemory<float>> contextEmbeddings)
    {
        if (contextEmbeddings.Count == 0) return 0;

        // Context recall: Max similarity between ground truth and contexts
        var maxSimilarity = contextEmbeddings
            .Max(c => VectorMath.CosineSimilarity(truthEmbedding.Span, c.Span));

        return maxSimilarity;
    }

    private static float CalculateOverallScore(
        float contextRelevance,
        float? contextRecall,
        float? faithfulness,
        float? answerRelevance,
        float answerability)
    {
        var scores = new List<float> { contextRelevance, answerability };
        if (contextRecall.HasValue) scores.Add(contextRecall.Value);
        if (faithfulness.HasValue) scores.Add(faithfulness.Value);
        if (answerRelevance.HasValue) scores.Add(answerRelevance.Value);

        return scores.Average();
    }

    private static float GetPercentile(List<float> sortedValues, float percentile)
    {
        if (sortedValues.Count == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        return sortedValues[Math.Max(0, Math.Min(index, sortedValues.Count - 1))];
    }

    private static float CalculateStdDev(List<float> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sumSquares = values.Sum(v => (v - avg) * (v - avg));
        return (float)Math.Sqrt(sumSquares / (values.Count - 1));
    }

    private static List<string> GenerateDistractors(int count, Random random)
    {
        var templates = new[]
        {
            "The weather today is pleasant with mild temperatures.",
            "Software development requires careful planning and execution.",
            "The stock market showed mixed results yesterday.",
            "Healthy eating habits contribute to overall wellbeing.",
            "Technology continues to evolve at a rapid pace.",
            "Education is fundamental to personal and societal growth.",
            "Environmental conservation is increasingly important.",
            "Cultural diversity enriches our communities.",
            "Scientific research advances human understanding.",
            "Communication skills are essential in the workplace."
        };

        return Enumerable.Range(0, count)
            .Select(_ => templates[random.Next(templates.Length)] + $" [{random.Next(1000)}]")
            .ToList();
    }

    private static string GenerateSearchQuery(string needleContent, Random random)
    {
        // Extract key terms from needle for search query
        var words = needleContent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keyWords = words.Where(w => w.Length > 4).Take(3).ToList();

        if (keyWords.Count == 0)
        {
            return needleContent;
        }

        // Shuffle and combine
        return string.Join(" ", keyWords.OrderBy(_ => random.Next()));
    }
}
