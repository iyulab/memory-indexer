using System.Collections.Concurrent;
using System.Diagnostics;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Inference;

/// <summary>
/// Service for deriving new facts from existing facts through inference.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public partial class FactInferenceService : IFactInferenceEngine
{
    private readonly IArchiveStore _archiveStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<FactInferenceService> _logger;
    private readonly ConcurrentDictionary<string, IInferenceRule> _rules = new();

    /// <summary>
    /// Semantic similarity threshold for inference.
    /// </summary>
    public const float DefaultSemanticThreshold = 0.7f;

    /// <summary>
    /// Minimum facts needed for generalization inference.
    /// </summary>
    public const int MinFactsForGeneralization = 3;

    public FactInferenceService(
        IArchiveStore archiveStore,
        IEmbeddingService embeddingService,
        ILogger<FactInferenceService> logger)
    {
        _archiveStore = archiveStore;
        _embeddingService = embeddingService;
        _logger = logger;

        // Register built-in rules
        RegisterBuiltInRules();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InferredFact>> InferAsync(
        IReadOnlyList<SemanticStoreEntry> facts,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new InferenceOptions();
        var inferredFacts = new List<InferredFact>();

        // Filter facts by minimum confidence
        var qualifiedFacts = facts
            .Where(f => f.IsActive && f.Confidence >= options.MinSourceConfidence)
            .ToList();

        if (qualifiedFacts.Count < 2)
        {
            LogEnoughQualifiedFactsInferenceCount(_logger, qualifiedFacts.Count);
            return inferredFacts;
        }

        // Run enabled inference types
        var enabledTypes = options.EnabledTypes ?? Enum.GetValues<InferenceType>();

        foreach (var inferenceType in enabledTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var typeInferences = inferenceType switch
            {
                InferenceType.CoOccurrence => await InferCoOccurrenceAsync(qualifiedFacts, options, cancellationToken),
                InferenceType.Semantic => await InferSemanticAsync(qualifiedFacts, options, cancellationToken),
                InferenceType.Generalization => await InferGeneralizationAsync(qualifiedFacts, options, cancellationToken),
                InferenceType.Negation => InferNegation(qualifiedFacts, options),
                InferenceType.Custom => await InferCustomRulesAsync(qualifiedFacts, options, cancellationToken),
                _ => []
            };

            inferredFacts.AddRange(typeInferences);
        }

        // Filter by minimum inference confidence and limit results
        return inferredFacts
            .Where(f => f.Confidence >= options.MinInferenceConfidence)
            .OrderByDescending(f => f.Confidence)
            .Take(options.MaxResults)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<InferenceResult> InferForUserAsync(
        string userId,
        InferenceOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        options ??= new InferenceOptions();

        // Get all facts for user
        var allFacts = await _archiveStore.GetAllAsync(userId, cancellationToken);

        // Filter by categories if specified
        var filteredFacts = allFacts.AsEnumerable();
        if (options.IncludeCategories != null)
        {
            var categorySet = options.IncludeCategories
                .Select(MapFactCategoryToStoreCategory)
                .ToHashSet();
            filteredFacts = filteredFacts.Where(f => categorySet.Contains(f.Category));
        }

        var factList = filteredFacts.ToList();

        // Run inference
        var inferred = await InferAsync(factList, options, cancellationToken);

        // Auto-store if enabled
        var storedCount = 0;
        if (options.AutoStore)
        {
            foreach (var fact in inferred.Where(f => f.Confidence >= options.AutoStoreThreshold && f.ShouldStore))
            {
                try
                {
                    var entry = new SemanticStoreEntry
                    {
                        Key = $"inferred:{fact.Category.ToString().ToLowerInvariant()}:{Guid.NewGuid():N}",
                        Value = fact.Content,
                        Category = MapFactCategoryToStoreCategory(fact.Category),
                        Confidence = fact.Confidence,
                        Metadata = new Dictionary<string, string>
                        {
                            ["inference_type"] = fact.Type.ToString(),
                            ["rule_name"] = fact.RuleName ?? "unknown",
                            ["source_keys"] = string.Join(",", fact.SourceFactKeys)
                        }
                    };

                    await _archiveStore.SetAsync(userId, entry, cancellationToken);
                    storedCount++;
                }
                catch (Exception ex)
                {
                    LogFailedStoreInferredFactContent(_logger, ex, fact.Content);
                }
            }
        }

        sw.Stop();

        // Build statistics
        var byType = inferred
            .GroupBy(f => f.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        var byCategory = inferred
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        return new InferenceResult
        {
            UserId = userId,
            InferredFacts = inferred,
            SourceFactCount = factList.Count,
            StoredCount = storedCount,
            ByType = byType,
            ByCategory = byCategory,
            ProcessingTimeMs = sw.ElapsedMilliseconds
        };
    }

    /// <inheritdoc />
    public void RegisterRule(IInferenceRule rule)
    {
        if (_rules.TryAdd(rule.Name, rule))
        {
            LogRegisteredInferenceRuleName(_logger, rule.Name);
        }
        else
        {
            LogInferenceRuleAlreadyRegisteredName(_logger, rule.Name);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IInferenceRule> GetRegisteredRules()
    {
        return _rules.Values.ToList();
    }

    #region Inference Methods

    private static async Task<IReadOnlyList<InferredFact>> InferCoOccurrenceAsync(
        IReadOnlyList<SemanticStoreEntry> facts,
        InferenceOptions options,
        CancellationToken cancellationToken)
    {
        var inferred = new List<InferredFact>();

        // Group facts by category for co-occurrence analysis
        var byCategory = facts.GroupBy(f => f.Category).ToDictionary(g => g.Key, g => g.ToList());

        // Example: Location + Work → Commute inference
        if (byCategory.TryGetValue(SemanticStoreCategory.Work, out var workFacts) &&
            byCategory.TryGetValue(SemanticStoreCategory.Fact, out var locationFacts))
        {
            var workLocations = workFacts.Where(f => ContainsLocationHint(f.Value)).ToList();
            var residenceFacts = locationFacts.Where(f => ContainsResidenceHint(f.Value)).ToList();

            foreach (var work in workLocations)
            {
                foreach (var residence in residenceFacts)
                {
                    var workLocation = ExtractLocation(work.Value);
                    var residenceLocation = ExtractLocation(residence.Value);

                    if (!string.IsNullOrEmpty(workLocation) && !string.IsNullOrEmpty(residenceLocation))
                    {
                        // Same location = likely commutes locally
                        if (string.Equals(workLocation, residenceLocation, StringComparison.OrdinalIgnoreCase))
                        {
                            inferred.Add(new InferredFact
                            {
                                Content = $"Commutes within {workLocation}",
                                Category = FactCategory.Location,
                                Type = InferenceType.CoOccurrence,
                                Confidence = Math.Min(work.Confidence, residence.Confidence) * 0.8f,
                                RuleName = "LocationWorkCoOccurrence",
                                SourceFactKeys = [work.Key, residence.Key],
                                Explanation = $"Works and lives in {workLocation}",
                                ShouldStore = true,
                                Triple = new InferredTriple
                                {
                                    Subject = "user",
                                    Predicate = "commutes in",
                                    Object = workLocation
                                }
                            });
                        }
                    }
                }
            }
        }

        // Preference co-occurrence: Multiple similar preferences → general interest
        if (byCategory.TryGetValue(SemanticStoreCategory.Preference, out var preferences) && preferences.Count >= 3)
        {
            // Group preferences by topic keywords
            var topicGroups = GroupByTopics(preferences);
            foreach (var (topic, group) in topicGroups.Where(g => g.Value.Count >= 2))
            {
                var avgConfidence = group.Average(f => f.Confidence);
                inferred.Add(new InferredFact
                {
                    Content = $"Has a general interest in {topic}",
                    Category = FactCategory.Preference,
                    Type = InferenceType.CoOccurrence,
                    Confidence = avgConfidence * 0.75f,
                    RuleName = "PreferenceTopicCoOccurrence",
                    SourceFactKeys = group.Select(f => f.Key).ToList(),
                    Explanation = $"Multiple preferences related to {topic}",
                    ShouldStore = false // Don't auto-store generalizations
                });
            }
        }

        return inferred;
    }

    private static async Task<IReadOnlyList<InferredFact>> InferSemanticAsync(
        IReadOnlyList<SemanticStoreEntry> facts,
        InferenceOptions options,
        CancellationToken cancellationToken)
    {
        var inferred = new List<InferredFact>();

        // Get facts with embeddings
        var factsWithEmbeddings = facts.Where(f => f.Embedding != null).ToList();
        if (factsWithEmbeddings.Count < 2) return inferred;

        // Find semantically similar fact clusters
        var clusters = ClusterBySimilarity(factsWithEmbeddings, DefaultSemanticThreshold);

        foreach (var cluster in clusters.Where(c => c.Count >= 2))
        {
            var avgConfidence = cluster.Average(f => f.Confidence);
            var categories = cluster.Select(f => f.Category).Distinct().ToList();

            // Cross-category similarity might indicate hidden relationship
            if (categories.Count > 1)
            {
                inferred.Add(new InferredFact
                {
                    Content = $"Related concepts: {string.Join(", ", cluster.Take(3).Select(f => TruncateContent(f.Value, 50)))}",
                    Category = FactCategory.General,
                    Type = InferenceType.Semantic,
                    Confidence = avgConfidence * 0.6f,
                    RuleName = "SemanticCrossCategory",
                    SourceFactKeys = cluster.Select(f => f.Key).ToList(),
                    Explanation = $"Semantically similar facts across {string.Join(", ", categories)}",
                    ShouldStore = false
                });
            }
        }

        return inferred;
    }

    private static Task<IReadOnlyList<InferredFact>> InferGeneralizationAsync(
        IReadOnlyList<SemanticStoreEntry> facts,
        InferenceOptions options,
        CancellationToken cancellationToken)
    {
        var inferred = new List<InferredFact>();

        // Count category occurrences
        var categoryStats = facts
            .GroupBy(f => f.Category)
            .Select(g => new { Category = g.Key, Count = g.Count(), AvgConfidence = g.Average(f => f.Confidence) })
            .OrderByDescending(s => s.Count)
            .ToList();

        // If user has many facts in a category, generalize their profile
        foreach (var stat in categoryStats.Where(s => s.Count >= MinFactsForGeneralization))
        {
            var categoryName = stat.Category.ToString().ToLowerInvariant();
            var description = stat.Category switch
            {
                SemanticStoreCategory.Preference => "Has diverse preferences",
                SemanticStoreCategory.Skill => "Has multiple skills",
                SemanticStoreCategory.Interest => "Has varied interests",
                SemanticStoreCategory.Work => "Has professional experience",
                SemanticStoreCategory.Goal => "Has multiple goals",
                SemanticStoreCategory.Relationship => "Has social connections",
                _ => $"Has extensive {categoryName} profile"
            };

            inferred.Add(new InferredFact
            {
                Content = description,
                Category = FactCategory.General,
                Type = InferenceType.Generalization,
                Confidence = Math.Min(stat.AvgConfidence, 0.7f),
                RuleName = "CategoryGeneralization",
                SourceFactKeys = facts
                    .Where(f => f.Category == stat.Category)
                    .Take(5)
                    .Select(f => f.Key)
                    .ToList(),
                Explanation = $"User has {stat.Count} facts in {categoryName} category",
                ShouldStore = false
            });
        }

        return Task.FromResult<IReadOnlyList<InferredFact>>(inferred);
    }

    private static List<InferredFact> InferNegation(
        IReadOnlyList<SemanticStoreEntry> facts,
        InferenceOptions options)
    {
        var inferred = new List<InferredFact>();

        // Find negation patterns (dislikes, doesn't, never, etc.)
        var negationKeywords = new[] { "dislike", "doesn't", "don't", "never", "hate", "avoid", "not " };

        foreach (var fact in facts)
        {
            var lowerValue = fact.Value.ToLowerInvariant();
            if (negationKeywords.Any(n => lowerValue.Contains(n)))
            {
                // Extract what is negated
                var negatedSubject = ExtractNegatedSubject(fact.Value);
                if (!string.IsNullOrEmpty(negatedSubject))
                {
                    inferred.Add(new InferredFact
                    {
                        Content = $"Prefers to avoid {negatedSubject}",
                        Category = FactCategory.Preference,
                        Type = InferenceType.Negation,
                        Confidence = fact.Confidence * 0.7f,
                        RuleName = "NegationPattern",
                        SourceFactKeys = [fact.Key],
                        Explanation = $"Negation detected in: {TruncateContent(fact.Value, 50)}",
                        ShouldStore = false
                    });
                }
            }
        }

        return inferred;
    }

    private async Task<IReadOnlyList<InferredFact>> InferCustomRulesAsync(
        IReadOnlyList<SemanticStoreEntry> facts,
        InferenceOptions options,
        CancellationToken cancellationToken)
    {
        var allInferred = new List<InferredFact>();

        foreach (var rule in _rules.Values)
        {
            try
            {
                var ruleInferred = await rule.EvaluateAsync(facts, cancellationToken);
                allInferred.AddRange(ruleInferred);
            }
            catch (Exception ex)
            {
                LogCustomRuleNameFailed(_logger, ex, rule.Name);
            }
        }

        return allInferred;
    }

    #endregion

    #region Helper Methods

    private static void RegisterBuiltInRules()
    {
        // Built-in rules can be added here
        // Example: RegisterRule(new AgeInferenceRule());
    }

    private static bool ContainsLocationHint(string value)
    {
        var locationKeywords = new[] { "works at", "works in", "employed at", "office in", "based in" };
        return locationKeywords.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsResidenceHint(string value)
    {
        var residenceKeywords = new[] { "lives in", "resides in", "from", "home in", "located in" };
        return residenceKeywords.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractLocation(string value)
    {
        // Simple extraction - in production, use NER
        var locationPrepositions = new[] { " in ", " at ", " from " };
        foreach (var prep in locationPrepositions)
        {
            var idx = value.IndexOf(prep, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = value[(idx + prep.Length)..].Trim();
                var endIdx = rest.IndexOfAny([',', '.', ';', '!', '?']);
                return endIdx > 0 ? rest[..endIdx].Trim() : rest.Split(' ').FirstOrDefault();
            }
        }
        return null;
    }

    private static Dictionary<string, List<SemanticStoreEntry>> GroupByTopics(IEnumerable<SemanticStoreEntry> facts)
    {
        var topics = new Dictionary<string, List<SemanticStoreEntry>>(StringComparer.OrdinalIgnoreCase);

        // Simple topic extraction - split by common words
        foreach (var fact in facts)
        {
            var words = fact.Value
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 4)
                .Select(w => w.ToLowerInvariant().Trim(',', '.', '!', '?'));

            foreach (var word in words)
            {
                if (!topics.ContainsKey(word))
                    topics[word] = [];
                topics[word].Add(fact);
            }
        }

        return topics;
    }

    private static List<List<SemanticStoreEntry>> ClusterBySimilarity(
        List<SemanticStoreEntry> facts,
        float threshold)
    {
        var clusters = new List<List<SemanticStoreEntry>>();
        var assigned = new HashSet<string>();

        foreach (var fact in facts)
        {
            if (assigned.Contains(fact.Key)) continue;

            var cluster = new List<SemanticStoreEntry> { fact };
            assigned.Add(fact.Key);

            foreach (var other in facts)
            {
                if (assigned.Contains(other.Key)) continue;
                if (fact.Embedding == null || other.Embedding == null) continue;

                var similarity = CosineSimilarity(fact.Embedding.Value.Span, other.Embedding.Value.Span);
                if (similarity >= threshold)
                {
                    cluster.Add(other);
                    assigned.Add(other.Key);
                }
            }

            if (cluster.Count > 1)
                clusters.Add(cluster);
        }

        return clusters;
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length) return 0;

        float dotProduct = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator > 0 ? dotProduct / denominator : 0;
    }

    private static string? ExtractNegatedSubject(string value)
    {
        // Simple extraction of what's being negated
        var patterns = new[]
        {
            ("dislikes ", 9),
            ("doesn't like ", 13),
            ("don't like ", 11),
            ("never ", 6),
            ("hates ", 6),
            ("avoids ", 7)
        };

        foreach (var (pattern, length) in patterns)
        {
            var idx = value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = value[(idx + length)..].Trim();
                var endIdx = rest.IndexOfAny([',', '.', ';', '!', '?']);
                return endIdx > 0 ? rest[..endIdx].Trim() : rest.Split(' ').FirstOrDefault();
            }
        }

        return null;
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;
        return content[..(maxLength - 3)] + "...";
    }

    private static SemanticStoreCategory MapFactCategoryToStoreCategory(FactCategory category)
    {
        return category switch
        {
            FactCategory.Identity => SemanticStoreCategory.Fact,
            FactCategory.Preference => SemanticStoreCategory.Preference,
            FactCategory.Relationship => SemanticStoreCategory.Relationship,
            FactCategory.Location => SemanticStoreCategory.Fact,
            FactCategory.Temporal => SemanticStoreCategory.Fact,
            FactCategory.Skill => SemanticStoreCategory.Skill,
            FactCategory.Goal => SemanticStoreCategory.Goal,
            FactCategory.Health => SemanticStoreCategory.Fact,
            FactCategory.Professional => SemanticStoreCategory.Work,
            FactCategory.General => SemanticStoreCategory.Other,
            _ => SemanticStoreCategory.Other
        };
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Not enough qualified facts for inference: {Count}")]
    private static partial void LogEnoughQualifiedFactsInferenceCount(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to store inferred fact: {Content}")]
    private static partial void LogFailedStoreInferredFactContent(ILogger logger, Exception ex, string content);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Registered inference rule: {Name}")]
    private static partial void LogRegisteredInferenceRuleName(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Inference rule already registered: {Name}")]
    private static partial void LogInferenceRuleAlreadyRegisteredName(ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom rule {Name} failed")]
    private static partial void LogCustomRuleNameFailed(ILogger logger, Exception ex, string name);
}
