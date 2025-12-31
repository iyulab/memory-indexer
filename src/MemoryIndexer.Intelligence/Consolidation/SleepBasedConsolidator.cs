using System.Diagnostics;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Consolidation;

/// <summary>
/// Memory consolidator based on the SLEEP paradigm.
/// Implements memory consolidation, reflection generation, and forgetting curves.
/// </summary>
/// <remarks>
/// The consolidation process mimics biological sleep:
/// 1. Memory Replay: Review recent memories
/// 2. Reflection: Generate higher-level inferences
/// 3. Consolidation: Merge similar memories
/// 4. Decay: Apply forgetting curve to strengthen/weaken memories
/// </remarks>
public sealed class SleepBasedConsolidator : IMemoryConsolidator
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<SleepBasedConsolidator> _logger;

    public SleepBasedConsolidator(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        ILogger<SleepBasedConsolidator> logger)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConsolidationResult> ConsolidateAsync(
        ConsolidationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ConsolidationOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting memory consolidation cycle (Sleep)");

        try
        {
            // Phase 1: Retrieve memories for consolidation
            var cutoffDate = DateTime.UtcNow - options.MaxMemoryAge;
            var memories = await GetMemoriesForConsolidationAsync(
                cutoffDate, options.UserId, options.SessionId, cancellationToken);

            if (memories.Count == 0)
            {
                _logger.LogInformation("No memories found for consolidation");
                return new ConsolidationResult
                {
                    Success = true,
                    MemoriesProcessed = 0,
                    Duration = stopwatch.Elapsed
                };
            }

            _logger.LogDebug("Retrieved {Count} memories for consolidation", memories.Count);

            // Phase 2: Apply forgetting curve decay
            var decayResults = new List<MemoryDecayResult>();
            var memoriesDecayed = 0;
            var memoriesArchived = 0;

            if (options.ApplyForgettingCurve)
            {
                decayResults = (await ApplyForgettingCurveAsync(memories, cancellationToken)).ToList();
                memoriesDecayed = decayResults.Count(r => r.NewScore != r.PreviousScore);
                memoriesArchived = decayResults.Count(r => r.ShouldArchive);

                // Apply decay updates to store
                foreach (var result in decayResults.Where(r => r.NewScore != r.PreviousScore))
                {
                    var memory = memories.FirstOrDefault(m => m.Id == result.MemoryId);
                    if (memory != null)
                    {
                        memory.ImportanceScore = result.NewScore;
                        await _memoryStore.UpdateAsync(memory, cancellationToken);
                    }
                }

                _logger.LogDebug("Applied forgetting curve: {Decayed} decayed, {Archived} below archive threshold",
                    memoriesDecayed, memoriesArchived);
            }

            // Phase 3: Identify and merge similar memories
            var mergeOps = await IdentifyMergeCandidatesAsync(
                memories, options.MergeSimilarityThreshold, cancellationToken);
            var memoriesMerged = mergeOps.Sum(op => op.MemoriesToMerge.Count);

            // Phase 4: Generate reflections
            var reflections = new List<MemoryUnit>();
            if (memories.Count >= options.MinMemoriesForReflection)
            {
                var generatedReflections = await GenerateReflectionsAsync(memories, cancellationToken);
                reflections.AddRange(generatedReflections.Take(options.MaxReflectionsPerCycle));

                // Store reflections
                foreach (var reflection in reflections)
                {
                    await _memoryStore.StoreAsync(reflection, cancellationToken);
                }

                _logger.LogDebug("Generated {Count} reflections", reflections.Count);
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Consolidation complete: {Processed} processed, {Reflections} reflections, {Merged} merged, {Decayed} decayed",
                memories.Count, reflections.Count, memoriesMerged, memoriesDecayed);

            return new ConsolidationResult
            {
                Success = true,
                MemoriesProcessed = memories.Count,
                ReflectionsGenerated = reflections.Count,
                MemoriesMerged = memoriesMerged,
                MemoriesArchived = memoriesArchived,
                MemoriesDecayed = memoriesDecayed,
                Duration = stopwatch.Elapsed,
                Reflections = reflections
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory consolidation failed");
            return new ConsolidationResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryUnit>> GenerateReflectionsAsync(
        IReadOnlyList<MemoryUnit> recentMemories,
        CancellationToken cancellationToken = default)
    {
        if (recentMemories.Count < 3)
        {
            return [];
        }

        var reflections = new List<MemoryUnit>();

        // Group memories by topic for reflection
        var topicGroups = recentMemories
            .Where(m => m.Topics.Count > 0)
            .SelectMany(m => m.Topics.Select(t => (Topic: t, Memory: m)))
            .GroupBy(x => x.Topic)
            .Where(g => g.Count() >= 2)
            .ToList();

        foreach (var group in topicGroups)
        {
            var topicMemories = group.Select(g => g.Memory).ToList();

            // Generate reflection by synthesizing topic memories
            var reflection = await GenerateTopicReflectionAsync(
                group.Key, topicMemories, cancellationToken);

            if (reflection != null)
            {
                reflections.Add(reflection);
            }
        }

        // Generate cross-topic reflections for highly important memories
        var importantMemories = recentMemories
            .Where(m => m.ImportanceScore >= 0.7f)
            .OrderByDescending(m => m.ImportanceScore)
            .Take(10)
            .ToList();

        if (importantMemories.Count >= 3)
        {
            var crossReflection = await GenerateCrossTopicReflectionAsync(
                importantMemories, cancellationToken);

            if (crossReflection != null)
            {
                reflections.Add(crossReflection);
            }
        }

        return reflections;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MemoryDecayResult>> ApplyForgettingCurveAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MemoryDecayResult>();
        var now = DateTime.UtcNow;

        foreach (var memory in memories)
        {
            // Calculate memory strength based on:
            // 1. Access frequency (more accesses = stronger)
            // 2. Recency of access (more recent = stronger)
            // 3. Original importance (higher = stronger)

            var daysSinceAccess = memory.LastAccessedAt.HasValue
                ? (now - memory.LastAccessedAt.Value).TotalDays
                : (now - memory.CreatedAt).TotalDays;

            // Strength factor: higher access count and importance = slower decay
            var accessBonus = MathF.Log(1 + memory.AccessCount);
            var importanceBonus = memory.ImportanceScore;
            var strengthFactor = 1.0f + accessBonus + importanceBonus;

            // Ebbinghaus forgetting curve: R = e^(-t/S)
            // R = retention, t = time, S = strength
            var retention = MathF.Exp((float)(-daysSinceAccess / (strengthFactor * 10)));

            // Apply retention to importance score
            var newScore = memory.ImportanceScore * retention;

            // Ensure minimum score for recently accessed memories
            if (daysSinceAccess < 1)
            {
                newScore = MathF.Max(newScore, memory.ImportanceScore * 0.95f);
            }

            // Clamp to valid range
            newScore = Math.Clamp(newScore, 0.01f, 1.0f);

            results.Add(new MemoryDecayResult
            {
                MemoryId = memory.Id,
                PreviousScore = memory.ImportanceScore,
                NewScore = newScore,
                ShouldArchive = newScore < 0.2f,
                StrengthFactor = strengthFactor
            });
        }

        return Task.FromResult<IReadOnlyList<MemoryDecayResult>>(results);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryMergeOperation>> IdentifyMergeCandidatesAsync(
        IReadOnlyList<MemoryUnit> candidates,
        float similarityThreshold = 0.85f,
        CancellationToken cancellationToken = default)
    {
        if (candidates.Count < 2)
        {
            return [];
        }

        var mergeOperations = new List<MemoryMergeOperation>();
        var processed = new HashSet<Guid>();

        // Ensure all candidates have embeddings
        var memoriesWithEmbeddings = candidates
            .Where(m => m.Embedding.HasValue && m.Embedding.Value.Length > 0)
            .ToList();

        for (var i = 0; i < memoriesWithEmbeddings.Count; i++)
        {
            var primary = memoriesWithEmbeddings[i];
            if (processed.Contains(primary.Id))
            {
                continue;
            }

            var toMerge = new List<MemoryUnit>();
            var similarities = new List<float>();

            for (var j = i + 1; j < memoriesWithEmbeddings.Count; j++)
            {
                var candidate = memoriesWithEmbeddings[j];
                if (processed.Contains(candidate.Id))
                {
                    continue;
                }

                var similarity = VectorMath.CosineSimilarity(
                    primary.Embedding!.Value.Span,
                    candidate.Embedding!.Value.Span);

                if (similarity >= similarityThreshold)
                {
                    toMerge.Add(candidate);
                    similarities.Add(similarity);
                    processed.Add(candidate.Id);
                }
            }

            if (toMerge.Count > 0)
            {
                processed.Add(primary.Id);

                // Generate merged content suggestion
                var mergedContent = GenerateMergedContent(primary, toMerge);

                mergeOperations.Add(new MemoryMergeOperation
                {
                    PrimaryMemory = primary,
                    MemoriesToMerge = toMerge,
                    SimilarityScores = similarities,
                    SuggestedMergedContent = mergedContent
                });
            }
        }

        return mergeOperations;
    }

    private async Task<List<MemoryUnit>> GetMemoriesForConsolidationAsync(
        DateTime cutoffDate,
        string? userId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        // UserId is required for multi-tenant isolation
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("UserId is required for consolidation - skipping");
            return [];
        }

        // Get all memories for the user within the time window
        var filterOptions = new MemoryFilterOptions
        {
            SessionId = sessionId
        };

        var allMemories = await _memoryStore.GetAllAsync(userId, filterOptions, cancellationToken);

        return allMemories
            .Where(m => m.CreatedAt >= cutoffDate)
            .OrderByDescending(m => m.ImportanceScore)
            .ToList();
    }

    private async Task<MemoryUnit?> GenerateTopicReflectionAsync(
        string topic,
        List<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        if (memories.Count < 2)
        {
            return null;
        }

        // Extract key points from memories
        var keyPoints = memories
            .OrderByDescending(m => m.ImportanceScore)
            .Take(5)
            .Select(m => m.Content)
            .ToList();

        // Synthesize reflection content
        var reflectionContent = $"Reflection on '{topic}': Based on {memories.Count} related memories, " +
            $"the key insights are: {string.Join("; ", keyPoints.Select((p, i) => $"({i + 1}) {TruncateContent(p, 100)}"))}";

        // Calculate importance as weighted average
        var avgImportance = memories.Average(m => m.ImportanceScore);
        var reflectionImportance = Math.Min(avgImportance * 1.2f, 1.0f); // Reflections slightly more important

        // Generate embedding for reflection
        var embedding = await _embeddingService.GenerateEmbeddingAsync(reflectionContent, cancellationToken);

        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = reflectionContent,
            Embedding = embedding,
            Type = MemoryType.Reflection,
            Tier = MemoryTier.User, // Reflections are long-term cross-session memories
            Topics = [topic],
            ImportanceScore = reflectionImportance,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UserId = memories.First().UserId,
            Metadata = new Dictionary<string, string>
            {
                ["source_type"] = "reflection",
                ["source_count"] = memories.Count.ToString(),
                ["source_topic"] = topic,
                ["source_ids"] = string.Join(",", memories.Select(m => m.Id))
            }
        };
    }

    private async Task<MemoryUnit?> GenerateCrossTopicReflectionAsync(
        List<MemoryUnit> importantMemories,
        CancellationToken cancellationToken)
    {
        // Extract all topics
        var allTopics = importantMemories
            .SelectMany(m => m.Topics)
            .Distinct()
            .Take(5)
            .ToList();

        if (allTopics.Count < 2)
        {
            return null;
        }

        // Synthesize cross-topic reflection
        var reflectionContent = $"Cross-topic reflection: Connections identified across {string.Join(", ", allTopics)}. " +
            $"Key patterns from {importantMemories.Count} significant memories suggest interrelated themes.";

        var embedding = await _embeddingService.GenerateEmbeddingAsync(reflectionContent, cancellationToken);

        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = reflectionContent,
            Embedding = embedding,
            Type = MemoryType.Reflection,
            Tier = MemoryTier.User, // Reflections are long-term cross-session memories
            Topics = allTopics,
            ImportanceScore = 0.8f, // Cross-topic reflections are high value
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            UserId = importantMemories.First().UserId,
            Metadata = new Dictionary<string, string>
            {
                ["source_type"] = "cross_topic_reflection",
                ["source_count"] = importantMemories.Count.ToString(),
                ["source_ids"] = string.Join(",", importantMemories.Select(m => m.Id))
            }
        };
    }

    private static string GenerateMergedContent(MemoryUnit primary, List<MemoryUnit> toMerge)
    {
        // Combine unique information from all memories
        var allContent = new[] { primary.Content }
            .Concat(toMerge.Select(m => m.Content))
            .ToList();

        // For now, keep primary content and note the merge
        return $"{primary.Content} [Consolidated from {toMerge.Count + 1} related memories]";
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
        {
            return content;
        }

        return content[..maxLength] + "...";
    }
}
