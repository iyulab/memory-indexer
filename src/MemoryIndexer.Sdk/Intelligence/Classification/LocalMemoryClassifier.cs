using System.Text.RegularExpressions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Classification;

/// <summary>
/// Memory classifier using enhanced heuristic rules with multi-label support.
/// </summary>
/// <remarks>
/// Phase 23.1: Enhanced with multi-score classification system to balance
/// memory type distribution (Episodic, Semantic, Procedural, Fact).
///
/// Classification improvements:
/// - Multi-label support (primary + secondary types)
/// - Expanded pattern detection (30+ procedural patterns, 20+ semantic patterns)
/// - Type-specific scoring algorithms
/// - Implicit procedural knowledge detection (tool usage, environment setup)
/// </remarks>
public sealed partial class LocalMemoryClassifier : IMemoryClassifier
{
    private readonly ILogger<LocalMemoryClassifier> _logger;
    private readonly IntelligenceOptions _options;

    #region Pattern Definitions

    /// <summary>
    /// Patterns that indicate factual content about the user.
    /// </summary>
    private static readonly string[] FactIndicators =
    [
        "my name is", "i am", "i'm", "i prefer", "i like", "i always",
        "i work", "i live", "my favorite",
        "my email", "my phone", "my address", "i was born"
    ];

    /// <summary>
    /// Patterns that indicate procedural/how-to content.
    /// Phase 23.1: Expanded from 8 to 30+ patterns.
    /// </summary>
    private static readonly string[] ProceduralIndicators =
    [
        // Explicit procedures
        "how to", "step by step", "first,", "then,", "finally,",
        "to do this", "you need to", "make sure to", "don't forget to",

        // Tool/framework usage (NEW)
        "use", "uses", "using", "built with", "configured with",
        "based on", "running on", "powered by", "depends on",
        "rely on", "leverage", "utilize",

        // Environment/setup (NEW)
        "installed", "set up", "deploy with", "package with",
        "initialize", "configure", "install", "setup",

        // Habitual patterns (NEW)
        "always", "usually", "typically", "generally", "normally",
        "prefer to", "tend to", "habit of", "practice of"
    ];

    /// <summary>
    /// Patterns that indicate semantic/conceptual content.
    /// Phase 23.1: Expanded from 4 to 20+ patterns.
    /// </summary>
    private static readonly string[] SemanticIndicators =
    [
        // Existing
        "means", "definition", "concept", "principle",

        // Knowledge/facts (NEW)
        "is a", "refers to", "defined as", "known as",
        "type of", "kind of", "category of", "class of",

        // Explanations (NEW)
        "because", "therefore", "thus", "hence", "consequently",
        "reason", "cause", "effect", "purpose"
    ];

    /// <summary>
    /// Patterns that indicate episodic/event-based content.
    /// Phase 23.1: NEW - explicit episodic markers.
    /// </summary>
    private static readonly string[] EpisodicIndicators =
    [
        // Time markers
        "yesterday", "today", "tomorrow", "last week", "next month",
        "ago", "recently", "previously", "earlier", "later",

        // Personal events
        "i did", "we went", "i saw", "i met", "i talked",
        "happened", "occurred", "took place", "experienced",

        // Location markers
        "at the", "in the", "where", "there", "here"
    ];

    /// <summary>
    /// Patterns that indicate transient/ephemeral content.
    /// </summary>
    private static readonly string[] TransientPatterns =
    [
        "hello", "hi", "hey", "thanks", "thank you", "ok", "okay",
        "yes", "no", "sure", "got it", "understood", "bye", "goodbye",
        "see you", "hmm", "um", "uh", "well", "cool", "great", "nice"
    ];

    /// <summary>
    /// Common tool/framework keywords for procedural detection.
    /// Phase 23.1: NEW - implicit procedural knowledge.
    /// </summary>
    private static readonly HashSet<string> ToolKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "react", "vue", "angular", "svelte",
        "docker", "kubernetes", "k8s",
        "pnpm", "npm", "yarn", "bun",
        "typescript", "javascript", "python", "rust", "go",
        "postgres", "mysql", "mongodb", "redis",
        "aws", "azure", "gcp", "vercel", "netlify"
    };

    /// <summary>
    /// Common topic keywords mapped to topics.
    /// </summary>
    private static readonly Dictionary<string, string> TopicKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["api"] = "api",
        ["database"] = "database",
        ["authentication"] = "security",
        ["auth"] = "security",
        ["login"] = "security",
        ["password"] = "security",
        ["code"] = "development",
        ["programming"] = "development",
        ["bug"] = "debugging",
        ["error"] = "debugging",
        ["test"] = "testing",
        ["deploy"] = "deployment",
        ["docker"] = "infrastructure",
        ["kubernetes"] = "infrastructure",
        ["k8s"] = "infrastructure",
        ["cloud"] = "infrastructure",
        ["aws"] = "cloud",
        ["azure"] = "cloud",
        ["gcp"] = "cloud",
        ["react"] = "frontend",
        ["vue"] = "frontend",
        ["angular"] = "frontend",
        ["css"] = "frontend",
        ["html"] = "frontend",
        ["python"] = "python",
        ["javascript"] = "javascript",
        ["typescript"] = "typescript",
        ["csharp"] = "dotnet",
        [".net"] = "dotnet",
        ["dotnet"] = "dotnet"
    };

    #endregion

    public LocalMemoryClassifier(
        IOptions<MemoryIndexerOptions> options,
        ILogger<LocalMemoryClassifier> logger)
    {
        _logger = logger;
        _options = options.Value.Intelligence;

        _logger.LogInformation("LocalMemoryClassifier initialized (Phase 23.1 multi-score mode)");
    }

    /// <inheritdoc />
    public Task<MemoryClassification> ClassifyAsync(
        string content,
        ClassificationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(MemoryClassification.Transient);
        }

        var classification = ClassifyHeuristic(content, context);

        _logger.LogDebug(
            "Classified: Tier={Tier}, Primary={Type}, Secondary=[{Secondary}], Importance={Importance:F2}",
            classification.Tier,
            classification.Type,
            string.Join(",", classification.SecondaryTypes),
            classification.Importance);

        return Task.FromResult(classification);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MemoryClassification>> ClassifyBatchAsync(
        IEnumerable<string> contents,
        ClassificationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<MemoryClassification>();

        foreach (var content in contents)
        {
            var classification = await ClassifyAsync(content, context, cancellationToken);
            results.Add(classification);
        }

        return results;
    }

    private MemoryClassification ClassifyHeuristic(string content, ClassificationContext? context)
    {
        var lower = content.ToLowerInvariant();
        var wordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        // Check for transient content first
        if (IsTransientContent(lower, wordCount))
        {
            return MemoryClassification.Transient;
        }

        // Phase 23.1: Multi-score classification
        var scores = CalculateTypeScores(lower, wordCount);

        // Primary type = highest score
        var primaryType = scores.OrderByDescending(x => x.Value).First().Key;

        // Secondary types = scores >= 0.3 (excluding primary)
        var secondaryTypes = scores
            .Where(x => x.Key != primaryType && x.Value >= 0.3f)
            .OrderByDescending(x => x.Value)
            .Select(x => x.Key)
            .ToList();

        // Determine tier based on primary type and content
        var tier = DetermineTier(lower, wordCount, primaryType, context);

        // Calculate importance
        var importance = CalculateImportance(lower, wordCount, primaryType, context);

        // Extract topics and entities
        var topics = ExtractTopics(lower);
        var entities = ExtractEntities(content);

        // Determine if should persist
        var shouldPersist = tier != MemoryTier.Working && importance >= 0.3f;

        return new MemoryClassification
        {
            Tier = tier,
            Type = primaryType,
            SecondaryTypes = secondaryTypes,
            TypeConfidences = scores,
            Importance = importance,
            Topics = topics,
            Entities = entities,
            ShouldPersist = shouldPersist,
            Confidence = CalculateOverallConfidence(scores),
            Reason = $"Multi-score: {primaryType}={scores[primaryType]:F2}, {wordCount} words"
        };
    }

    #region Phase 23.1: Multi-Score Classification

    private Dictionary<MemoryType, float> CalculateTypeScores(string lower, int wordCount)
    {
        return new Dictionary<MemoryType, float>
        {
            [MemoryType.Episodic] = CalculateEpisodicScore(lower, wordCount),
            [MemoryType.Semantic] = CalculateSemanticScore(lower, wordCount),
            [MemoryType.Procedural] = CalculateProceduralScore(lower, wordCount),
            [MemoryType.Fact] = CalculateFactScore(lower, wordCount)
        };
    }

    private float CalculateEpisodicScore(string lower, int wordCount)
    {
        float score = 0.2f; // Base score

        // Time/location markers (+0.3 each, max 0.6)
        int markerCount = EpisodicIndicators.Count(i => lower.Contains(i));
        score += Math.Min(markerCount * 0.3f, 0.6f);

        // Personal pronouns in past tense (+0.2)
        if ((lower.Contains("i ") || lower.Contains("we ")) &&
            (lower.Contains("did") || lower.Contains("was") || lower.Contains("were")))
        {
            score += 0.2f;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    private float CalculateSemanticScore(string lower, int wordCount)
    {
        float score = 0.1f;

        // Semantic indicators (+0.25 each, max 0.75)
        int count = SemanticIndicators.Count(i => lower.Contains(i));
        score += Math.Min(count * 0.25f, 0.75f);

        // Definition pattern: "X is a Y" (+0.3) - stronger weight for definitions
        if (Regex.IsMatch(lower, @"\b\w+ is a \w+"))
        {
            score += 0.3f;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    private float CalculateProceduralScore(string lower, int wordCount)
    {
        float score = 0.1f;

        // Procedural indicators (+0.2 each, max 0.6)
        int count = ProceduralIndicators.Count(i => lower.Contains(i));
        score += Math.Min(count * 0.2f, 0.6f);

        // Tool/framework keywords (+0.3 if present)
        if (ToolKeywords.Any(k => lower.Contains(k)))
        {
            score += 0.3f;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    private float CalculateFactScore(string lower, int wordCount)
    {
        // Fact indicators (+0.2 each)
        int count = FactIndicators.Count(i => lower.Contains(i));

        if (count == 0)
        {
            return 0.1f; // Base score
        }

        return Math.Clamp(0.6f + count * 0.1f, 0f, 1f);
    }

    private static float CalculateOverallConfidence(Dictionary<MemoryType, float> scores)
    {
        // Confidence = max score (higher max = more confident classification)
        var maxScore = scores.Values.Max();

        // If max score is low, confidence should be low
        // If max score is high, confidence should be high
        return Math.Clamp(maxScore * 0.9f, 0.5f, 1.0f);
    }

    #endregion

    #region Original Helper Methods

    private static bool IsTransientContent(string lower, int wordCount)
    {
        // Very short content that matches transient patterns
        if (wordCount <= 5)
        {
            foreach (var pattern in TransientPatterns)
            {
                if (lower.StartsWith(pattern) || lower == pattern || lower.EndsWith(pattern))
                {
                    return true;
                }
            }
        }

        // Single word responses
        if (wordCount == 1 && TransientPatterns.Contains(lower.Trim('!', '.', '?')))
        {
            return true;
        }

        return false;
    }

    private static MemoryTier DetermineTier(string lower, int wordCount, MemoryType type, ClassificationContext? context)
    {
        // Facts about user go to User tier
        if (type == MemoryType.Fact)
        {
            return MemoryTier.User;
        }

        // Long semantic content goes to User tier
        if (type == MemoryType.Semantic && wordCount > 50)
        {
            return MemoryTier.User;
        }

        // Procedural knowledge persists at Session or User level
        if (type == MemoryType.Procedural)
        {
            return wordCount > 100 ? MemoryTier.User : MemoryTier.Session;
        }

        // Short episodic content stays in Working memory
        if (wordCount < 20)
        {
            return MemoryTier.Working;
        }

        // Medium-length content goes to Session
        return MemoryTier.Session;
    }

    private static float CalculateImportance(string lower, int wordCount, MemoryType type, ClassificationContext? context)
    {
        var importance = 0.3f; // Base importance

        // Type-based adjustments
        importance += type switch
        {
            MemoryType.Fact => 0.3f,
            MemoryType.Procedural => 0.2f,
            MemoryType.Semantic => 0.15f,
            _ => 0f
        };

        // Length-based adjustment (longer = potentially more important)
        importance += Math.Min(wordCount * 0.005f, 0.2f);

        // Contains personal information
        if (lower.Contains("i ") || lower.Contains("my ") || lower.Contains("me "))
        {
            importance += 0.1f;
        }

        // Contains technical keywords
        if (TopicKeywords.Keys.Any(k => lower.Contains(k)))
        {
            importance += 0.05f;
        }

        return Math.Clamp(importance, 0f, 1f);
    }

    private static List<string> ExtractTopics(string lower)
    {
        var topics = new HashSet<string>();

        foreach (var (keyword, topic) in TopicKeywords)
        {
            if (lower.Contains(keyword))
            {
                topics.Add(topic);
            }
        }

        return topics.Take(5).ToList();
    }

    private static List<string> ExtractEntities(string content)
    {
        var entities = new HashSet<string>();

        // Extract capitalized words (potential names/entities)
        var matches = CapitalizedWordRegex().Matches(content);
        foreach (Match match in matches)
        {
            var word = match.Value;
            // Filter out common sentence starters
            if (!IsCommonWord(word))
            {
                entities.Add(word);
            }
        }

        return entities.Take(10).ToList();
    }

    private static bool IsCommonWord(string word)
    {
        var common = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "I", "The", "A", "An", "This", "That", "It", "Is", "Are", "Was", "Were",
            "Have", "Has", "Had", "Do", "Does", "Did", "Will", "Would", "Could",
            "Should", "Can", "May", "Might", "Must", "Shall"
        };
        return common.Contains(word);
    }

    [GeneratedRegex(@"\b[A-Z][a-z]+\b")]
    private static partial Regex CapitalizedWordRegex();

    #endregion
}
