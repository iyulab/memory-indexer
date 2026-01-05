using System.Text.RegularExpressions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Retrieval;

/// <summary>
/// Heuristic-based query intent classifier.
/// Uses pattern matching and keyword analysis for classification.
/// </summary>
/// <remarks>
/// Classification strategy based on research:
/// - Factual: "what is", "who is", "my X", fact-related patterns
/// - Contextual: "tell me more", "continue", pronouns like "that", "it"
/// - Temporal: time expressions ("yesterday", "last week", dates)
/// - Relational: "related to", "connected with", "about X and Y"
/// </remarks>
public sealed partial class LocalQueryIntentClassifier : IQueryIntentClassifier
{
    private readonly ILogger<LocalQueryIntentClassifier> _logger;

    // Factual patterns: direct fact queries
    private static readonly string[] FactualPatterns =
    [
        @"what(?:\s+is|\s+are)\s+(?:my|your)",
        @"(?:who|what)\s+(?:is|are|was|were)",
        @"(?:my|your)\s+\w+(?:\s+is|\s+are)?",
        @"do\s+(?:i|you)\s+(?:like|prefer|have|know)",
        @"(?:tell|remind)\s+me\s+(?:my|about\s+my)",
        @"what\s+(?:did\s+i|have\s+i)\s+(?:say|mention|tell)",
        @"remember\s+(?:my|when\s+i)"
    ];

    // Contextual patterns: continuation queries
    private static readonly string[] ContextualPatterns =
    [
        @"(?:tell|explain|say)\s+(?:me\s+)?more",
        @"continue\s+(?:with|from|the)",
        @"(?:elaborate|expand)\s+on",
        @"(?:what|how)\s+(?:about|regarding)\s+(?:that|this|it)",
        @"^(?:and|so|but|also)\s+",
        @"(?:as\s+(?:i|we)\s+(?:said|discussed|mentioned))"
    ];

    // Temporal patterns: time-based queries (higher weight patterns first)
    private static readonly string[] TemporalPatterns =
    [
        @"(?:last|previous|past)\s+(?:week|month|year|day|time|session|conversation)",
        @"(?:yesterday|today|earlier|before|ago)\b",
        @"(?:when\s+did|what\s+time|how\s+long\s+ago)",
        @"(?:recently|lately|just\s+now)",
        @"\d+\s+(?:days?|weeks?|months?|hours?)\s+ago",
        @"(?:in|during|on)\s+(?:january|february|march|april|may|june|july|august|september|october|november|december)",
        @"(?:first|last)\s+(?:time|conversation|session)",
        @"(?:previous|prior)\s+(?:session|conversation)",
        @"(?:what|how)\s+(?:was|were)\s+(?:the\s+)?(?:previous|prior|last)\s+(?:session|conversation)"
    ];

    // Relational patterns: relationship queries
    private static readonly string[] RelationalPatterns =
    [
        @"(?:related|similar|connected)\s+(?:to|with)",
        @"(?:relationship|connection)\s+(?:between|with)",
        @"(?:how\s+(?:does|is|are))\s+\w+\s+(?:relate|connect)",
        @"(?:linked|associated)\s+(?:to|with)",
        @"what\s+else\s+(?:do\s+)?(?:i|you|we)\s+know",
        @"(?:anything\s+(?:else\s+)?(?:about|related\s+to))",
        @"(?:more\s+information|everything)\s+(?:about|on|regarding)",
        @"\belse\b.*\bknow\b.*\babout\b",  // Catch "else...know...about" patterns
        @"\belse\b.*\babout\b"  // "else...about" indicates exploring relationships
    ];

    // Stopwords to filter from keywords
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "must", "can", "i", "you", "he", "she",
        "it", "we", "they", "my", "your", "his", "her", "its", "our", "their",
        "this", "that", "these", "those", "what", "which", "who", "whom",
        "when", "where", "why", "how", "all", "each", "every", "both", "few",
        "more", "most", "other", "some", "such", "no", "nor", "not", "only",
        "same", "so", "than", "too", "very", "just", "about", "after", "again",
        "also", "any", "because", "before", "between", "but", "by", "for",
        "from", "if", "in", "into", "of", "on", "or", "out", "over", "then",
        "there", "through", "to", "under", "up", "with", "me", "tell", "know"
    };

    public LocalQueryIntentClassifier(ILogger<LocalQueryIntentClassifier> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<QueryIntentResult> ClassifyAsync(
        string query,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var normalizedQuery = query.ToLowerInvariant().Trim();

        // Calculate scores for each intent type
        var factualScore = CalculatePatternScore(normalizedQuery, FactualPatterns);
        var contextualScore = CalculatePatternScore(normalizedQuery, ContextualPatterns);
        var temporalScore = CalculatePatternScore(normalizedQuery, TemporalPatterns);
        var relationalScore = CalculatePatternScore(normalizedQuery, RelationalPatterns);

        // Boost contextual if context is provided and query references it
        if (!string.IsNullOrWhiteSpace(context) && HasContextualReference(normalizedQuery))
        {
            contextualScore += 0.3f;
        }

        // Determine primary intent
        var scores = new Dictionary<QueryIntent, float>
        {
            [QueryIntent.Factual] = factualScore,
            [QueryIntent.Contextual] = contextualScore,
            [QueryIntent.Temporal] = temporalScore,
            [QueryIntent.Relational] = relationalScore,
            [QueryIntent.General] = 0.1f // Base score for general
        };

        var sortedIntents = scores.OrderByDescending(x => x.Value).ToList();
        var primaryIntent = sortedIntents[0].Key;
        var primaryConfidence = Math.Clamp(sortedIntents[0].Value, 0f, 1f);

        // If no pattern matched well, default to General
        if (primaryConfidence < 0.2f)
        {
            primaryIntent = QueryIntent.General;
            primaryConfidence = 0.5f;
        }

        // Determine secondary intent if close
        QueryIntent? secondaryIntent = null;
        if (sortedIntents.Count > 1 && sortedIntents[1].Value > 0.2f &&
            sortedIntents[0].Value - sortedIntents[1].Value < 0.2f)
        {
            secondaryIntent = sortedIntents[1].Key;
        }

        // Extract temporal reference
        var temporalRef = ExtractTemporalReference(normalizedQuery);

        // Extract keywords
        var keywords = ExtractKeywords(normalizedQuery);

        // Extract entity references
        var entities = ExtractEntityReferences(query); // Use original case

        // Determine tier priority based on intent
        var tierPriority = GetTierPriority(primaryIntent);

        _logger.LogDebug(
            "Query '{Query}' classified as {Intent} with confidence {Confidence:F2}",
            query, primaryIntent, primaryConfidence);

        var result = new QueryIntentResult
        {
            Intent = primaryIntent,
            Confidence = primaryConfidence,
            SecondaryIntent = secondaryIntent,
            TemporalReference = temporalRef,
            EntityReferences = entities,
            Keywords = keywords,
            TierPriority = tierPriority
        };

        return Task.FromResult(result);
    }

    private static float CalculatePatternScore(string query, string[] patterns)
    {
        var matchCount = 0;
        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(query, pattern, RegexOptions.IgnoreCase))
            {
                matchCount++;
            }
        }

        // Normalize score: more matches = higher confidence
        return matchCount switch
        {
            0 => 0f,
            1 => 0.4f,
            2 => 0.7f,
            _ => 0.9f
        };
    }

    private static bool HasContextualReference(string query)
    {
        // Check for pronouns and demonstratives that reference prior context
        return Regex.IsMatch(query, @"\b(that|this|it|them|those|these|there)\b", RegexOptions.IgnoreCase);
    }

    private static string? ExtractTemporalReference(string query)
    {
        // Try to extract temporal expressions
        var patterns = new[]
        {
            @"(last\s+(?:week|month|year|day|time|session))",
            @"(yesterday|today|earlier|recently|lately)",
            @"(\d+\s+(?:days?|weeks?|months?|hours?)\s+ago)",
            @"((?:in|during|on)\s+(?:january|february|march|april|may|june|july|august|september|october|november|december))",
            @"((?:first|last)\s+(?:time|conversation|session))"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(query, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
        }

        return null;
    }

    private static List<string> ExtractKeywords(string query)
    {
        // Tokenize and filter
        var words = Regex.Split(query.ToLowerInvariant(), @"\W+")
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .Distinct()
            .ToList();

        return words;
    }

    private static List<string> ExtractEntityReferences(string query)
    {
        var entities = new List<string>();

        // Extract quoted strings
        var quotedMatches = Regex.Matches(query, @"""([^""]+)""");
        foreach (Match match in quotedMatches)
        {
            entities.Add(match.Groups[1].Value);
        }

        // Extract capitalized words (potential proper nouns) - excluding sentence start
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < words.Length; i++) // Skip first word
        {
            var word = words[i].Trim(',', '.', '?', '!', ';', ':');
            if (word.Length > 1 && char.IsUpper(word[0]) && !StopWords.Contains(word))
            {
                entities.Add(word);
            }
        }

        return entities.Distinct().ToList();
    }

    private static List<MemoryTier> GetTierPriority(QueryIntent intent) => intent switch
    {
        QueryIntent.Factual => [MemoryTier.User, MemoryTier.Session, MemoryTier.Working],
        QueryIntent.Contextual => [MemoryTier.Working, MemoryTier.Session, MemoryTier.User],
        QueryIntent.Temporal => [MemoryTier.Session, MemoryTier.User, MemoryTier.Working],
        QueryIntent.Relational => [MemoryTier.Session, MemoryTier.User, MemoryTier.Working],
        _ => [MemoryTier.Working, MemoryTier.Session, MemoryTier.User]
    };
}
