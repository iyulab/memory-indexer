using System.Text.RegularExpressions;
using MemoryIndexer.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Compression;

/// <summary>
/// LLMLingua-2 style prompt compressor.
/// Uses token importance scoring and selective pruning to compress prompts
/// while preserving key information.
/// </summary>
/// <remarks>
/// Implements concepts from:
/// - LLMLingua: Compressing Prompts for Accelerated Inference of Large Language Models
/// - LLMLingua-2: Data Distillation for Efficient and Faithful Task-Agnostic Prompt Compression
///
/// This implementation uses embedding-based importance scoring as a proxy for perplexity-based scoring.
/// </remarks>
public sealed partial class LLMLinguaCompressor : IPromptCompressor
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<LLMLinguaCompressor> _logger;

    // Token importance weights
    private const float StructuralWeight = 0.3f;
    private const float SemanticWeight = 0.4f;
    private const float PositionalWeight = 0.2f;
    private const float TypeWeight = 0.1f;

    // Common stop words that can often be removed
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "must", "shall", "can", "need", "dare",
        "to", "of", "in", "for", "on", "with", "at", "by", "from", "as",
        "into", "through", "during", "before", "after", "above", "below",
        "between", "under", "again", "further", "then", "once", "here",
        "there", "when", "where", "why", "how", "all", "each", "few",
        "more", "most", "other", "some", "such", "only", "own", "same",
        "than", "too", "very", "just", "also", "now", "and", "but", "or"
    };

    // High-importance words that should always be preserved
    private static readonly HashSet<string> ImportantWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "exception", "fail", "failed", "success", "critical",
        "important", "warning", "note", "remember", "always", "never",
        "must", "required", "essential", "key", "main", "primary",
        "password", "secret", "credential", "token", "api", "endpoint",
        "database", "server", "client", "user", "admin", "root"
    };

    public LLMLinguaCompressor(
        IEmbeddingService embeddingService,
        ILogger<LLMLinguaCompressor> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CompressionResult> CompressAsync(
        string text,
        CompressionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CompressionOptions();

        if (string.IsNullOrWhiteSpace(text))
        {
            return new CompressionResult
            {
                CompressedText = string.Empty,
                OriginalTokenCount = 0,
                CompressedTokenCount = 0,
                InformationRetention = 1.0f,
                TargetAchieved = true
            };
        }

        _logger.LogDebug("Compressing text of length {Length} with target ratio {Ratio}",
            text.Length, options.TargetRatio);

        return options.Strategy switch
        {
            CompressionStrategy.TokenPruning => await TokenPruningCompressionAsync(text, options, cancellationToken),
            CompressionStrategy.SentencePruning => await SentencePruningCompressionAsync(text, options, cancellationToken),
            CompressionStrategy.Heuristic => HeuristicCompression(text, options),
            _ => await HybridCompressionAsync(text, options, cancellationToken)
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CompressionResult>> CompressBatchAsync(
        IEnumerable<string> texts,
        CompressionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var textList = texts.ToList();
        var results = new List<CompressionResult>(textList.Count);

        foreach (var text in textList)
        {
            var result = await CompressAsync(text, options, cancellationToken);
            results.Add(result);
        }

        return results;
    }

    /// <inheritdoc />
    public float EstimateCompressionRatio(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 1.0f;

        var tokens = Tokenize(text);
        var removableCount = tokens.Count(t => StopWords.Contains(t) && !ImportantWords.Contains(t));

        // Estimate based on stop word ratio
        return 1.0f - (float)removableCount / tokens.Count * 0.4f;
    }

    private async Task<CompressionResult> TokenPruningCompressionAsync(
        string text,
        CompressionOptions options,
        CancellationToken cancellationToken)
    {
        var tokens = Tokenize(text);
        var originalCount = tokens.Count;

        if (originalCount == 0)
        {
            return CreateEmptyResult();
        }

        // Score each token
        var scoredTokens = await ScoreTokensAsync(tokens, text, options, cancellationToken);

        // Determine how many tokens to keep
        var targetCount = (int)(originalCount * options.TargetRatio);
        if (options.MaxOutputTokens > 0)
        {
            targetCount = Math.Min(targetCount, options.MaxOutputTokens);
        }

        // Select tokens to keep based on importance
        var keptTokens = SelectTokensToKeep(scoredTokens, targetCount, options);
        var compressedText = ReconstructText(keptTokens, tokens);

        var removedTokens = tokens.Except(keptTokens.Select(kt => kt.Token)).ToList();

        return new CompressionResult
        {
            CompressedText = compressedText,
            OriginalTokenCount = originalCount,
            CompressedTokenCount = keptTokens.Count,
            InformationRetention = CalculateRetention(keptTokens, scoredTokens),
            RemovedTokens = removedTokens,
            TargetAchieved = keptTokens.Count <= targetCount
        };
    }

    private async Task<CompressionResult> SentencePruningCompressionAsync(
        string text,
        CompressionOptions options,
        CancellationToken cancellationToken)
    {
        var sentences = SplitIntoSentences(text);
        var originalTokenCount = EstimateTokenCount(text);

        if (sentences.Count <= 1)
        {
            return new CompressionResult
            {
                CompressedText = text,
                OriginalTokenCount = originalTokenCount,
                CompressedTokenCount = originalTokenCount,
                InformationRetention = 1.0f,
                TargetAchieved = true
            };
        }

        // Score sentences by importance
        var scoredSentences = await ScoreSentencesAsync(sentences, options, cancellationToken);

        // Calculate target token count
        var targetCount = (int)(originalTokenCount * options.TargetRatio);

        // Select sentences to keep
        var keptSentences = SelectSentencesToKeep(scoredSentences, targetCount);
        var compressedText = string.Join(" ", keptSentences.Select(s => s.Text));
        var compressedTokenCount = EstimateTokenCount(compressedText);

        return new CompressionResult
        {
            CompressedText = compressedText,
            OriginalTokenCount = originalTokenCount,
            CompressedTokenCount = compressedTokenCount,
            InformationRetention = CalculateSentenceRetention(keptSentences, scoredSentences),
            TargetAchieved = compressedTokenCount <= targetCount
        };
    }

    private async Task<CompressionResult> HybridCompressionAsync(
        string text,
        CompressionOptions options,
        CancellationToken cancellationToken)
    {
        // First pass: Sentence-level pruning
        var sentenceOptions = new CompressionOptions
        {
            TargetRatio = Math.Min(0.7f, options.TargetRatio * 1.5f), // More lenient first pass
            MinTokenImportance = options.MinTokenImportance,
            PreserveSentenceStructure = options.PreserveSentenceStructure,
            PreserveNamedEntities = options.PreserveNamedEntities,
            PreserveNumericals = options.PreserveNumericals,
            PreserveCodeContent = options.PreserveCodeContent,
            RequiredKeywords = options.RequiredKeywords,
            MaxOutputTokens = options.MaxOutputTokens,
            Strategy = options.Strategy
        };
        var sentenceResult = await SentencePruningCompressionAsync(text, sentenceOptions, cancellationToken);

        // Check if we've already achieved target
        var currentRatio = (float)sentenceResult.CompressedTokenCount / sentenceResult.OriginalTokenCount;
        if (currentRatio <= options.TargetRatio)
        {
            return sentenceResult;
        }

        // Second pass: Token-level pruning on the result
        var adjustedRatio = options.TargetRatio / currentRatio;
        var tokenOptions = new CompressionOptions
        {
            TargetRatio = adjustedRatio,
            MinTokenImportance = options.MinTokenImportance,
            PreserveSentenceStructure = options.PreserveSentenceStructure,
            PreserveNamedEntities = options.PreserveNamedEntities,
            PreserveNumericals = options.PreserveNumericals,
            PreserveCodeContent = options.PreserveCodeContent,
            RequiredKeywords = options.RequiredKeywords,
            MaxOutputTokens = options.MaxOutputTokens,
            Strategy = options.Strategy
        };
        var tokenResult = await TokenPruningCompressionAsync(sentenceResult.CompressedText, tokenOptions, cancellationToken);

        return new CompressionResult
        {
            CompressedText = tokenResult.CompressedText,
            OriginalTokenCount = sentenceResult.OriginalTokenCount,
            CompressedTokenCount = tokenResult.CompressedTokenCount,
            InformationRetention = sentenceResult.InformationRetention * tokenResult.InformationRetention,
            RemovedTokens = tokenResult.RemovedTokens,
            TargetAchieved = tokenResult.TargetAchieved
        };
    }

    private CompressionResult HeuristicCompression(string text, CompressionOptions options)
    {
        var tokens = Tokenize(text);
        var originalCount = tokens.Count;
        var targetCount = (int)(originalCount * options.TargetRatio);

        var keptTokens = new List<string>();
        var removedTokens = new List<string>();

        foreach (var token in tokens)
        {
            var shouldKeep = !StopWords.Contains(token)
                             || ImportantWords.Contains(token)
                             || (options.PreserveNamedEntities && IsCapitalized(token))
                             || (options.PreserveNumericals && IsNumeric(token))
                             || (options.RequiredKeywords?.Contains(token, StringComparer.OrdinalIgnoreCase) ?? false);

            if (shouldKeep || keptTokens.Count < targetCount)
            {
                keptTokens.Add(token);
            }
            else
            {
                removedTokens.Add(token);
            }
        }

        // Ensure we don't exceed target
        while (keptTokens.Count > targetCount && keptTokens.Count > 1)
        {
            // Find least important token to remove
            var indexToRemove = FindLeastImportantIndex(keptTokens);
            removedTokens.Add(keptTokens[indexToRemove]);
            keptTokens.RemoveAt(indexToRemove);
        }

        var compressedText = string.Join(" ", keptTokens);

        return new CompressionResult
        {
            CompressedText = compressedText,
            OriginalTokenCount = originalCount,
            CompressedTokenCount = keptTokens.Count,
            InformationRetention = 0.8f, // Heuristic estimation
            RemovedTokens = removedTokens,
            TargetAchieved = keptTokens.Count <= targetCount
        };
    }

    private async Task<List<ScoredToken>> ScoreTokensAsync(
        List<string> tokens,
        string originalText,
        CompressionOptions options,
        CancellationToken cancellationToken)
    {
        var scoredTokens = new List<ScoredToken>(tokens.Count);

        // Generate embedding for the whole text (semantic context)
        var textEmbedding = await _embeddingService.GenerateEmbeddingAsync(originalText, cancellationToken);

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var score = CalculateTokenImportance(token, i, tokens.Count, options);
            scoredTokens.Add(new ScoredToken(token, score, i));
        }

        return scoredTokens;
    }

    private float CalculateTokenImportance(string token, int position, int totalTokens, CompressionOptions options)
    {
        var score = 0f;

        // Structural importance (start/end of text, sentence boundaries)
        var relativePosition = (float)position / totalTokens;
        var positionScore = 1f - Math.Abs(relativePosition - 0.5f) * 0.3f; // Slight preference for middle
        if (position < 3 || position > totalTokens - 3)
        {
            positionScore += 0.2f; // Boost start/end tokens
        }
        score += positionScore * PositionalWeight;

        // Token type importance
        var typeScore = 0f;
        if (ImportantWords.Contains(token)) typeScore = 1.0f;
        else if (StopWords.Contains(token)) typeScore = 0.1f;
        else if (IsCapitalized(token)) typeScore = 0.8f;
        else if (IsNumeric(token)) typeScore = 0.7f;
        else typeScore = 0.5f;

        if (options.PreserveNamedEntities && IsCapitalized(token)) typeScore = Math.Max(typeScore, 0.9f);
        if (options.PreserveNumericals && IsNumeric(token)) typeScore = Math.Max(typeScore, 0.85f);
        if (options.RequiredKeywords?.Contains(token, StringComparer.OrdinalIgnoreCase) ?? false) typeScore = 1.0f;

        score += typeScore * TypeWeight;

        // Structural tokens (punctuation, connectors)
        if (IsPunctuation(token))
        {
            score += options.PreserveSentenceStructure ? 0.6f * StructuralWeight : 0.1f * StructuralWeight;
        }
        else
        {
            score += 0.4f * StructuralWeight;
        }

        // Semantic importance (placeholder - real LLMLingua uses perplexity)
        var semanticScore = token.Length > 4 ? 0.6f : 0.4f; // Longer words tend to carry more meaning
        score += semanticScore * SemanticWeight;

        return Math.Clamp(score, 0f, 1f);
    }

    private async Task<List<ScoredSentence>> ScoreSentencesAsync(
        List<string> sentences,
        CompressionOptions options,
        CancellationToken cancellationToken)
    {
        var scored = new List<ScoredSentence>(sentences.Count);

        // Generate embeddings for all sentences
        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(sentences, cancellationToken);

        // Calculate centroid embedding
        var centroid = CalculateCentroid(embeddings);

        for (var i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];
            var embedding = embeddings[i];

            // Calculate similarity to centroid (relevance to overall content)
            var similarity = CalculateCosineSimilarity(embedding, centroid);

            // Position score
            var positionScore = i < sentences.Count / 4 ? 0.8f : (i > 3 * sentences.Count / 4 ? 0.7f : 0.5f);

            // Content score
            var contentScore = CalculateSentenceContentScore(sentence, options);

            var finalScore = similarity * 0.4f + positionScore * 0.2f + contentScore * 0.4f;

            scored.Add(new ScoredSentence(sentence, finalScore, i));
        }

        return scored;
    }

    private static float CalculateSentenceContentScore(string sentence, CompressionOptions options)
    {
        var score = 0.5f;
        var tokens = Tokenize(sentence);

        // Check for important words
        var importantCount = tokens.Count(t => ImportantWords.Contains(t));
        score += importantCount * 0.1f;

        // Check for named entities (capitalized words)
        if (options.PreserveNamedEntities)
        {
            var entityCount = tokens.Count(IsCapitalized);
            score += entityCount * 0.05f;
        }

        // Check for numbers/dates
        if (options.PreserveNumericals)
        {
            var numericCount = tokens.Count(IsNumeric);
            score += numericCount * 0.05f;
        }

        // Check for code-like content
        if (options.PreserveCodeContent && ContainsCodeContent(sentence))
        {
            score += 0.3f;
        }

        return Math.Clamp(score, 0f, 1f);
    }

    private static List<ScoredToken> SelectTokensToKeep(
        List<ScoredToken> scoredTokens,
        int targetCount,
        CompressionOptions options)
    {
        // Sort by importance score descending, preserving relative order for ties
        var sorted = scoredTokens
            .Select((st, idx) => (st, originalIndex: idx))
            .OrderByDescending(x => x.st.Score)
            .ThenBy(x => x.originalIndex)
            .Take(targetCount)
            .Select(x => x.st)
            .OrderBy(st => st.Position) // Restore original order
            .ToList();

        return sorted;
    }

    private static List<ScoredSentence> SelectSentencesToKeep(
        List<ScoredSentence> scoredSentences,
        int targetTokenCount)
    {
        var selected = new List<ScoredSentence>();
        var currentTokens = 0;

        // Sort by score descending
        var sorted = scoredSentences.OrderByDescending(s => s.Score).ToList();

        foreach (var sentence in sorted)
        {
            var sentenceTokens = EstimateTokenCount(sentence.Text);
            if (currentTokens + sentenceTokens <= targetTokenCount || selected.Count == 0)
            {
                selected.Add(sentence);
                currentTokens += sentenceTokens;
            }
        }

        // Restore original order
        return selected.OrderBy(s => s.Position).ToList();
    }

    private static string ReconstructText(List<ScoredToken> keptTokens, List<string> originalTokens)
    {
        var result = new List<string>();

        foreach (var scoredToken in keptTokens)
        {
            result.Add(scoredToken.Token);
        }

        return string.Join(" ", result);
    }

    private static float CalculateRetention(List<ScoredToken> kept, List<ScoredToken> all)
    {
        if (all.Count == 0) return 1.0f;
        var keptImportance = kept.Sum(t => t.Score);
        var totalImportance = all.Sum(t => t.Score);
        return totalImportance > 0 ? keptImportance / totalImportance : 1.0f;
    }

    private static float CalculateSentenceRetention(List<ScoredSentence> kept, List<ScoredSentence> all)
    {
        if (all.Count == 0) return 1.0f;
        var keptImportance = kept.Sum(s => s.Score);
        var totalImportance = all.Sum(s => s.Score);
        return totalImportance > 0 ? keptImportance / totalImportance : 1.0f;
    }

    private static List<string> Tokenize(string text)
    {
        return TokenRegex().Split(text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

    private static List<string> SplitIntoSentences(string text)
    {
        return text.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    private static int EstimateTokenCount(string text)
    {
        // Rough estimate: 1 token ≈ 4 characters
        return text.Length / 4;
    }

    private static bool IsCapitalized(string token)
    {
        return token.Length > 0 && char.IsUpper(token[0]) && !StopWords.Contains(token);
    }

    private static bool IsNumeric(string token)
    {
        return NumericRegex().IsMatch(token);
    }

    private static bool IsPunctuation(string token)
    {
        return token.Length == 1 && char.IsPunctuation(token[0]);
    }

    private static bool ContainsCodeContent(string text)
    {
        return CodePatternRegex().IsMatch(text);
    }

    private static int FindLeastImportantIndex(List<string> tokens)
    {
        // Find stop word with lowest position preference (middle of text)
        var leastImportant = -1;
        var lowestScore = float.MaxValue;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!StopWords.Contains(token)) continue;

            var positionScore = Math.Abs((float)i / tokens.Count - 0.5f);
            if (positionScore < lowestScore)
            {
                lowestScore = positionScore;
                leastImportant = i;
            }
        }

        // If no stop words, remove from middle
        return leastImportant >= 0 ? leastImportant : tokens.Count / 2;
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

        if (spanA.Length != spanB.Length || spanA.Length == 0)
            return 0f;

        var dotProduct = 0f;
        var normA = 0f;
        var normB = 0f;

        for (var i = 0; i < spanA.Length; i++)
        {
            dotProduct += spanA[i] * spanB[i];
            normA += spanA[i] * spanA[i];
            normB += spanB[i] * spanB[i];
        }

        normA = MathF.Sqrt(normA);
        normB = MathF.Sqrt(normB);

        return normA > 0 && normB > 0 ? dotProduct / (normA * normB) : 0f;
    }

    private static CompressionResult CreateEmptyResult() => new()
    {
        CompressedText = string.Empty,
        OriginalTokenCount = 0,
        CompressedTokenCount = 0,
        InformationRetention = 1.0f,
        TargetAchieved = true
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"^\d+\.?\d*$")]
    private static partial Regex NumericRegex();

    [GeneratedRegex(@"[{}\[\]();=>]|->|::|//")]
    private static partial Regex CodePatternRegex();

    private sealed record ScoredToken(string Token, float Score, int Position);
    private sealed record ScoredSentence(string Text, float Score, int Position);
}
