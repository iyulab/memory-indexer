using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Deduplication;

/// <summary>
/// Detects and handles duplicate or near-duplicate memories.
/// Uses both content hashing and semantic similarity.
/// </summary>
public sealed partial class DuplicateDetector
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

        LogScanningMemoriesForDuplicates(_logger, memories.Count);

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

        LogFoundDuplicateGroups(_logger, groups.Count);
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
        var allTopics = new HashSet<string>(primary.Topics ?? []);
        foreach (var dup in group.Duplicates)
        {
            if (dup.Topics != null)
            {
                allTopics.UnionWith(dup.Topics);
            }
        }
        primary.Topics = allTopics.Count > 0 ? allTopics.ToList() : null;

        // Update primary
        await _memoryStore.UpdateAsync(primary, cancellationToken);

        // Delete duplicates
        foreach (var duplicate in group.Duplicates.Where(d => d.Id != primary.Id))
        {
            await _memoryStore.DeleteAsync(primary.UserId, duplicate.Id, hardDelete: true, cancellationToken: cancellationToken);
        }

        LogMergedDuplicates(_logger, group.Duplicates.Count, primary.Id);

        return primary;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Scanning {Count} memories for duplicates")]
    private static partial void LogScanningMemoriesForDuplicates(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} duplicate groups")]
    private static partial void LogFoundDuplicateGroups(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Merged {Count} duplicates into {Id}")]
    private static partial void LogMergedDuplicates(ILogger logger, int count, Guid id);

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
