using System.Numerics.Tensors;
using MemoryIndexer.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Chunking;

/// <summary>
/// Neural TextTiling implementation for topic segmentation.
/// Uses sliding window similarity analysis to detect topic boundaries.
/// </summary>
public sealed class TopicSegmenter
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<TopicSegmenter> _logger;

    /// <summary>
    /// Number of sentences in the sliding window.
    /// </summary>
    public int WindowSize { get; init; } = 3;

    /// <summary>
    /// Minimum similarity drop to consider as a boundary.
    /// </summary>
    public float SimilarityDropThreshold { get; init; } = 0.15f;

    /// <summary>
    /// Minimum number of sentences per segment.
    /// </summary>
    public int MinSegmentLength { get; init; } = 2;

    /// <summary>
    /// Whether to use local minima detection instead of threshold.
    /// </summary>
    public bool UseLocalMinima { get; init; } = true;

    public TopicSegmenter(
        IEmbeddingService embeddingService,
        ILogger<TopicSegmenter> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <summary>
    /// Segments text into topic-coherent chunks.
    /// </summary>
    public async Task<IReadOnlyList<TopicSegment>> SegmentAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var sentences = SplitIntoSentences(text);

        if (sentences.Count <= MinSegmentLength)
        {
            return [new TopicSegment
            {
                Content = text,
                StartIndex = 0,
                EndIndex = sentences.Count - 1,
                Sentences = sentences
            }];
        }

        _logger.LogDebug("Segmenting {Count} sentences", sentences.Count);

        // Generate embeddings for all sentences
        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(
            sentences, cancellationToken);

        // Calculate similarity scores using sliding window
        var similarities = CalculateWindowSimilarities(embeddings);

        // Detect boundaries
        var boundaries = UseLocalMinima
            ? DetectLocalMinimaBoundaries(similarities)
            : DetectThresholdBoundaries(similarities);

        // Enforce minimum segment length
        boundaries = EnforceMinSegmentLength(boundaries, sentences.Count);

        // Create segments
        var segments = CreateSegments(sentences, boundaries);

        _logger.LogDebug("Created {Count} segments from {SentenceCount} sentences",
            segments.Count, sentences.Count);

        return segments;
    }

    /// <summary>
    /// Segments a conversation (list of messages) into topics.
    /// </summary>
    public async Task<IReadOnlyList<TopicSegment>> SegmentConversationAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return [];

        if (messages.Count <= MinSegmentLength)
        {
            return [new TopicSegment
            {
                Content = string.Join("\n", messages.Select(m => m.Content)),
                StartIndex = 0,
                EndIndex = messages.Count - 1,
                Messages = messages.ToList()
            }];
        }

        _logger.LogDebug("Segmenting conversation with {Count} messages", messages.Count);

        // Generate embeddings for each message
        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(
            messages.Select(m => m.Content), cancellationToken);

        // Calculate similarity scores
        var similarities = CalculateWindowSimilarities(embeddings);

        // Detect boundaries
        var boundaries = UseLocalMinima
            ? DetectLocalMinimaBoundaries(similarities)
            : DetectThresholdBoundaries(similarities);

        // Enforce minimum segment length
        boundaries = EnforceMinSegmentLength(boundaries, messages.Count);

        // Create segments from messages
        var segments = CreateConversationSegments(messages, boundaries);

        _logger.LogDebug("Created {Count} topic segments from conversation", segments.Count);

        return segments;
    }

    /// <summary>
    /// Calculates pairwise similarities using sliding window approach.
    /// </summary>
    private List<float> CalculateWindowSimilarities(IReadOnlyList<ReadOnlyMemory<float>> embeddings)
    {
        var similarities = new List<float>();

        for (var i = 0; i < embeddings.Count - 1; i++)
        {
            // Create window embeddings (average of window)
            var leftStart = Math.Max(0, i - WindowSize + 1);
            var rightEnd = Math.Min(embeddings.Count - 1, i + WindowSize);

            var leftWindow = AverageEmbeddings(embeddings, leftStart, i + 1);
            var rightWindow = AverageEmbeddings(embeddings, i + 1, rightEnd + 1);

            var similarity = CalculateCosineSimilarity(leftWindow, rightWindow);
            similarities.Add(similarity);
        }

        return similarities;
    }

    /// <summary>
    /// Detects boundaries using local minima in similarity scores.
    /// </summary>
    private List<int> DetectLocalMinimaBoundaries(List<float> similarities)
    {
        var boundaries = new List<int>();

        for (var i = 1; i < similarities.Count - 1; i++)
        {
            var current = similarities[i];
            var prev = similarities[i - 1];
            var next = similarities[i + 1];

            // Local minimum
            if (current < prev && current < next)
            {
                // Also check if the drop is significant
                var avgNeighbor = (prev + next) / 2;
                if (avgNeighbor - current >= SimilarityDropThreshold)
                {
                    boundaries.Add(i + 1); // Boundary after sentence i
                }
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Detects boundaries using fixed threshold.
    /// </summary>
    private List<int> DetectThresholdBoundaries(List<float> similarities)
    {
        var boundaries = new List<int>();
        var mean = similarities.Average();
        var stdDev = (float)Math.Sqrt(similarities.Average(s => Math.Pow(s - mean, 2)));
        var threshold = mean - stdDev * SimilarityDropThreshold;

        for (var i = 0; i < similarities.Count; i++)
        {
            if (similarities[i] < threshold)
            {
                boundaries.Add(i + 1);
            }
        }

        return boundaries;
    }

    /// <summary>
    /// Enforces minimum segment length by removing too-close boundaries.
    /// </summary>
    private List<int> EnforceMinSegmentLength(List<int> boundaries, int totalCount)
    {
        if (boundaries.Count == 0)
            return boundaries;

        var filtered = new List<int>();
        var lastBoundary = 0;

        foreach (var boundary in boundaries)
        {
            if (boundary - lastBoundary >= MinSegmentLength)
            {
                // Also check remaining length
                if (totalCount - boundary >= MinSegmentLength)
                {
                    filtered.Add(boundary);
                    lastBoundary = boundary;
                }
            }
        }

        return filtered;
    }

    /// <summary>
    /// Creates topic segments from sentences and boundaries.
    /// </summary>
    private static List<TopicSegment> CreateSegments(List<string> sentences, List<int> boundaries)
    {
        var segments = new List<TopicSegment>();
        var startIndex = 0;

        foreach (var boundary in boundaries)
        {
            var segmentSentences = sentences.Skip(startIndex).Take(boundary - startIndex).ToList();
            segments.Add(new TopicSegment
            {
                Content = string.Join(" ", segmentSentences),
                StartIndex = startIndex,
                EndIndex = boundary - 1,
                Sentences = segmentSentences
            });
            startIndex = boundary;
        }

        // Add final segment
        if (startIndex < sentences.Count)
        {
            var segmentSentences = sentences.Skip(startIndex).ToList();
            segments.Add(new TopicSegment
            {
                Content = string.Join(" ", segmentSentences),
                StartIndex = startIndex,
                EndIndex = sentences.Count - 1,
                Sentences = segmentSentences
            });
        }

        return segments;
    }

    /// <summary>
    /// Creates topic segments from conversation messages.
    /// </summary>
    private static List<TopicSegment> CreateConversationSegments(
        IReadOnlyList<ConversationMessage> messages,
        List<int> boundaries)
    {
        var segments = new List<TopicSegment>();
        var startIndex = 0;

        foreach (var boundary in boundaries)
        {
            var segmentMessages = messages.Skip(startIndex).Take(boundary - startIndex).ToList();
            segments.Add(new TopicSegment
            {
                Content = string.Join("\n", segmentMessages.Select(m => m.Content)),
                StartIndex = startIndex,
                EndIndex = boundary - 1,
                Messages = segmentMessages
            });
            startIndex = boundary;
        }

        // Add final segment
        if (startIndex < messages.Count)
        {
            var segmentMessages = messages.Skip(startIndex).ToList();
            segments.Add(new TopicSegment
            {
                Content = string.Join("\n", segmentMessages.Select(m => m.Content)),
                StartIndex = startIndex,
                EndIndex = messages.Count - 1,
                Messages = segmentMessages
            });
        }

        return segments;
    }

    /// <summary>
    /// Splits text into sentences.
    /// </summary>
    private static List<string> SplitIntoSentences(string text)
    {
        // Simple sentence splitting - can be improved with better NLP
        var sentences = text
            .Split(['.', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return sentences;
    }

    /// <summary>
    /// Averages multiple embeddings.
    /// </summary>
    private static float[] AverageEmbeddings(
        IReadOnlyList<ReadOnlyMemory<float>> embeddings,
        int start,
        int end)
    {
        var dimensions = embeddings[0].Length;
        var result = new float[dimensions];

        for (var i = start; i < end; i++)
        {
            var span = embeddings[i].Span;
            for (var j = 0; j < dimensions; j++)
            {
                result[j] += span[j];
            }
        }

        var count = end - start;
        for (var j = 0; j < dimensions; j++)
        {
            result[j] /= count;
        }

        return result;
    }

    /// <summary>
    /// Calculates cosine similarity between two vectors.
    /// </summary>
    private static float CalculateCosineSimilarity(float[] a, float[] b)
    {
        var dotProduct = TensorPrimitives.Dot(a.AsSpan(), b.AsSpan());
        var normA = TensorPrimitives.Norm(a.AsSpan());
        var normB = TensorPrimitives.Norm(b.AsSpan());

        if (normA == 0 || normB == 0)
            return 0f;

        return dotProduct / (normA * normB);
    }
}

/// <summary>
/// Represents a topic segment.
/// </summary>
public sealed class TopicSegment
{
    /// <summary>
    /// The content of this segment.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Start index in original sequence.
    /// </summary>
    public required int StartIndex { get; init; }

    /// <summary>
    /// End index in original sequence.
    /// </summary>
    public required int EndIndex { get; init; }

    /// <summary>
    /// Sentences in this segment (if text-based).
    /// </summary>
    public List<string> Sentences { get; init; } = [];

    /// <summary>
    /// Messages in this segment (if conversation-based).
    /// </summary>
    public List<ConversationMessage> Messages { get; init; } = [];

    /// <summary>
    /// Extracted topic label (if available).
    /// </summary>
    public string? TopicLabel { get; set; }

    /// <summary>
    /// Topic embedding (if generated).
    /// </summary>
    public ReadOnlyMemory<float>? Embedding { get; set; }
}

/// <summary>
/// Represents a message in a conversation.
/// </summary>
public sealed class ConversationMessage
{
    /// <summary>
    /// The role of the message sender (user, assistant, system).
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// The content of the message.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Optional timestamp.
    /// </summary>
    public DateTime? Timestamp { get; init; }
}
