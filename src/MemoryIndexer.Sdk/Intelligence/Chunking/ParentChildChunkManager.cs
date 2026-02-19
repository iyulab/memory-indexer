using System.Numerics.Tensors;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Chunking;

/// <summary>
/// Parent-Child chunk manager for hierarchical retrieval.
/// Enables precise matching with small child chunks while returning
/// larger parent chunks for richer context.
/// </summary>
/// <remarks>
/// Pattern: "Small-to-Big" retrieval
/// - Child chunks: Small (100-200 tokens) for precise semantic matching
/// - Parent chunks: Larger (500-1000 tokens) for context-rich responses
///
/// Benefits:
/// - Better precision: Small chunks match queries more accurately
/// - Better context: Parent chunks provide surrounding information
/// - Reduced hallucination: More context = better grounded responses
/// </remarks>
public sealed partial class ParentChildChunkManager : IParentChildChunkManager
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<ParentChildChunkManager> _logger;

    /// <summary>
    /// Target size for child chunks in characters.
    /// </summary>
    public int ChildChunkSize { get; init; } = 500;

    /// <summary>
    /// Overlap between child chunks in characters.
    /// </summary>
    public int ChildChunkOverlap { get; init; } = 100;

    /// <summary>
    /// Number of child chunks per parent.
    /// </summary>
    public int ChildrenPerParent { get; init; } = 4;

    /// <summary>
    /// Minimum content length to apply chunking.
    /// </summary>
    public int MinContentLength { get; init; } = 200;

    public ParentChildChunkManager(
        IEmbeddingService embeddingService,
        ILogger<ParentChildChunkManager> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChunkHierarchy> CreateHierarchyAsync(
        string content,
        string? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content cannot be empty", nameof(content));
        }

        // For short content, create single-level hierarchy
        if (content.Length < MinContentLength)
        {
            var singleChunk = new ContentChunk
            {
                Id = Guid.NewGuid(),
                Content = content,
                SourceId = sourceId,
                ChunkType = ChunkType.Parent,
                StartPosition = 0,
                EndPosition = content.Length,
                ChildIds = []
            };

            singleChunk.Embedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

            return new ChunkHierarchy
            {
                SourceId = sourceId,
                OriginalContent = content,
                ParentChunks = [singleChunk],
                ChildChunks = [],
                ChunkMap = new Dictionary<Guid, ContentChunk> { [singleChunk.Id] = singleChunk }
            };
        }

        // Create child chunks first
        var childChunks = CreateChildChunks(content, sourceId);

        LogCreatedChildChunks(_logger, childChunks.Count, content.Length);

        // Group children into parents
        var parentChunks = CreateParentChunks(content, childChunks, sourceId);

        LogCreatedParentChunks(_logger, parentChunks.Count);

        // Generate embeddings for all chunks
        var allChunks = childChunks.Concat(parentChunks).ToList();
        var embeddings = await _embeddingService.GenerateBatchEmbeddingsAsync(
            allChunks.Select(c => c.Content), cancellationToken);

        for (var i = 0; i < allChunks.Count; i++)
        {
            allChunks[i].Embedding = embeddings[i];
        }

        // Build chunk map
        var chunkMap = allChunks.ToDictionary(c => c.Id);

        return new ChunkHierarchy
        {
            SourceId = sourceId,
            OriginalContent = content,
            ParentChunks = parentChunks,
            ChildChunks = childChunks,
            ChunkMap = chunkMap
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParentChildSearchResult>> SearchAsync(
        string query,
        ChunkHierarchy hierarchy,
        int topK = 5,
        bool returnParents = true,
        CancellationToken cancellationToken = default)
    {
        if (hierarchy.ChildChunks.Count == 0)
        {
            // No children, search parents directly
            return await SearchParentsDirectlyAsync(query, hierarchy, topK, cancellationToken);
        }

        // Generate query embedding
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Search child chunks for precise matching
        var childResults = hierarchy.ChildChunks
            .Select(c => new
            {
                Chunk = c,
                Score = CalculateCosineSimilarity(queryEmbedding, c.Embedding!.Value)
            })
            .OrderByDescending(x => x.Score)
            .Take(topK * 2) // Over-fetch to deduplicate parents
            .ToList();

        LogMatchingChildChunks(_logger, childResults.Count);

        if (!returnParents)
        {
            // Return child chunks directly
            return childResults
                .Take(topK)
                .Select(x => new ParentChildSearchResult
                {
                    MatchedChunk = x.Chunk,
                    ParentChunk = null,
                    Score = x.Score,
                    MatchType = ChunkMatchType.ChildOnly
                })
                .ToList();
        }

        // Aggregate by parent and return parent chunks with highest child score
        var parentResults = new Dictionary<Guid, ParentChildSearchResult>();

        foreach (var result in childResults)
        {
            if (!result.Chunk.ParentId.HasValue)
                continue;

            var parentId = result.Chunk.ParentId.Value;

            if (!parentResults.TryGetValue(parentId, out var existing) || existing.Score < result.Score)
            {
                var parent = hierarchy.ChunkMap.GetValueOrDefault(parentId);
                parentResults[parentId] = new ParentChildSearchResult
                {
                    MatchedChunk = result.Chunk,
                    ParentChunk = parent,
                    Score = result.Score,
                    MatchType = ChunkMatchType.ChildToParent
                };
            }
        }

        return parentResults.Values
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
    }

    /// <inheritdoc />
    public ContentChunk? GetParent(ContentChunk child, ChunkHierarchy hierarchy)
    {
        if (!child.ParentId.HasValue)
            return null;

        return hierarchy.ChunkMap.GetValueOrDefault(child.ParentId.Value);
    }

    /// <inheritdoc />
    public IReadOnlyList<ContentChunk> GetChildren(ContentChunk parent, ChunkHierarchy hierarchy)
    {
        return parent.ChildIds
            .Select(id => hierarchy.ChunkMap.GetValueOrDefault(id))
            .Where(c => c is not null)
            .ToList()!;
    }

    /// <inheritdoc />
    public IReadOnlyList<ContentChunk> GetSiblings(ContentChunk chunk, ChunkHierarchy hierarchy)
    {
        if (!chunk.ParentId.HasValue)
            return [];

        var parent = hierarchy.ChunkMap.GetValueOrDefault(chunk.ParentId.Value);
        if (parent is null)
            return [];

        return parent.ChildIds
            .Where(id => id != chunk.Id)
            .Select(id => hierarchy.ChunkMap.GetValueOrDefault(id))
            .Where(c => c is not null)
            .ToList()!;
    }

    /// <summary>
    /// Creates child chunks with sliding window approach.
    /// </summary>
    private List<ContentChunk> CreateChildChunks(string content, string? sourceId)
    {
        var chunks = new List<ContentChunk>();
        var position = 0;

        while (position < content.Length)
        {
            var endPosition = Math.Min(position + ChildChunkSize, content.Length);

            // Try to break at sentence or word boundary
            if (endPosition < content.Length)
            {
                endPosition = FindBreakPoint(content, endPosition);
            }

            var chunkContent = content[position..endPosition].Trim();

            if (!string.IsNullOrWhiteSpace(chunkContent))
            {
                chunks.Add(new ContentChunk
                {
                    Id = Guid.NewGuid(),
                    Content = chunkContent,
                    SourceId = sourceId,
                    ChunkType = ChunkType.Child,
                    StartPosition = position,
                    EndPosition = endPosition,
                    SequenceIndex = chunks.Count
                });
            }

            // Move position with overlap
            position = endPosition - ChildChunkOverlap;
            if (position <= chunks.LastOrDefault()?.StartPosition)
            {
                position = endPosition; // Prevent infinite loop
            }
        }

        return chunks;
    }

    /// <summary>
    /// Creates parent chunks by grouping children.
    /// </summary>
    private List<ContentChunk> CreateParentChunks(
        string content,
        List<ContentChunk> children,
        string? sourceId)
    {
        var parents = new List<ContentChunk>();

        for (var i = 0; i < children.Count; i += ChildrenPerParent)
        {
            var groupChildren = children
                .Skip(i)
                .Take(ChildrenPerParent)
                .ToList();

            if (groupChildren.Count == 0)
                continue;

            var startPos = groupChildren.First().StartPosition;
            var endPos = groupChildren.Last().EndPosition;
            var parentContent = content[startPos..endPos];

            var parent = new ContentChunk
            {
                Id = Guid.NewGuid(),
                Content = parentContent,
                SourceId = sourceId,
                ChunkType = ChunkType.Parent,
                StartPosition = startPos,
                EndPosition = endPos,
                SequenceIndex = parents.Count,
                ChildIds = groupChildren.Select(c => c.Id).ToList()
            };

            // Link children to parent
            foreach (var child in groupChildren)
            {
                child.ParentId = parent.Id;
            }

            parents.Add(parent);
        }

        return parents;
    }

    /// <summary>
    /// Finds a good break point (sentence or word boundary).
    /// </summary>
    private static int FindBreakPoint(string content, int targetPosition)
    {
        // Look for sentence boundary first
        var searchStart = Math.Max(0, targetPosition - 100);
        var searchEnd = Math.Min(content.Length, targetPosition + 50);

        for (var i = targetPosition; i >= searchStart; i--)
        {
            var c = content[i];
            if (c == '.' || c == '!' || c == '?' || c == '\n')
            {
                return i + 1;
            }
        }

        // Fall back to word boundary
        for (var i = targetPosition; i >= searchStart; i--)
        {
            if (char.IsWhiteSpace(content[i]))
            {
                return i + 1;
            }
        }

        return targetPosition;
    }

    /// <summary>
    /// Searches parent chunks directly when no children exist.
    /// </summary>
    private async Task<IReadOnlyList<ParentChildSearchResult>> SearchParentsDirectlyAsync(
        string query,
        ChunkHierarchy hierarchy,
        int topK,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        return hierarchy.ParentChunks
            .Select(p => new ParentChildSearchResult
            {
                MatchedChunk = p,
                ParentChunk = p,
                Score = CalculateCosineSimilarity(queryEmbedding, p.Embedding!.Value),
                MatchType = ChunkMatchType.ParentDirect
            })
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// Calculates cosine similarity between query and chunk embeddings.
    /// </summary>
    private static float CalculateCosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var spanA = a.Span;
        var spanB = b.Span;

        var dotProduct = TensorPrimitives.Dot(spanA, spanB);
        var normA = TensorPrimitives.Norm(spanA);
        var normB = TensorPrimitives.Norm(spanB);

        if (normA == 0 || normB == 0)
            return 0f;

        return dotProduct / (normA * normB);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created {ChildCount} child chunks from content of length {Length}")]
    private static partial void LogCreatedChildChunks(ILogger logger, int childCount, int length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created {ParentCount} parent chunks")]
    private static partial void LogCreatedParentChunks(ILogger logger, int parentCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} matching child chunks")]
    private static partial void LogMatchingChildChunks(ILogger logger, int count);
}

/// <summary>
/// Interface for parent-child chunk management.
/// </summary>
public interface IParentChildChunkManager
{
    /// <summary>
    /// Creates a hierarchical chunk structure from content.
    /// </summary>
    Task<ChunkHierarchy> CreateHierarchyAsync(
        string content,
        string? sourceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches chunks and returns results with parent context.
    /// </summary>
    Task<IReadOnlyList<ParentChildSearchResult>> SearchAsync(
        string query,
        ChunkHierarchy hierarchy,
        int topK = 5,
        bool returnParents = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the parent chunk for a child.
    /// </summary>
    ContentChunk? GetParent(ContentChunk child, ChunkHierarchy hierarchy);

    /// <summary>
    /// Gets all children of a parent chunk.
    /// </summary>
    IReadOnlyList<ContentChunk> GetChildren(ContentChunk parent, ChunkHierarchy hierarchy);

    /// <summary>
    /// Gets sibling chunks (other children of the same parent).
    /// </summary>
    IReadOnlyList<ContentChunk> GetSiblings(ContentChunk chunk, ChunkHierarchy hierarchy);
}

/// <summary>
/// Represents a content chunk in the hierarchy.
/// </summary>
public sealed class ContentChunk
{
    /// <summary>
    /// Unique identifier for this chunk.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// The text content of this chunk.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Source document/memory identifier.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Type of chunk (parent or child).
    /// </summary>
    public required ChunkType ChunkType { get; init; }

    /// <summary>
    /// Start position in original content.
    /// </summary>
    public required int StartPosition { get; init; }

    /// <summary>
    /// End position in original content.
    /// </summary>
    public required int EndPosition { get; init; }

    /// <summary>
    /// Sequence index within its level.
    /// </summary>
    public int SequenceIndex { get; init; }

    /// <summary>
    /// Parent chunk ID (for child chunks).
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Child chunk IDs (for parent chunks).
    /// </summary>
    public List<Guid> ChildIds { get; init; } = [];

    /// <summary>
    /// Embedding vector for this chunk.
    /// </summary>
    public ReadOnlyMemory<float>? Embedding { get; set; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Type of chunk in the hierarchy.
/// </summary>
public enum ChunkType
{
    /// <summary>
    /// Parent chunk (larger context).
    /// </summary>
    Parent,

    /// <summary>
    /// Child chunk (precise matching).
    /// </summary>
    Child
}

/// <summary>
/// Represents a complete chunk hierarchy for a document.
/// </summary>
public sealed class ChunkHierarchy
{
    /// <summary>
    /// Source document/memory identifier.
    /// </summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// Original content before chunking.
    /// </summary>
    public required string OriginalContent { get; init; }

    /// <summary>
    /// All parent chunks.
    /// </summary>
    public required IReadOnlyList<ContentChunk> ParentChunks { get; init; }

    /// <summary>
    /// All child chunks.
    /// </summary>
    public required IReadOnlyList<ContentChunk> ChildChunks { get; init; }

    /// <summary>
    /// Map of chunk ID to chunk for quick lookup.
    /// </summary>
    public required IReadOnlyDictionary<Guid, ContentChunk> ChunkMap { get; init; }

    /// <summary>
    /// Total number of chunks.
    /// </summary>
    public int TotalChunks => ParentChunks.Count + ChildChunks.Count;
}

/// <summary>
/// Result of parent-child search.
/// </summary>
public sealed class ParentChildSearchResult
{
    /// <summary>
    /// The chunk that matched the query (usually a child).
    /// </summary>
    public required ContentChunk MatchedChunk { get; init; }

    /// <summary>
    /// The parent chunk providing context (null if child-only mode).
    /// </summary>
    public ContentChunk? ParentChunk { get; init; }

    /// <summary>
    /// Similarity score.
    /// </summary>
    public required float Score { get; init; }

    /// <summary>
    /// Type of match.
    /// </summary>
    public required ChunkMatchType MatchType { get; init; }

    /// <summary>
    /// Content to use (parent if available, otherwise matched chunk).
    /// </summary>
    public string RetrievedContent => ParentChunk?.Content ?? MatchedChunk.Content;
}

/// <summary>
/// Type of chunk match.
/// </summary>
public enum ChunkMatchType
{
    /// <summary>
    /// Child chunk matched, returning parent context.
    /// </summary>
    ChildToParent,

    /// <summary>
    /// Child chunk matched, returning child only.
    /// </summary>
    ChildOnly,

    /// <summary>
    /// Parent chunk matched directly (no children).
    /// </summary>
    ParentDirect
}
