using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;

namespace MemoryIndexer.Core.Services;

/// <summary>
/// Core service for memory operations.
/// Orchestrates storage, embedding, and scoring services.
/// </summary>
public class MemoryService(
    IMemoryStore memoryStore,
    IEmbeddingService embeddingService,
    IScoringService scoringService)
{
    /// <summary>
    /// Stores a new memory with automatic embedding generation.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="content">The content to store.</param>
    /// <param name="type">The memory type.</param>
    /// <param name="sessionId">Optional session ID.</param>
    /// <param name="importance">Optional importance score (0.0 to 1.0).</param>
    /// <param name="metadata">Optional metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored memory.</returns>
    public async Task<MemoryUnit> StoreAsync(
        string userId,
        string content,
        MemoryType type = MemoryType.Episodic,
        string? sessionId = null,
        float? importance = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        // Generate embedding
        var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        // Create memory unit
        var memory = new MemoryUnit
        {
            UserId = userId,
            SessionId = sessionId,
            Content = content,
            Embedding = embedding,
            Type = type,
            ImportanceScore = importance ?? 0.5f,
            ContentHash = ComputeContentHash(content),
            Metadata = metadata ?? []
        };

        return await memoryStore.StoreAsync(memory, cancellationToken);
    }

    /// <summary>
    /// Recalls memories relevant to the given query.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="query">The search query.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="sessionId">Optional session ID filter.</param>
    /// <param name="types">Optional memory type filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Relevant memories with scores.</returns>
    public async Task<IReadOnlyList<MemorySearchResult>> RecallAsync(
        string userId,
        string query,
        int limit = 5,
        string? sessionId = null,
        MemoryType[]? types = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // Generate query embedding
        var queryEmbedding = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        // Search for similar memories
        var searchOptions = new MemorySearchOptions
        {
            UserId = userId,
            SessionId = sessionId,
            Limit = limit * 2, // Get extra for re-ranking
            Types = types
        };

        var results = await memoryStore.SearchAsync(queryEmbedding, searchOptions, cancellationToken);

        // Re-rank using combined scoring
        var rerankedResults = results
            .Select(r =>
            {
                // Record access
                r.Memory.RecordAccess();

                // Calculate combined score
                var combinedScore = scoringService.CalculateScore(r.Memory, queryEmbedding);

                return new MemorySearchResult
                {
                    Memory = r.Memory,
                    Score = (r.Score + combinedScore) / 2 // Blend vector similarity with combined score
                };
            })
            .OrderByDescending(r => r.Score)
            .Take(limit)
            .ToList();

        // Update access times in background (fire and forget for performance)
        _ = Task.Run(async () =>
        {
            foreach (var result in rerankedResults)
            {
                await memoryStore.UpdateAsync(result.Memory, CancellationToken.None);
            }
        }, CancellationToken.None);

        return rerankedResults;
    }

    /// <summary>
    /// Gets all memories for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="options">Filter options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's memories.</returns>
    public Task<IReadOnlyList<MemoryUnit>> GetAllAsync(
        string userId,
        MemoryFilterOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return memoryStore.GetAllAsync(userId, options, cancellationToken);
    }

    /// <summary>
    /// Gets a memory by ID.
    /// </summary>
    /// <param name="id">The memory ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memory if found.</returns>
    public Task<MemoryUnit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return memoryStore.GetByIdAsync(id, cancellationToken);
    }

    /// <summary>
    /// Updates a memory's content with new embedding.
    /// </summary>
    /// <param name="id">The memory ID.</param>
    /// <param name="content">The new content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated.</returns>
    public async Task<bool> UpdateContentAsync(
        Guid id,
        string content,
        CancellationToken cancellationToken = default)
    {
        var memory = await memoryStore.GetByIdAsync(id, cancellationToken);
        if (memory is null)
        {
            return false;
        }

        memory.Content = content;
        memory.Embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
        memory.ContentHash = ComputeContentHash(content);
        memory.MarkUpdated();

        return await memoryStore.UpdateAsync(memory, cancellationToken);
    }

    /// <summary>
    /// Updates a memory's importance score.
    /// </summary>
    /// <param name="id">The memory ID.</param>
    /// <param name="importance">The new importance score.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if updated.</returns>
    public async Task<bool> UpdateImportanceAsync(
        Guid id,
        float importance,
        CancellationToken cancellationToken = default)
    {
        var memory = await memoryStore.GetByIdAsync(id, cancellationToken);
        if (memory is null)
        {
            return false;
        }

        memory.ImportanceScore = Math.Clamp(importance, 0f, 1f);
        memory.MarkUpdated();

        return await memoryStore.UpdateAsync(memory, cancellationToken);
    }

    /// <summary>
    /// Deletes a memory.
    /// </summary>
    /// <param name="id">The memory ID.</param>
    /// <param name="hardDelete">If true, permanently removes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if deleted.</returns>
    public Task<bool> DeleteAsync(
        Guid id,
        bool hardDelete = false,
        CancellationToken cancellationToken = default)
    {
        return memoryStore.DeleteAsync(id, hardDelete, cancellationToken);
    }

    /// <summary>
    /// Computes SHA256 hash of content for duplicate detection.
    /// </summary>
    private static string ComputeContentHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
