using System.Text.RegularExpressions;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Intelligence.Classification;

/// <summary>
/// Memory classifier using heuristic rules with optional LLM enhancement.
/// </summary>
/// <remarks>
/// This implementation uses pattern matching and heuristics for fast classification.
/// It can be extended to use LocalAI.Generator for more sophisticated classification
/// when LLM resources are available.
///
/// Classification is based on:
/// - Content length and complexity
/// - Presence of factual indicators (names, numbers, preferences)
/// - Transient patterns (greetings, acknowledgments)
/// - Topic extraction via keyword detection
/// </remarks>
public sealed partial class LocalMemoryClassifier : IMemoryClassifier
{
    private readonly ILogger<LocalMemoryClassifier> _logger;
    private readonly IntelligenceOptions _options;

    /// <summary>
    /// Patterns that indicate factual content about the user.
    /// </summary>
    private static readonly string[] FactIndicators =
    [
        "my name is", "i am", "i'm", "i prefer", "i like", "i use",
        "i work", "i live", "my favorite", "i always", "i never",
        "my email", "my phone", "my address", "i was born"
    ];

    /// <summary>
    /// Patterns that indicate procedural/how-to content.
    /// </summary>
    private static readonly string[] ProceduralIndicators =
    [
        "how to", "step by step", "first,", "then,", "finally,",
        "to do this", "you need to", "make sure to", "don't forget to"
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

    public LocalMemoryClassifier(
        IOptions<MemoryIndexerOptions> options,
        ILogger<LocalMemoryClassifier> logger)
    {
        _logger = logger;
        _options = options.Value.Intelligence;

        _logger.LogInformation("LocalMemoryClassifier initialized (heuristic mode)");
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
            "Classified message: Tier={Tier}, Type={Type}, Importance={Importance:F2}, Persist={ShouldPersist}",
            classification.Tier, classification.Type, classification.Importance, classification.ShouldPersist);

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

        // Determine memory type
        var type = DetermineMemoryType(lower);

        // Determine tier based on content characteristics
        var tier = DetermineTier(lower, wordCount, type, context);

        // Calculate importance
        var importance = CalculateImportance(lower, wordCount, type, context);

        // Extract topics
        var topics = ExtractTopics(lower);

        // Extract entities (simplified)
        var entities = ExtractEntities(content);

        // Determine if should persist
        var shouldPersist = tier != MemoryTier.Working && importance >= 0.3f;

        return new MemoryClassification
        {
            Tier = tier,
            Type = type,
            Importance = importance,
            Topics = topics,
            Entities = entities,
            ShouldPersist = shouldPersist,
            Confidence = 0.7f, // Heuristic confidence
            Reason = $"Heuristic: {wordCount} words, type={type}"
        };
    }

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

    private static MemoryType DetermineMemoryType(string lower)
    {
        // Check for factual content about user
        foreach (var indicator in FactIndicators)
        {
            if (lower.Contains(indicator))
            {
                return MemoryType.Fact;
            }
        }

        // Check for procedural content
        foreach (var indicator in ProceduralIndicators)
        {
            if (lower.Contains(indicator))
            {
                return MemoryType.Procedural;
            }
        }

        // Check for semantic/conceptual content
        if (lower.Contains("means") || lower.Contains("definition") ||
            lower.Contains("concept") || lower.Contains("principle"))
        {
            return MemoryType.Semantic;
        }

        // Default to episodic (conversation/event)
        return MemoryType.Episodic;
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
}
