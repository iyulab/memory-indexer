using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Intelligence.Deduplication;

/// <summary>
/// Detects and handles duplicate or near-duplicate memories.
/// Uses both content hashing and semantic similarity.
/// </summary>
public sealed class DuplicateDetector
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<DuplicateDetector> _logger;
    private readonly SearchOptions _options;

    public DuplicateDetector(
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        IOptions<MemoryIndexerOptions> options,
        ILogger<DuplicateDetector> logger)
    {
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _options = options.Value.Search;
        _logger = logger;
    }

    /// <summary>
    /// Checks if content is a duplicate of existing memories.
    /// </summary>
    public async Task<DuplicateCheckResult> CheckForDuplicateAsync(
        string content,
        string userId,
        float? similarityThreshold = null,
        CancellationToken cancellationToken = default)
    {
        var threshold = similarityThreshold ?? _options.DuplicateThreshold;

        // Quick hash check first
        var contentHash = ComputeContentHash(content);
        var exactMatch = await FindExactMatchAsync(userId, contentHash, cancellationToken);

        if (exactMatch != null)
        {
            _logger.LogDebug("Found exact duplicate: {Id}", exactMatch.Id);
            return new DuplicateCheckResult
            {
                IsDuplicate = true,
                DuplicateType = DuplicateType.Exact,
                ExistingMemory = exactMatch,
                SimilarityScore = 1.0f,
                RecommendedAction = DuplicateAction.Skip
            };
        }

        // Semantic similarity check
        var embedding = await _embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        var searchOptions = new MemorySearchOptions
        {
            UserId = userId,
            Limit = 5,
            MinScore = threshold * 0.9f // Slightly lower to catch near-misses
        };

        var similarMemories = await _memoryStore.SearchAsync(embedding, searchOptions, cancellationToken);

        if (similarMemories.Count == 0)
        {
            return new DuplicateCheckResult
            {
                IsDuplicate = false,
                DuplicateType = DuplicateType.None,
                RecommendedAction = DuplicateAction.Add
            };
        }

        var mostSimilar = similarMemories[0];

        if (mostSimilar.Score >= threshold)
        {
            var action = DetermineAction(content, mostSimilar.Memory, mostSimilar.Score);

            _logger.LogDebug(
                "Found semantic duplicate: {Id} with score {Score:F3}, action: {Action}",
                mostSimilar.Memory.Id, mostSimilar.Score, action);

            return new DuplicateCheckResult
            {
                IsDuplicate = true,
                DuplicateType = DuplicateType.Semantic,
                ExistingMemory = mostSimilar.Memory,
                SimilarityScore = mostSimilar.Score,
                RecommendedAction = action,
                SimilarMemories = similarMemories
                    .Where(m => m.Score >= threshold * 0.9f)
                    .ToList()
            };
        }

        return new DuplicateCheckResult
        {
            IsDuplicate = false,
            DuplicateType = DuplicateType.None,
            RecommendedAction = DuplicateAction.Add,
            SimilarMemories = similarMemories.ToList()
        };
    }

    /// <summary>
    /// Finds all duplicates in a user's memories.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateGroup>> FindAllDuplicatesAsync(
        string userId,
        float? similarityThreshold = null,
        CancellationToken cancellationToken = default)
    {
        var threshold = similarityThreshold ?? _options.DuplicateThreshold;
        var memories = await _memoryStore.GetAllAsync(userId, cancellationToken: cancellationToken);

        if (memories.Count <= 1)
            return [];

        _logger.LogInformation("Scanning {Count} memories for duplicates", memories.Count);

        var groups = new List<DuplicateGroup>();
        var processed = new HashSet<Guid>();

        foreach (var memory in memories)
        {
            if (processed.Contains(memory.Id) || !memory.Embedding.HasValue)
                continue;

            var group = new List<MemoryUnit> { memory };

            foreach (var other in memories)
            {
                if (other.Id == memory.Id || processed.Contains(other.Id) || !other.Embedding.HasValue)
                    continue;

                var similarity = CalculateCosineSimilarity(
                    memory.Embedding.Value, other.Embedding.Value);

                if (similarity >= threshold)
                {
                    group.Add(other);
                    processed.Add(other.Id);
                }
            }

            if (group.Count > 1)
            {
                processed.Add(memory.Id);

                // Sort by creation date (oldest first) and importance
                var sorted = group
                    .OrderBy(m => m.CreatedAt)
                    .ThenByDescending(m => m.ImportanceScore)
                    .ToList();

                groups.Add(new DuplicateGroup
                {
                    PrimaryMemory = sorted[0],
                    Duplicates = sorted.Skip(1).ToList()
                });
            }
        }

        _logger.LogInformation("Found {Count} duplicate groups", groups.Count);
        return groups;
    }

    /// <summary>
    /// Merges duplicate memories into a single memory.
    /// </summary>
    public async Task<MemoryUnit> MergeDuplicatesAsync(
        DuplicateGroup group,
        MergeStrategy strategy = MergeStrategy.KeepOldest,
        CancellationToken cancellationToken = default)
    {
        var primary = group.PrimaryMemory;

        switch (strategy)
        {
            case MergeStrategy.KeepOldest:
                // Already sorted by date
                break;

            case MergeStrategy.KeepNewest:
                primary = group.Duplicates
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefault() ?? primary;
                break;

            case MergeStrategy.KeepMostAccessed:
                primary = group.Duplicates
                    .OrderByDescending(m => m.AccessCount)
                    .FirstOrDefault() ?? primary;
                break;

            case MergeStrategy.KeepHighestImportance:
                primary = group.Duplicates
                    .OrderByDescending(m => m.ImportanceScore)
                    .FirstOrDefault() ?? primary;
                break;

            case MergeStrategy.CombineContent:
                // Combine unique information from all duplicates
                primary = CombineMemories(group);
                break;
        }

        // Update primary with combined metadata
        primary.AccessCount = group.Duplicates.Sum(m => m.AccessCount) + primary.AccessCount;
        primary.ImportanceScore = Math.Max(
            primary.ImportanceScore,
            group.Duplicates.Max(m => m.ImportanceScore));

        // Merge topics
        var allTopics = new HashSet<string>(primary.Topics);
        foreach (var dup in group.Duplicates)
        {
            allTopics.UnionWith(dup.Topics);
        }
        primary.Topics = allTopics.ToList();

        // Update primary
        await _memoryStore.UpdateAsync(primary, cancellationToken);

        // Delete duplicates
        foreach (var duplicate in group.Duplicates.Where(d => d.Id != primary.Id))
        {
            await _memoryStore.DeleteAsync(duplicate.Id, hardDelete: true, cancellationToken: cancellationToken);
        }

        _logger.LogInformation(
            "Merged {Count} duplicates into {Id}",
            group.Duplicates.Count, primary.Id);

        return primary;
    }

    /// <summary>
    /// Computes a content hash for quick duplicate detection.
    /// </summary>
    public static string ComputeContentHash(string content)
    {
        // Normalize content before hashing
        var normalized = content
            .ToLowerInvariant()
            .Trim()
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private async Task<MemoryUnit?> FindExactMatchAsync(
        string userId,
        string contentHash,
        CancellationToken cancellationToken)
    {
        var memories = await _memoryStore.GetAllAsync(
            userId,
            new MemoryFilterOptions { Limit = 1000 },
            cancellationToken);

        return memories.FirstOrDefault(m =>
            m.Metadata.TryGetValue("ContentHash", out var hash) &&
            hash?.ToString() == contentHash);
    }

    private static DuplicateAction DetermineAction(
        string newContent,
        MemoryUnit existing,
        float similarity)
    {
        // Very high similarity = skip or update
        if (similarity >= 0.95f)
        {
            // Check if new content is more detailed
            if (newContent.Length > existing.Content.Length * 1.2)
            {
                return DuplicateAction.Update;
            }
            return DuplicateAction.Skip;
        }

        // High similarity = might want to merge
        if (similarity >= 0.85f)
        {
            return DuplicateAction.Merge;
        }

        // Moderate similarity = add but link
        return DuplicateAction.AddWithRelation;
    }

    private static MemoryUnit CombineMemories(DuplicateGroup group)
    {
        var primary = group.PrimaryMemory;
        var allContent = new StringBuilder(primary.Content);

        foreach (var duplicate in group.Duplicates)
        {
            // Only add content that's not already present
            if (!primary.Content.Contains(duplicate.Content))
            {
                var uniqueParts = ExtractUniqueParts(duplicate.Content, primary.Content);
                if (!string.IsNullOrWhiteSpace(uniqueParts))
                {
                    allContent.AppendLine();
                    allContent.Append(uniqueParts);
                }
            }
        }

        primary.Content = allContent.ToString();
        return primary;
    }

    private static string ExtractUniqueParts(string source, string existing)
    {
        // Simple extraction - can be improved with more sophisticated NLP
        var sourceSentences = source.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var existingSentences = new HashSet<string>(
            existing.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant()));

        var unique = sourceSentences
            .Where(s => !existingSentences.Contains(s.Trim().ToLowerInvariant()))
            .Select(s => s.Trim());

        return string.Join(". ", unique);
    }

    private static float CalculateCosineSimilarity(
        ReadOnlyMemory<float> embedding1,
        ReadOnlyMemory<float> embedding2)
    {
        var span1 = embedding1.Span;
        var span2 = embedding2.Span;

        if (span1.Length != span2.Length)
            return 0f;

        var dotProduct = TensorPrimitives.Dot(span1, span2);
        var norm1 = TensorPrimitives.Norm(span1);
        var norm2 = TensorPrimitives.Norm(span2);

        if (norm1 == 0 || norm2 == 0)
            return 0f;

        return dotProduct / (norm1 * norm2);
    }
}

/// <summary>
/// Result of a duplicate check operation.
/// </summary>
public sealed class DuplicateCheckResult
{
    /// <summary>
    /// Whether a duplicate was found.
    /// </summary>
    public required bool IsDuplicate { get; init; }

    /// <summary>
    /// Type of duplicate found.
    /// </summary>
    public required DuplicateType DuplicateType { get; init; }

    /// <summary>
    /// The existing memory that is a duplicate (if found).
    /// </summary>
    public MemoryUnit? ExistingMemory { get; init; }

    /// <summary>
    /// Similarity score with the most similar memory.
    /// </summary>
    public float SimilarityScore { get; init; }

    /// <summary>
    /// Recommended action to take.
    /// </summary>
    public required DuplicateAction RecommendedAction { get; init; }

    /// <summary>
    /// List of similar (but not duplicate) memories.
    /// </summary>
    public List<MemorySearchResult> SimilarMemories { get; init; } = [];
}

/// <summary>
/// Type of duplicate detection.
/// </summary>
public enum DuplicateType
{
    /// <summary>
    /// No duplicate found.
    /// </summary>
    None,

    /// <summary>
    /// Exact content match (hash-based).
    /// </summary>
    Exact,

    /// <summary>
    /// Semantic/meaning-based match.
    /// </summary>
    Semantic
}

/// <summary>
/// Recommended action for handling duplicates.
/// </summary>
public enum DuplicateAction
{
    /// <summary>
    /// Add as new memory.
    /// </summary>
    Add,

    /// <summary>
    /// Skip - don't store the new content.
    /// </summary>
    Skip,

    /// <summary>
    /// Update the existing memory with new content.
    /// </summary>
    Update,

    /// <summary>
    /// Merge new and existing into one memory.
    /// </summary>
    Merge,

    /// <summary>
    /// Add but create a relationship to the similar memory.
    /// </summary>
    AddWithRelation
}

/// <summary>
/// A group of duplicate memories.
/// </summary>
public sealed class DuplicateGroup
{
    /// <summary>
    /// The primary (canonical) memory to keep.
    /// </summary>
    public required MemoryUnit PrimaryMemory { get; init; }

    /// <summary>
    /// The duplicate memories.
    /// </summary>
    public required List<MemoryUnit> Duplicates { get; init; }
}

/// <summary>
/// Strategy for merging duplicate memories.
/// </summary>
public enum MergeStrategy
{
    /// <summary>
    /// Keep the oldest memory.
    /// </summary>
    KeepOldest,

    /// <summary>
    /// Keep the newest memory.
    /// </summary>
    KeepNewest,

    /// <summary>
    /// Keep the most accessed memory.
    /// </summary>
    KeepMostAccessed,

    /// <summary>
    /// Keep the memory with highest importance.
    /// </summary>
    KeepHighestImportance,

    /// <summary>
    /// Combine content from all duplicates.
    /// </summary>
    CombineContent
}
