using System.Numerics.Tensors;
using System.Text.RegularExpressions;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Summarization;

/// <summary>
/// Extractive summarization service that selects important sentences.
/// Uses TF-IDF and embedding-based importance scoring.
/// </summary>
public sealed partial class ExtractiveSummarizer : ISummarizationService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<ExtractiveSummarizer> _logger;

    /// <summary>
    /// Token budget threshold percentage to trigger summarization.
    /// </summary>
    public float TriggerThreshold { get; init; } = 0.85f;

    /// <summary>
    /// Minimum memories before considering summarization.
    /// </summary>
    public int MinMemoriesForSummarization { get; init; } = 5;

    public ExtractiveSummarizer(
        IEmbeddingService embeddingService,
        ILogger<ExtractiveSummarizer> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MemorySummary> SummarizeAsync(
        IEnumerable<MemoryUnit> memories,
        SummarizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SummarizationOptions();
        var memoryList = memories.ToList();

        if (memoryList.Count == 0)
        {
            return new MemorySummary
            {
                Content = string.Empty,
                OriginalTokenCount = 0,
                SummarizedTokenCount = 0
            };
        }

        _logger.LogDebug("Summarizing {Count} memories", memoryList.Count);

        // Extract all sentences from memories
        var allSentences = new List<ScoredSentence>();
        var originalTokenCount = 0;

        foreach (var memory in memoryList)
        {
            var sentences = SplitIntoSentences(memory.Content);
            originalTokenCount += EstimateTokenCount(memory.Content);

            foreach (var sentence in sentences)
            {
                if (sentence.Length < 10) continue; // Skip very short sentences

                allSentences.Add(new ScoredSentence
                {
                    Text = sentence,
                    SourceMemoryId = memory.Id,
                    BaseImportance = memory.ImportanceScore,
                    Timestamp = memory.CreatedAt
                });
            }
        }

        if (allSentences.Count == 0)
        {
            return new MemorySummary
            {
                Content = string.Join(" ", memoryList.Select(m => m.Content)),
                SourceMemoryIds = memoryList.Select(m => m.Id).ToList(),
                OriginalTokenCount = originalTokenCount,
                SummarizedTokenCount = originalTokenCount
            };
        }

        // Score sentences
        await ScoreSentencesAsync(allSentences, options, cancellationToken);

        // Select top sentences based on target compression
        var targetTokens = (int)(originalTokenCount * options.TargetCompressionRatio);
        targetTokens = Math.Max(targetTokens, options.MaxOutputTokens);

        var selectedSentences = SelectSentences(allSentences, targetTokens, options);

        // Build summary
        var summaryContent = BuildSummaryContent(selectedSentences, options);
        var summaryTokenCount = EstimateTokenCount(summaryContent);

        // Extract entities and topics
        var entities = ExtractEntities(selectedSentences);
        var topics = ExtractTopics(memoryList);

        // Generate embedding for summary
        var embedding = await _embeddingService.GenerateEmbeddingAsync(summaryContent, cancellationToken);

        var summary = new MemorySummary
        {
            Content = summaryContent,
            KeyPoints = selectedSentences.Take(5).Select(s => s.Text).ToList(),
            Entities = entities,
            Topics = topics,
            SourceMemoryIds = memoryList.Select(m => m.Id).ToList(),
            OriginalTokenCount = originalTokenCount,
            SummarizedTokenCount = summaryTokenCount,
            Embedding = embedding
        };

        _logger.LogInformation(
            "Summary created: {Original} → {Summary} tokens ({Ratio:P0} compression)",
            originalTokenCount, summaryTokenCount, summary.CompressionRatio);

        return summary;
    }

    /// <inheritdoc />
    public async Task<MemorySummary> IncrementalUpdateAsync(
        MemorySummary existing,
        IEnumerable<MemoryUnit> newMemories,
        CancellationToken cancellationToken = default)
    {
        var newMemoryList = newMemories.ToList();

        if (newMemoryList.Count == 0)
            return existing;

        _logger.LogDebug("Incrementally updating summary with {Count} new memories", newMemoryList.Count);

        // Create summary of new content
        var newSummary = await SummarizeAsync(newMemoryList, cancellationToken: cancellationToken);

        // Merge summaries using CoK-style operations
        var mergedContent = MergeSummaries(existing.Content, newSummary.Content);
        var mergedTokenCount = EstimateTokenCount(mergedContent);

        // If merged content is too long, re-summarize
        if (mergedTokenCount > existing.SummarizedTokenCount * 1.5)
        {
            // Create a virtual memory from the merged content and re-summarize
            var virtualMemories = new List<MemoryUnit>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    UserId = "system",
                    Content = existing.Content,
                    Type = MemoryType.Semantic,
                    ImportanceScore = 0.8f
                }
            };
            virtualMemories.AddRange(newMemoryList);

            return await SummarizeAsync(virtualMemories, cancellationToken: cancellationToken);
        }

        // Update existing summary
        existing.Content = mergedContent;
        existing.SummarizedTokenCount = mergedTokenCount;
        existing.OriginalTokenCount += newSummary.OriginalTokenCount;
        existing.SourceMemoryIds.AddRange(newMemoryList.Select(m => m.Id));
        existing.KeyPoints = existing.KeyPoints.Union(newSummary.KeyPoints).Take(10).ToList();
        existing.Entities = existing.Entities.Union(newSummary.Entities).Distinct().ToList();
        existing.Topics = existing.Topics.Union(newSummary.Topics).Distinct().ToList();
        existing.UpdatedAt = DateTime.UtcNow;

        // Update embedding
        existing.Embedding = await _embeddingService.GenerateEmbeddingAsync(mergedContent, cancellationToken);

        return existing;
    }

    /// <inheritdoc />
    public async Task<HierarchicalSummary> CreateHierarchyAsync(
        IEnumerable<MemoryUnit> memories,
        int levels = 3,
        CancellationToken cancellationToken = default)
    {
        var memoryList = memories.ToList();
        levels = Math.Clamp(levels, 2, 5);

        _logger.LogDebug("Creating {Levels}-level hierarchical summary for {Count} memories",
            levels, memoryList.Count);

        var hierarchyLevels = new List<List<MemorySummary>>();
        var currentItems = memoryList;
        var totalOriginalTokens = memoryList.Sum(m => EstimateTokenCount(m.Content));

        // Build hierarchy from bottom up
        for (var level = 0; level < levels - 1; level++)
        {
            var chunkSize = Math.Max(3, currentItems.Count / Math.Max(1, levels - level));
            var levelSummaries = new List<MemorySummary>();

            // Group memories into chunks and summarize each
            for (var i = 0; i < currentItems.Count; i += chunkSize)
            {
                var chunk = currentItems.Skip(i).Take(chunkSize).ToList();
                var summary = await SummarizeAsync(chunk, cancellationToken: cancellationToken);
                levelSummaries.Add(summary);
            }

            hierarchyLevels.Add(levelSummaries);

            // Convert summaries to memories for next level
            currentItems = levelSummaries.Select(s => new MemoryUnit
            {
                Id = s.Id,
                UserId = "system",
                Content = s.Content,
                Type = MemoryType.Semantic,
                ImportanceScore = 0.9f,
                Embedding = s.Embedding
            }).ToList();
        }

        // Create root summary from final level
        var rootSummary = await SummarizeAsync(currentItems, cancellationToken: cancellationToken);

        return new HierarchicalSummary
        {
            RootSummary = rootSummary,
            Levels = hierarchyLevels,
            TotalMemoryCount = memoryList.Count,
            OverallCompressionRatio = totalOriginalTokens > 0
                ? (float)rootSummary.SummarizedTokenCount / totalOriginalTokens
                : 0
        };
    }

    /// <inheritdoc />
    public bool ShouldTriggerSummarization(int currentTokenCount, int maxTokens, int memoryCount)
    {
        if (memoryCount < MinMemoriesForSummarization)
            return false;

        var usageRatio = (float)currentTokenCount / maxTokens;
        return usageRatio >= TriggerThreshold;
    }

    private async Task ScoreSentencesAsync(
        List<ScoredSentence> sentences,
        SummarizationOptions options,
        CancellationToken cancellationToken)
    {
        // Generate embeddings for all sentences
        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(
            sentences.Select(s => s.Text), cancellationToken);

        for (var i = 0; i < sentences.Count; i++)
        {
            sentences[i].Embedding = embeddings[i];
        }

        // Calculate centroid embedding (represents the overall content)
        var centroid = CalculateCentroid(embeddings);

        // Score each sentence
        foreach (var sentence in sentences)
        {
            var score = 0f;

            // 1. Similarity to centroid (relevance to overall topic)
            if (sentence.Embedding.HasValue)
            {
                score += CalculateCosineSimilarity(sentence.Embedding.Value, centroid) * 0.3f;
            }

            // 2. Position score (earlier sentences often more important)
            // Normalize based on timestamp
            score += sentence.BaseImportance * 0.3f;

            // 3. Length score (prefer medium-length sentences)
            var wordCount = sentence.Text.Split(' ').Length;
            var lengthScore = wordCount switch
            {
                < 5 => 0.3f,
                <= 25 => 1.0f,
                <= 50 => 0.7f,
                _ => 0.4f
            };
            score += lengthScore * 0.2f;

            // 4. Entity/keyword presence
            if (options.PreserveEntities && HasNamedEntities(sentence.Text))
            {
                score += 0.1f;
            }

            if (options.PreserveTimestamps && HasTimestamp(sentence.Text))
            {
                score += 0.1f;
            }

            sentence.Score = score;
        }
    }

    private static List<ScoredSentence> SelectSentences(
        List<ScoredSentence> sentences,
        int targetTokens,
        SummarizationOptions options)
    {
        var selected = new List<ScoredSentence>();
        var currentTokens = 0;

        // Sort by score descending
        var sorted = sentences.OrderByDescending(s => s.Score).ToList();

        foreach (var sentence in sorted)
        {
            var sentenceTokens = EstimateTokenCount(sentence.Text);

            if (currentTokens + sentenceTokens <= targetTokens)
            {
                selected.Add(sentence);
                currentTokens += sentenceTokens;
            }

            if (currentTokens >= targetTokens)
                break;
        }

        // Re-order by timestamp for coherent reading
        return selected.OrderBy(s => s.Timestamp).ToList();
    }

    private static string BuildSummaryContent(List<ScoredSentence> sentences, SummarizationOptions options)
    {
        if (sentences.Count == 0)
            return string.Empty;

        // Group by source memory for better coherence
        var grouped = sentences
            .GroupBy(s => s.SourceMemoryId)
            .OrderBy(g => g.Min(s => s.Timestamp));

        var parts = new List<string>();

        foreach (var group in grouped)
        {
            var groupSentences = group.OrderBy(s => s.Timestamp).Select(s => s.Text);
            parts.Add(string.Join(" ", groupSentences));
        }

        return string.Join("\n\n", parts);
    }

    private static string MergeSummaries(string existing, string newContent)
    {
        // Simple concatenation with deduplication
        var existingSentences = new HashSet<string>(
            SplitIntoSentences(existing).Select(s => s.Trim().ToLowerInvariant()));

        var newSentences = SplitIntoSentences(newContent)
            .Where(s => !existingSentences.Contains(s.Trim().ToLowerInvariant()))
            .ToList();

        if (newSentences.Count == 0)
            return existing;

        return existing + "\n\n" + string.Join(" ", newSentences);
    }

    private static List<string> ExtractEntities(List<ScoredSentence> sentences)
    {
        var entities = new HashSet<string>();

        foreach (var sentence in sentences)
        {
            // Extract capitalized words (potential names/entities)
            var matches = CapitalWordRegex().Matches(sentence.Text);
            foreach (Match match in matches)
            {
                if (match.Value.Length > 2 && !CommonWords.Contains(match.Value.ToLowerInvariant()))
                {
                    entities.Add(match.Value);
                }
            }

            // Extract emails
            var emailMatches = EmailRegex().Matches(sentence.Text);
            foreach (Match match in emailMatches)
            {
                entities.Add(match.Value);
            }
        }

        return entities.Take(20).ToList();
    }

    private static List<string> ExtractTopics(List<MemoryUnit> memories)
    {
        return memories
            .SelectMany(m => m.Topics)
            .GroupBy(t => t.ToLowerInvariant())
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => g.First())
            .ToList();
    }

    private static ReadOnlyMemory<float> CalculateCentroid(IReadOnlyList<ReadOnlyMemory<float>> embeddings)
    {
        if (embeddings.Count == 0)
            return ReadOnlyMemory<float>.Empty;

        var dimensions = embeddings[0].Length;
        var centroid = new float[dimensions];

        foreach (var embedding in embeddings)
        {
            var span = embedding.Span;
            for (var i = 0; i < dimensions; i++)
            {
                centroid[i] += span[i];
            }
        }

        for (var i = 0; i < dimensions; i++)
        {
            centroid[i] /= embeddings.Count;
        }

        return centroid;
    }

    private static float CalculateCosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var spanA = a.Span;
        var spanB = b.Span;

        if (spanA.Length != spanB.Length)
            return 0f;

        var dotProduct = TensorPrimitives.Dot(spanA, spanB);
        var normA = TensorPrimitives.Norm(spanA);
        var normB = TensorPrimitives.Norm(spanB);

        if (normA == 0 || normB == 0)
            return 0f;

        return dotProduct / (normA * normB);
    }

    private static List<string> SplitIntoSentences(string text)
    {
        return text
            .Split(['.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static int EstimateTokenCount(string text)
    {
        // Rough estimate: 1 token ≈ 4 characters for English
        return text.Length / 4;
    }

    private static bool HasNamedEntities(string text)
    {
        return CapitalWordRegex().IsMatch(text) || EmailRegex().IsMatch(text);
    }

    private static bool HasTimestamp(string text)
    {
        return DateRegex().IsMatch(text) || TimeRegex().IsMatch(text);
    }

    [GeneratedRegex(@"\b[A-Z][a-z]+\b")]
    private static partial Regex CapitalWordRegex();

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b|\b\d{4}[/-]\d{1,2}[/-]\d{1,2}\b")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b")]
    private static partial Regex TimeRegex();

    private static readonly HashSet<string> CommonWords =
    [
        "the", "a", "an", "is", "are", "was", "were", "be", "been",
        "have", "has", "had", "do", "does", "did", "will", "would",
        "could", "should", "may", "might", "must", "shall", "can",
        "this", "that", "these", "those", "i", "you", "he", "she",
        "it", "we", "they", "my", "your", "his", "her", "its", "our"
    ];

    private sealed class ScoredSentence
    {
        public required string Text { get; init; }
        public required Guid SourceMemoryId { get; init; }
        public required float BaseImportance { get; init; }
        public required DateTime Timestamp { get; init; }
        public float Score { get; set; }
        public ReadOnlyMemory<float>? Embedding { get; set; }
    }
}
