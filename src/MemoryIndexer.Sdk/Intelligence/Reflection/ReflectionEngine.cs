using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Reflection;

/// <summary>
/// Reflection Engine implementation for higher-order memory synthesis.
/// Generates insights, discovers links, and identifies patterns.
/// </summary>
/// <remarks>
/// Research basis:
/// - Generative Agents: Importance-weighted reflection over recent memories
/// - MemInsight: Contextual memory augmentation
/// - Zettelkasten: Atomic notes with bidirectional links
/// </remarks>
public sealed class ReflectionEngine : IReflectionEngine
{
    private readonly IMemoryStore _memoryStore;
    private readonly ITemporalEntityStore _entityStore;
    private readonly IScoringService _scoringService;
    private readonly ILogger<ReflectionEngine> _logger;

    // Reflection history
    private readonly ConcurrentDictionary<string, ConcurrentQueue<ReflectionRecord>> _reflectionHistory = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastReflectionTime = new();

    // Configuration
    private const int MaxHistoryPerUser = 100;
    private const float ImportanceThreshold = 100f; // Cumulative importance before reflection
    private const int MaxMemoriesPerReflection = 200;

    public ReflectionEngine(
        IMemoryStore memoryStore,
        ITemporalEntityStore entityStore,
        IScoringService scoringService,
        ILogger<ReflectionEngine> logger)
    {
        _memoryStore = memoryStore;
        _entityStore = entityStore;
        _scoringService = scoringService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReflectionResult> ReflectAsync(
        string userId,
        ReflectionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ReflectionOptions();
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("Starting reflection for user {UserId} with depth {Depth}",
            userId, options.ReflectionDepth);

        var result = new ReflectionResult();

        try
        {
            // Get memories to reflect on
            var memories = await GetMemoriesForReflectionAsync(userId, options, cancellationToken);
            result.ReflectedMemoryIds = memories.Select(m => m.Id).ToList();

            if (memories.Count == 0)
            {
                result.Success = true;
                result.Summary = "No memories to reflect on in the specified time window.";
                return result;
            }

            // Generate insights
            if (options.GenerateInsights)
            {
                result.Insights = await GenerateInsightsAsync(memories, options.FocusTopic, cancellationToken);
            }

            // Discover links
            if (options.DiscoverLinks)
            {
                result.DiscoveredLinks = await DiscoverLinksAsync(memories, null, cancellationToken);
            }

            // Identify patterns
            if (options.IdentifyPatterns)
            {
                result.Patterns = await IdentifyPatternsAsync(memories, cancellationToken);
            }

            // Generate questions
            result.Questions = await SynthesizeQuestionsFromMemoriesAsync(memories, cancellationToken);

            // Generate summary
            result.Summary = GenerateReflectionSummary(result);

            // Update statistics
            result.Statistics = new ReflectionStatistics
            {
                MemoriesProcessed = memories.Count,
                InsightsGenerated = result.Insights.Count,
                LinksDiscovered = result.DiscoveredLinks.Count,
                PatternsIdentified = result.Patterns.Count,
                QuestionsSynthesized = result.Questions.Count,
                MemoriesCreated = result.CreatedMemoryIds.Count,
                TokensProcessed = EstimateTokens(memories)
            };

            result.Success = true;

            // Record history
            await RecordReflectionAsync(userId, result, cancellationToken);
            _lastReflectionTime[userId] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reflection failed for user {UserId}", userId);
            result.Success = false;
            result.Summary = $"Reflection failed: {ex.Message}";
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        _logger.LogInformation(
            "Reflection complete for user {UserId}: {Insights} insights, {Links} links, {Patterns} patterns in {Duration}ms",
            userId, result.Insights.Count, result.DiscoveredLinks.Count, result.Patterns.Count,
            stopwatch.ElapsedMilliseconds);

        return result;
    }

    /// <inheritdoc />
    public async Task<ReflectionTriggerResult> ShouldReflectAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var result = new ReflectionTriggerResult();
        var reasons = new List<string>();
        var suggestedTopics = new List<string>();

        // Check last reflection time
        _lastReflectionTime.TryGetValue(userId, out var lastReflection);
        result.LastReflection = lastReflection == default ? null : lastReflection;

        var timeSinceLastReflection = lastReflection == default
            ? TimeSpan.FromDays(7)
            : DateTime.UtcNow - lastReflection;

        // Get recent memories
        var recentMemories = await _memoryStore.GetAllAsync(userId, new MemoryFilterOptions { Limit = 100 }, cancellationToken);
        result.AccumulatedMemories = recentMemories.Count;

        // Calculate accumulated importance
        var accumulatedImportance = 0f;
        var entityMentions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var memory in recentMemories)
        {
            // Calculate importance based on memory scoring
            var importanceScore = _scoringService.CalculateScore(memory);
            accumulatedImportance += importanceScore;

            // Track entity mentions for topic suggestions
            var entities = ExtractEntities(memory.Content);
            foreach (var entity in entities)
            {
                if (!entityMentions.ContainsKey(entity))
                    entityMentions[entity] = 0;
                entityMentions[entity]++;
            }
        }
        result.AccumulatedImportance = accumulatedImportance;

        // Determine if reflection should trigger
        if (accumulatedImportance >= ImportanceThreshold)
        {
            result.ShouldReflect = true;
            reasons.Add($"Accumulated importance ({accumulatedImportance:F0}) exceeds threshold ({ImportanceThreshold})");
        }

        if (timeSinceLastReflection > TimeSpan.FromDays(1) && recentMemories.Count >= 10)
        {
            result.ShouldReflect = true;
            reasons.Add($"Over 24 hours since last reflection with {recentMemories.Count} new memories");
        }

        if (recentMemories.Count >= 50)
        {
            result.ShouldReflect = true;
            reasons.Add($"High memory accumulation ({recentMemories.Count} memories)");
        }

        // Suggest topics based on frequent entities
        suggestedTopics.AddRange(
            entityMentions
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Select(kv => kv.Key));

        result.Reasons = reasons;
        result.SuggestedTopics = suggestedTopics;
        result.Priority = CalculateReflectionPriority(accumulatedImportance, recentMemories.Count, timeSinceLastReflection);

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryInsight>> GenerateInsightsAsync(
        IReadOnlyList<MemoryUnit> memories,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        var insights = new List<MemoryInsight>();

        if (memories.Count == 0)
            return insights;

        // 1. Generate generalizations from similar memories
        var generalizations = await GenerateGeneralizationsAsync(memories, cancellationToken);
        insights.AddRange(generalizations);

        // 2. Find connections between different topics
        var connections = await FindConnectionsAsync(memories, cancellationToken);
        insights.AddRange(connections);

        // 3. Identify trends over time
        var trends = await IdentifyTrendsAsync(memories, cancellationToken);
        insights.AddRange(trends);

        // 4. Summarize by topic if focused
        if (!string.IsNullOrEmpty(context))
        {
            var summary = await GenerateTopicSummaryAsync(memories, context, cancellationToken);
            if (summary != null)
                insights.Add(summary);
        }

        _logger.LogDebug("Generated {Count} insights from {MemoryCount} memories",
            insights.Count, memories.Count);

        return insights.OrderByDescending(i => i.ImportanceScore).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SynthesizedQuestion>> SynthesizeQuestionsAsync(
        string userId,
        string? topic = null,
        CancellationToken cancellationToken = default)
    {
        var memories = await _memoryStore.GetAllAsync(userId, new MemoryFilterOptions { Limit = 50 }, cancellationToken);
        return await SynthesizeQuestionsFromMemoriesAsync(memories, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryLink>> DiscoverLinksAsync(
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        var memory = await _memoryStore.GetByIdAsync(memoryId, cancellationToken);
        if (memory == null)
            return [];

        // Get other memories to compare with
        var allMemories = await _memoryStore.GetAllAsync(memory.UserId, new MemoryFilterOptions { Limit = 100 }, cancellationToken);
        var memories = allMemories.Where(m => m.Id != memoryId).ToList();
        memories.Add(memory);

        return await DiscoverLinksAsync(memories, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryLink>> DiscoverLinksAsync(
        IReadOnlyList<MemoryUnit> memories,
        LinkDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LinkDiscoveryOptions();
        var links = new List<MemoryLink>();

        if (memories.Count < 2)
            return links;

        // Entity-based links
        if (options.FindEntityLinks)
        {
            var entityLinks = await DiscoverEntityLinksAsync(memories, cancellationToken);
            links.AddRange(entityLinks);
        }

        // Temporal links
        if (options.FindTemporalLinks)
        {
            var temporalLinks = DiscoverTemporalLinks(memories);
            links.AddRange(temporalLinks);
        }

        // Semantic links
        if (options.FindSemanticLinks)
        {
            var semanticLinks = await DiscoverSemanticLinksAsync(memories, options.MinSimilarity, cancellationToken);
            links.AddRange(semanticLinks);
        }

        // Deduplicate and limit
        var uniqueLinks = links
            .GroupBy(l => (Math.Min(l.SourceMemoryId.GetHashCode(), l.TargetMemoryId.GetHashCode()),
                          Math.Max(l.SourceMemoryId.GetHashCode(), l.TargetMemoryId.GetHashCode())))
            .Select(g => g.OrderByDescending(l => l.Strength).First())
            .OrderByDescending(l => l.Strength)
            .Take(options.MaxLinks)
            .ToList();

        _logger.LogDebug("Discovered {Count} links from {MemoryCount} memories",
            uniqueLinks.Count, memories.Count);

        return uniqueLinks;
    }

    /// <inheritdoc />
    public async Task<MemoryActivitySummary> SummarizeActivityAsync(
        string userId,
        TimeSpan? timeWindow = null,
        CancellationToken cancellationToken = default)
    {
        timeWindow ??= TimeSpan.FromDays(1);
        var cutoff = DateTime.UtcNow - timeWindow.Value;

        var memories = await _memoryStore.GetAllAsync(userId, new MemoryFilterOptions { Limit = 1000 }, cancellationToken);
        var recentMemories = memories.Where(m => m.CreatedAt >= cutoff).ToList();

        var typeDistribution = recentMemories
            .GroupBy(m => m.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var entityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var memory in recentMemories)
        {
            foreach (var entity in ExtractEntities(memory.Content))
            {
                if (!entityCounts.ContainsKey(entity))
                    entityCounts[entity] = 0;
                entityCounts[entity]++;
            }
        }

        var avgImportance = 0f;
        foreach (var memory in recentMemories)
        {
            avgImportance += _scoringService.CalculateScore(memory);
        }
        avgImportance = recentMemories.Count > 0 ? avgImportance / recentMemories.Count : 0;

        var topEntities = entityCounts
            .OrderByDescending(kv => kv.Value)
            .Take(10)
            .Select(kv => new EntityActivity
            {
                Entity = kv.Key,
                MentionCount = kv.Value,
                MemoryCount = recentMemories.Count(m => m.Content.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            })
            .ToList();

        var summary = new MemoryActivitySummary
        {
            Period = timeWindow.Value,
            MemoriesCreated = recentMemories.Count,
            MemoriesAccessed = 0, // Would need access tracking
            MemoriesUpdated = recentMemories.Count(m => m.UpdatedAt > m.CreatedAt),
            TopTopics = [], // Would need topic classification
            TopEntities = topEntities,
            TypeDistribution = typeDistribution,
            AverageImportance = avgImportance,
            TextSummary = GenerateActivitySummaryText(recentMemories, timeWindow.Value, topEntities)
        };

        return summary;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ReflectionRecord>> GetReflectionHistoryAsync(
        string userId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_reflectionHistory.TryGetValue(userId, out var history))
        {
            return Task.FromResult<IReadOnlyList<ReflectionRecord>>([]);
        }

        var records = history
            .OrderByDescending(r => r.ReflectedAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<ReflectionRecord>>(records);
    }

    #region Private Helper Methods

    private static float GetStabilityScore(MemoryStability stability)
    {
        return stability switch
        {
            MemoryStability.Permanent => 1.0f,
            MemoryStability.Consolidated => 0.9f,
            MemoryStability.Stable => 0.8f,
            MemoryStability.Stabilizing => 0.6f,
            MemoryStability.Volatile => 0.4f,
            _ => 0.5f
        };
    }

    private async Task<IReadOnlyList<MemoryUnit>> GetMemoriesForReflectionAsync(
        string userId,
        ReflectionOptions options,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - options.TimeWindow;

        // Get all memories and filter
        var memories = await _memoryStore.GetAllAsync(userId, new MemoryFilterOptions { Limit = options.MaxMemories }, cancellationToken);

        // Filter by time window and types
        var filtered = memories
            .Where(m => m.CreatedAt >= cutoff)
            .Where(m => options.IncludeTypes == null || options.IncludeTypes.Contains(m.Type))
            .Take(MaxMemoriesPerReflection)
            .ToList();

        return filtered;
    }

    private async Task<IReadOnlyList<MemoryInsight>> GenerateGeneralizationsAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        var insights = new List<MemoryInsight>();

        // Group by type and find common themes
        var byType = memories.GroupBy(m => m.Type);

        foreach (var group in byType)
        {
            if (group.Count() < 3)
                continue;

            var entities = group
                .SelectMany(m => ExtractEntities(m.Content))
                .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() >= 2)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => g.Key)
                .ToList();

            if (entities.Count > 0)
            {
                insights.Add(new MemoryInsight
                {
                    Content = $"Multiple {group.Key} memories reference: {string.Join(", ", entities)}",
                    Type = InsightType.Generalization,
                    Confidence = Math.Min(entities.Count / 5f, 1f),
                    SourceMemoryIds = group.Select(m => m.Id).ToList(),
                    RelatedEntities = entities,
                    ImportanceScore = 0.6f
                });
            }
        }

        return insights;
    }

    private async Task<IReadOnlyList<MemoryInsight>> FindConnectionsAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        var insights = new List<MemoryInsight>();

        // Find entity co-occurrences across memories
        var entityMemories = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var memory in memories)
        {
            foreach (var entity in ExtractEntities(memory.Content))
            {
                if (!entityMemories.ContainsKey(entity))
                    entityMemories[entity] = [];
                entityMemories[entity].Add(memory.Id);
            }
        }

        // Find entities that appear in different contexts
        var frequentEntities = entityMemories
            .Where(kv => kv.Value.Count >= 3)
            .OrderByDescending(kv => kv.Value.Count)
            .Take(5);

        foreach (var (entity, memoryIds) in frequentEntities)
        {
            var relatedMemories = memories.Where(m => memoryIds.Contains(m.Id)).ToList();
            var types = relatedMemories.Select(m => m.Type).Distinct().ToList();

            if (types.Count > 1)
            {
                insights.Add(new MemoryInsight
                {
                    Content = $"'{entity}' appears across different memory types: {string.Join(", ", types)}",
                    Type = InsightType.Connection,
                    Confidence = 0.7f,
                    SourceMemoryIds = memoryIds,
                    RelatedEntities = [entity],
                    ImportanceScore = 0.5f
                });
            }
        }

        return insights;
    }

    private async Task<IReadOnlyList<MemoryInsight>> IdentifyTrendsAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        var insights = new List<MemoryInsight>();

        // Sort by time and look for temporal patterns
        var ordered = memories.OrderBy(m => m.CreatedAt).ToList();

        if (ordered.Count < 5)
            return insights;

        // Split into time buckets and look for changes
        var midpoint = ordered.Count / 2;
        var earlyEntities = ordered.Take(midpoint)
            .SelectMany(m => ExtractEntities(m.Content))
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var lateEntities = ordered.Skip(midpoint)
            .SelectMany(m => ExtractEntities(m.Content))
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // Find emerging entities
        foreach (var (entity, count) in lateEntities)
        {
            var earlyCount = earlyEntities.GetValueOrDefault(entity, 0);
            if (count > earlyCount * 2 && count >= 3)
            {
                insights.Add(new MemoryInsight
                {
                    Content = $"Increasing mentions of '{entity}' over time ({earlyCount} → {count})",
                    Type = InsightType.Trend,
                    Confidence = 0.6f,
                    SourceMemoryIds = ordered.Where(m => m.Content.Contains(entity, StringComparison.OrdinalIgnoreCase))
                        .Select(m => m.Id).ToList(),
                    RelatedEntities = [entity],
                    ImportanceScore = 0.6f
                });
            }
        }

        return insights;
    }

    private Task<MemoryInsight?> GenerateTopicSummaryAsync(
        IReadOnlyList<MemoryUnit> memories,
        string topic,
        CancellationToken cancellationToken)
    {
        var relevant = memories
            .Where(m => m.Content.Contains(topic, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (relevant.Count < 2)
            return Task.FromResult<MemoryInsight?>(null);

        var types = relevant.Select(m => m.Type).Distinct().ToList();
        var entities = relevant
            .SelectMany(m => ExtractEntities(m.Content))
            .Where(e => !e.Equals(topic, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => g.Key)
            .ToList();

        var insight = new MemoryInsight
        {
            Content = $"Topic '{topic}': {relevant.Count} memories, types: {string.Join(", ", types)}, " +
                     $"related: {string.Join(", ", entities)}",
            Type = InsightType.Summary,
            Confidence = 0.8f,
            SourceMemoryIds = relevant.Select(m => m.Id).ToList(),
            RelatedEntities = entities,
            ImportanceScore = 0.7f
        };

        return Task.FromResult<MemoryInsight?>(insight);
    }

    private async Task<IReadOnlyList<SynthesizedQuestion>> SynthesizeQuestionsFromMemoriesAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        var questions = new List<SynthesizedQuestion>();

        // Find entities with limited information
        var entityMentions = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var memory in memories)
        {
            foreach (var entity in ExtractEntities(memory.Content))
            {
                if (!entityMentions.ContainsKey(entity))
                    entityMentions[entity] = [];
                entityMentions[entity].Add(memory.Id);
            }
        }

        // Generate clarification questions for frequently mentioned entities
        foreach (var (entity, memoryIds) in entityMentions.OrderByDescending(kv => kv.Value.Count).Take(5))
        {
            if (memoryIds.Count >= 2 && memoryIds.Count <= 5)
            {
                questions.Add(new SynthesizedQuestion
                {
                    Question = $"What else should I know about {entity}?",
                    Rationale = $"'{entity}' appears in {memoryIds.Count} memories but information may be incomplete",
                    RelatedEntities = [entity],
                    SourceMemoryIds = memoryIds,
                    Priority = Math.Min(memoryIds.Count / 5f, 1f),
                    Category = QuestionCategory.Elaboration
                });
            }
        }

        return questions;
    }

    private async Task<IReadOnlyList<MemoryLink>> DiscoverEntityLinksAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        var links = new List<MemoryLink>();
        var entityMemories = new Dictionary<string, List<MemoryUnit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var memory in memories)
        {
            foreach (var entity in ExtractEntities(memory.Content))
            {
                if (!entityMemories.ContainsKey(entity))
                    entityMemories[entity] = [];
                entityMemories[entity].Add(memory);
            }
        }

        // Create links between memories sharing entities
        foreach (var (entity, memoryList) in entityMemories)
        {
            if (memoryList.Count < 2)
                continue;

            for (var i = 0; i < memoryList.Count; i++)
            {
                for (var j = i + 1; j < memoryList.Count; j++)
                {
                    links.Add(new MemoryLink
                    {
                        SourceMemoryId = memoryList[i].Id,
                        TargetMemoryId = memoryList[j].Id,
                        Type = LinkType.Entity,
                        Strength = 0.7f,
                        Reason = $"Both mention '{entity}'",
                        IsBidirectional = true
                    });
                }
            }
        }

        return links;
    }

    private static IReadOnlyList<MemoryLink> DiscoverTemporalLinks(IReadOnlyList<MemoryUnit> memories)
    {
        var links = new List<MemoryLink>();
        var ordered = memories.OrderBy(m => m.CreatedAt).ToList();

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var current = ordered[i];
            var next = ordered[i + 1];
            var timeDiff = next.CreatedAt - current.CreatedAt;

            // Link memories created within 1 hour of each other
            if (timeDiff <= TimeSpan.FromHours(1))
            {
                links.Add(new MemoryLink
                {
                    SourceMemoryId = current.Id,
                    TargetMemoryId = next.Id,
                    Type = LinkType.Temporal,
                    Strength = 1.0f - (float)(timeDiff.TotalMinutes / 60),
                    Reason = $"Created {timeDiff.TotalMinutes:F0} minutes apart",
                    IsBidirectional = false
                });
            }
        }

        return links;
    }

    private async Task<IReadOnlyList<MemoryLink>> DiscoverSemanticLinksAsync(
        IReadOnlyList<MemoryUnit> memories,
        float minSimilarity,
        CancellationToken cancellationToken)
    {
        var links = new List<MemoryLink>();

        // Simple word overlap similarity
        for (var i = 0; i < memories.Count; i++)
        {
            for (var j = i + 1; j < memories.Count; j++)
            {
                var similarity = CalculateTextSimilarity(memories[i].Content, memories[j].Content);
                if (similarity >= minSimilarity)
                {
                    links.Add(new MemoryLink
                    {
                        SourceMemoryId = memories[i].Id,
                        TargetMemoryId = memories[j].Id,
                        Type = LinkType.Semantic,
                        Strength = similarity,
                        Reason = $"Content similarity: {similarity:F2}",
                        IsBidirectional = true
                    });
                }
            }
        }

        return links;
    }

    private async Task<IReadOnlyList<MemoryPattern>> IdentifyPatternsAsync(
        IReadOnlyList<MemoryUnit> memories,
        CancellationToken cancellationToken)
    {
        var patterns = new List<MemoryPattern>();

        // Find recurring topics
        var entityCounts = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        foreach (var memory in memories)
        {
            foreach (var entity in ExtractEntities(memory.Content))
            {
                if (!entityCounts.ContainsKey(entity))
                    entityCounts[entity] = [];
                entityCounts[entity].Add(memory.Id);
            }
        }

        foreach (var (entity, memoryIds) in entityCounts.Where(kv => kv.Value.Count >= 5))
        {
            patterns.Add(new MemoryPattern
            {
                Description = $"Recurring topic: '{entity}' appears in {memoryIds.Count} memories",
                Type = PatternType.RecurringTopic,
                MemoryIds = memoryIds,
                Confidence = Math.Min(memoryIds.Count / 10f, 1f),
                Frequency = memoryIds.Count,
                RelatedEntities = [entity]
            });
        }

        // Find entity co-occurrences
        foreach (var (entity1, ids1) in entityCounts.Take(10))
        {
            foreach (var (entity2, ids2) in entityCounts.Where(kv => !kv.Key.Equals(entity1, StringComparison.OrdinalIgnoreCase)).Take(10))
            {
                var overlap = ids1.Intersect(ids2).ToList();
                if (overlap.Count >= 3)
                {
                    patterns.Add(new MemoryPattern
                    {
                        Description = $"'{entity1}' and '{entity2}' frequently appear together",
                        Type = PatternType.EntityCooccurrence,
                        MemoryIds = overlap,
                        Confidence = (float)overlap.Count / Math.Max(ids1.Count, ids2.Count),
                        Frequency = overlap.Count,
                        RelatedEntities = [entity1, entity2]
                    });
                }
            }
        }

        return patterns.OrderByDescending(p => p.Confidence).Take(10).ToList();
    }

    private async Task<Guid?> StoreInsightAsMemoryAsync(
        string userId,
        MemoryInsight insight,
        CancellationToken cancellationToken)
    {
        try
        {
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Content = $"[Insight] {insight.Content}",
                Type = MemoryType.Semantic,
                Tier = MemoryTier.Session,
                Stability = MemoryStability.Stable,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _memoryStore.StoreAsync(memory, cancellationToken);
            return memory.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store insight as memory");
            return null;
        }
    }

    private static string GenerateReflectionSummary(ReflectionResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Reflected on {result.ReflectedMemoryIds.Count} memories.");

        if (result.Insights.Count > 0)
            sb.AppendLine($"Generated {result.Insights.Count} insights.");

        if (result.DiscoveredLinks.Count > 0)
            sb.AppendLine($"Discovered {result.DiscoveredLinks.Count} memory links.");

        if (result.Patterns.Count > 0)
            sb.AppendLine($"Identified {result.Patterns.Count} patterns.");

        if (result.Questions.Count > 0)
            sb.AppendLine($"Synthesized {result.Questions.Count} questions.");

        if (result.CreatedMemoryIds.Count > 0)
            sb.AppendLine($"Created {result.CreatedMemoryIds.Count} new insight memories.");

        return sb.ToString().TrimEnd();
    }

    private static string GenerateActivitySummaryText(
        IReadOnlyList<MemoryUnit> memories,
        TimeSpan period,
        IReadOnlyList<EntityActivity> topEntities)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"In the last {period.TotalHours:F0} hours:");
        sb.AppendLine($"- Created {memories.Count} memories");

        if (topEntities.Count > 0)
        {
            sb.AppendLine($"- Top topics: {string.Join(", ", topEntities.Take(3).Select(e => e.Entity))}");
        }

        return sb.ToString().TrimEnd();
    }

    private Task RecordReflectionAsync(string userId, ReflectionResult result, CancellationToken cancellationToken)
    {
        var record = new ReflectionRecord
        {
            Id = result.Id,
            ReflectedAt = result.ReflectedAt,
            MemoryCount = result.ReflectedMemoryIds.Count,
            InsightCount = result.Insights.Count,
            LinkCount = result.DiscoveredLinks.Count,
            Summary = result.Summary,
            Duration = result.Duration
        };

        var history = _reflectionHistory.GetOrAdd(userId, _ => new ConcurrentQueue<ReflectionRecord>());
        history.Enqueue(record);

        while (history.Count > MaxHistoryPerUser)
        {
            history.TryDequeue(out _);
        }

        return Task.CompletedTask;
    }

    private static float CalculateReflectionPriority(float importance, int memoryCount, TimeSpan timeSinceLastReflection)
    {
        var importanceFactor = Math.Min(importance / ImportanceThreshold, 1f);
        var countFactor = Math.Min(memoryCount / 50f, 1f);
        var timeFactor = Math.Min((float)timeSinceLastReflection.TotalHours / 24, 1f);

        return (importanceFactor * 0.4f + countFactor * 0.3f + timeFactor * 0.3f);
    }

    private static IEnumerable<string> ExtractEntities(string content)
    {
        if (string.IsNullOrEmpty(content))
            yield break;

        var words = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            var cleaned = word.Trim('.', ',', '!', '?', '"', '\'', ':', ';', '(', ')');
            if (cleaned.Length > 2 && char.IsUpper(cleaned[0]) &&
                !IsCommonWord(cleaned))
            {
                yield return cleaned;
            }
        }
    }

    private static bool IsCommonWord(string word)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "The", "This", "That", "These", "Those", "What", "Where", "When",
            "Why", "How", "Who", "Which", "There", "Here", "Today", "Yesterday",
            "Tomorrow", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday",
            "Saturday", "Sunday", "January", "February", "March", "April", "May",
            "June", "July", "August", "September", "October", "November", "December"
        };
        return common.Contains(word);
    }

    private static float CalculateTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return 0;

        var words1 = text1.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var words2 = text2.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        var intersection = words1.Intersect(words2).Count();
        var union = words1.Union(words2).Count();

        return union > 0 ? (float)intersection / union : 0;
    }

    private static int EstimateTokens(IReadOnlyList<MemoryUnit> memories)
    {
        return memories.Sum(m => m.Content?.Length ?? 0) / 4;
    }

    #endregion
}
