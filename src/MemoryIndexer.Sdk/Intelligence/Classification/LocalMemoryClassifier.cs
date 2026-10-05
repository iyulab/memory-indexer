using System.Text.RegularExpressions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

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
///
/// Length is measured with the registered <see cref="ITokenCounter"/>, not by splitting on spaces: Korean puts
/// particles inside space-separated units and Chinese and Japanese use no spaces at all, so a word count calls almost
/// every turn in those languages short. Length decides the tier (where a memory lives), never whether it is kept —
/// only small talk (<see cref="MemoryClassification.Transient"/>) is dropped. The small-talk lexicon is English; a
/// turn in another language that is all greeting is kept at <see cref="Tier.Short"/> rather than risk dropping one
/// that carries information.
/// </remarks>
public sealed partial class LocalMemoryClassifier : IMemoryClassifier
{
    // Thresholds were written in English words; ~0.75 words per token is the usual English ratio, so each is the
    // same English length in tokens (5 → 7, 20 → 27, 50 → 67, 100 → 133).
    private const int TransientMaxTokens = 7;
    private const int ShortEpisodicBelowTokens = 27;
    private const int ArchiveSemanticAboveTokens = 67;
    private const int ArchiveProceduralAboveTokens = 133;
    private const float ImportancePerToken = 0.00375f;

    private readonly ILogger<LocalMemoryClassifier> _logger;
    private readonly ITokenCounter _tokenCounter;

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
    /// Small-talk phrases (greetings, acknowledgements, fillers), longest first. Content is transient only when it is
    /// made of these alone, so "ok, my plate is 12-3456" is kept while "ok thanks!" is not.
    /// </summary>
    private static readonly string[][] TransientPhrases =
    [
        .. new[]
        {
            "hello", "hi", "hey", "thanks", "thank you", "ok", "okay",
            "yes", "no", "sure", "got it", "understood", "bye", "goodbye",
            "see you", "hmm", "um", "uh", "well", "cool", "great", "nice",
            "yeah", "yep", "nope", "alright", "all right", "sounds good", "good", "oh", "ah", "haha", "lol",
            "good morning", "good night", "sure thing", "there", "so much", "a lot", "very much", "you", "too", "again"
        }
        .Select(phrase => phrase.Split(' '))
        .OrderByDescending(words => words.Length)
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

    /// <summary>
    /// Creates the classifier.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="tokenCounter">Measures content length in a way that does not depend on the language using spaces.</param>
    public LocalMemoryClassifier(
        ILogger<LocalMemoryClassifier> logger,
        ITokenCounter tokenCounter)
    {
        ArgumentNullException.ThrowIfNull(tokenCounter);

        _logger = logger;
        _tokenCounter = tokenCounter;

        LogInitialized(_logger);
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

        var classification = ClassifyHeuristic(content);

        var secondaryTypesStr = string.Join(",", classification.SecondaryTypes);
        LogClassified(_logger, classification.Tier, classification.Type, secondaryTypesStr, classification.Importance);

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

    private MemoryClassification ClassifyHeuristic(string content)
    {
        var lower = content.ToLowerInvariant();
        var tokens = _tokenCounter.Count(content);

        // Small talk is the only content that is not kept.
        if (IsTransientContent(lower, tokens))
        {
            return MemoryClassification.Transient;
        }

        // Phase 23.1: Multi-score classification
        var scores = CalculateTypeScores(lower);

        // Primary type = highest score
        var primaryType = scores.OrderByDescending(x => x.Value).First().Key;

        // Secondary types = scores >= 0.3 (excluding primary)
        var secondaryTypes = scores
            .Where(x => x.Key != primaryType && x.Value >= 0.3f)
            .OrderByDescending(x => x.Value)
            .Select(x => x.Key)
            .ToList();

        // Determine tier based on primary type and content
        var tier = DetermineTier(tokens, primaryType);

        // Calculate importance
        var importance = CalculateImportance(lower, tokens, primaryType);

        // Extract topics and entities
        var topics = ExtractTopics(lower);
        var entities = ExtractEntities(content);

        return new MemoryClassification
        {
            Tier = tier,
            Type = primaryType,
            SecondaryTypes = secondaryTypes,
            TypeConfidences = scores,
            Importance = importance,
            Topics = topics,
            Entities = entities,
            // The tier says where a memory lives; a short turn is working memory, not something to throw away.
            ShouldPersist = true,
            Confidence = CalculateOverallConfidence(scores),
            Reason = $"Multi-score: {primaryType}={scores[primaryType]:F2}, {tokens} tokens"
        };
    }

    /// <summary>
    /// Whether <paramref name="lower"/> holds <paramref name="phrase"/> as whole words: not inside a longer word, so
    /// "use" does not match "user:" or "because", and "effect" does not match "effective".
    /// </summary>
    private static bool HasPhrase(string lower, string phrase)
    {
        phrase = phrase.Trim();
        if (phrase.Length == 0)
        {
            return false;
        }

        var wordEnd = char.IsLetterOrDigit(phrase[^1]);
        for (var index = lower.IndexOf(phrase, StringComparison.Ordinal); index >= 0;
             index = lower.IndexOf(phrase, index + 1, StringComparison.Ordinal))
        {
            var end = index + phrase.Length;
            var startsWord = index == 0 || !char.IsLetterOrDigit(lower[index - 1]);
            var endsWord = !wordEnd || end >= lower.Length || !char.IsLetterOrDigit(lower[end]);
            if (startsWord && endsWord)
            {
                return true;
            }
        }

        return false;
    }

    #region Phase 23.1: Multi-Score Classification

    private static Dictionary<MemoryType, float> CalculateTypeScores(string lower)
    {
        return new Dictionary<MemoryType, float>
        {
            [MemoryType.Episodic] = CalculateEpisodicScore(lower),
            [MemoryType.Semantic] = CalculateSemanticScore(lower),
            [MemoryType.Procedural] = CalculateProceduralScore(lower),
            [MemoryType.Fact] = CalculateFactScore(lower)
        };
    }

    private static float CalculateEpisodicScore(string lower)
    {
        float score = 0.2f; // Base score

        // Time/location markers (+0.3 each, max 0.6)
        int markerCount = EpisodicIndicators.Count(i => HasPhrase(lower, i));
        score += Math.Min(markerCount * 0.3f, 0.6f);

        // Personal pronouns in past tense (+0.2)
        if ((HasPhrase(lower, "i") || HasPhrase(lower, "we")) &&
            (HasPhrase(lower, "did") || HasPhrase(lower, "was") || HasPhrase(lower, "were")))
        {
            score += 0.2f;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    private static float CalculateSemanticScore(string lower)
    {
        float score = 0.1f;

        // Semantic indicators (+0.25 each, max 0.75)
        int count = SemanticIndicators.Count(i => HasPhrase(lower, i));
        score += Math.Min(count * 0.25f, 0.75f);

        // Definition pattern: "X is a Y" (+0.3) - stronger weight for definitions
        if (Regex.IsMatch(lower, @"\b\w+ is a \w+"))
        {
            score += 0.3f;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    private static float CalculateProceduralScore(string lower)
    {
        float score = 0.1f;

        // Procedural indicators (+0.2 each, max 0.6)
        int count = ProceduralIndicators.Count(i => HasPhrase(lower, i));
        score += Math.Min(count * 0.2f, 0.6f);

        // Tool/framework keywords (+0.3 if present)
        if (ToolKeywords.Any(k => HasPhrase(lower, k)))
        {
            score += 0.3f;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    private static float CalculateFactScore(string lower)
    {
        // Fact indicators (+0.2 each)
        int count = FactIndicators.Count(i => HasPhrase(lower, i));

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

    /// <summary>
    /// Whether short content is small talk only: after a leading role label ("User:") and punctuation are set aside,
    /// every word belongs to a <see cref="TransientPhrases"/> phrase. A prefix or suffix match is not enough: it
    /// dropped "ok, my plate is 12-3456" with its acknowledgement and "his name is Kim" as "hi".
    /// </summary>
    private static bool IsTransientContent(string lower, int tokens)
    {
        if (tokens > TransientMaxTokens)
        {
            return false;
        }

        var words = WordRegex().Matches(RoleLabelRegex().Replace(lower, string.Empty, 1))
            .Select(match => match.Value)
            .ToArray();

        // Nothing left but a role label, punctuation or emoji counts as small talk: there is nothing to remember.
        var index = 0;
        while (index < words.Length)
        {
            var phrase = TransientPhrases.FirstOrDefault(p =>
                index + p.Length <= words.Length && p.SequenceEqual(words.Skip(index).Take(p.Length), StringComparer.Ordinal));
            if (phrase is null)
            {
                return false;
            }

            index += phrase.Length;
        }

        return true;
    }

    private static Tier DetermineTier(int tokens, MemoryType type)
    {
        // Facts about user go to User tier
        if (type == MemoryType.Fact)
        {
            return Tier.Archive;
        }

        // Long semantic content goes to User tier
        if (type == MemoryType.Semantic && tokens > ArchiveSemanticAboveTokens)
        {
            return Tier.Archive;
        }

        // Procedural knowledge persists at Session or User level
        if (type == MemoryType.Procedural)
        {
            return tokens > ArchiveProceduralAboveTokens ? Tier.Archive : Tier.Long;
        }

        // Short episodic content stays in Working memory
        if (tokens < ShortEpisodicBelowTokens)
        {
            return Tier.Short;
        }

        // Medium-length content goes to Session
        return Tier.Long;
    }

    private static float CalculateImportance(string lower, int tokens, MemoryType type)
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
        importance += Math.Min(tokens * ImportancePerToken, 0.2f);

        // Contains personal information
        if (HasPhrase(lower, "i") || HasPhrase(lower, "my") || HasPhrase(lower, "me"))
        {
            importance += 0.1f;
        }

        // Contains technical keywords
        if (TopicKeywords.Keys.Any(k => HasPhrase(lower, k)))
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
            if (HasPhrase(lower, keyword))
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

    [GeneratedRegex(@"^\s*\p{L}[\p{L}\p{N}_ -]{0,30}:")]
    private static partial Regex RoleLabelRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRegex();

    [LoggerMessage(Level = LogLevel.Information, Message = "LocalMemoryClassifier initialized (Phase 23.1 multi-score mode)")]
    private static partial void LogInitialized(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Classified: Tier={Tier}, Primary={Type}, Secondary=[{Secondary}], Importance={Importance:F2}")]
    private static partial void LogClassified(ILogger logger, Tier tier, MemoryType type, string secondary, float importance);

    #endregion
}
