using System.Security.Cryptography;
using System.Text;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Services;

/// <summary>
/// Core service for memory operations.
/// Orchestrates storage, embedding, and scoring services.
/// </summary>
public class MemoryService(
    IMemoryStore memoryStore,
    IEmbeddingService embeddingService,
    IScoringService scoringService,
    IDeduplicationService deduplicationService,
    IMemoryPressureMonitor pressureMonitor,
    IMemoryGrowthMonitor growthMonitor,
    IMemoryConflictResolver? conflictResolver,
    IOptions<MemoryIndexerOptions> options)
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

        // Phase 22.1: Importance-based filtering with dynamic thresholds
        var importanceScore = importance ?? 0.5f;
        var growthOptions = options.Value.MemoryGrowth;

        // Calculate dynamic threshold based on memory pressure
        float effectiveThreshold = growthOptions.MinImportanceForStorage;
        if (growthOptions.DynamicThresholds)
        {
            var memoryInfo = pressureMonitor.GetMemoryInfo();
            var utilizationPercent = memoryInfo.UtilizationPercentage * 100;

            effectiveThreshold = utilizationPercent switch
            {
                < 50 => growthOptions.MinImportanceForStorage * growthOptions.LowPressureThresholdMultiplier,
                > 80 => growthOptions.MinImportanceForStorage * growthOptions.HighPressureThresholdMultiplier,
                _ => growthOptions.MinImportanceForStorage
            };
        }

        // Filter out low-importance memories
        if (importanceScore < effectiveThreshold)
        {
            var reason = $"ImportanceScore {importanceScore:F3} < threshold {effectiveThreshold:F3}";

            // Phase 22.1: Record filtered memory for growth monitoring
            await growthMonitor.RecordMemoryStorageAsync(userId, filtered: true, reason, cancellationToken);

            // Return a "filtered" memory without storing (metadata marker)
            return new MemoryUnit
            {
                UserId = userId,
                SessionId = sessionId,
                Content = content,
                Type = type,
                ImportanceScore = importanceScore,
                ContentHash = ComputeContentHash(content),
                Metadata = new Dictionary<string, string>
                {
                    ["Filtered"] = "true",
                    ["Reason"] = reason,
                    ["MemoryPressure"] = pressureMonitor.GetMemoryInfo().UtilizationPercentage.ToString("F3")
                }
            };
        }

        // Phase 22.1: Topic-based pre-storage deduplication
        string? topic = null;
        if (growthOptions.TopicBasedDedup)
        {
            topic = ExtractTopic(content);

            // Check if memory with same topic already exists for this user
            var existingMemories = await memoryStore.GetAllAsync(userId, cancellationToken: cancellationToken);
            var duplicateTopic = existingMemories.FirstOrDefault(m =>
                m.Metadata?.TryGetValue("Topic", out var existingTopic) == true &&
                existingTopic.Equals(topic, StringComparison.OrdinalIgnoreCase));

            if (duplicateTopic != null)
            {
                var reason = $"Duplicate topic: '{topic}'";

                // Phase 22.1: Record filtered memory for growth monitoring
                await growthMonitor.RecordMemoryStorageAsync(userId, filtered: true, reason, cancellationToken);

                // Same topic exists - return filtered memory
                return new MemoryUnit
                {
                    UserId = userId,
                    SessionId = sessionId,
                    Content = content,
                    Type = type,
                    ImportanceScore = importanceScore,
                    ContentHash = ComputeContentHash(content),
                    Metadata = new Dictionary<string, string>
                    {
                        ["Filtered"] = "true",
                        ["Reason"] = reason,
                        ["ExistingMemoryId"] = duplicateTopic.Id.ToString(),
                        ["Topic"] = topic
                    }
                };
            }
        }

        // Phase 21.1: Check for duplicates before storing
        var contentType = metadata?.GetValueOrDefault("ContentType");
        var duplicateCheck = await deduplicationService.CheckForDuplicateAsync(
            content,
            userId,
            contentType: contentType,
            cancellationToken: cancellationToken);

        // Handle duplicate detection results
        if (duplicateCheck.IsDuplicate && duplicateCheck.ExistingMemory != null)
        {
            var existing = duplicateCheck.ExistingMemory;

            switch (duplicateCheck.RecommendedAction)
            {
                case DuplicateAction.Skip:
                    // Exact duplicate - return existing memory without modification
                    return existing;

                case DuplicateAction.Merge:
                    // High similarity - merge content and update
                    existing.Content = $"{existing.Content}\n\n[MERGED] {content}";
                    existing.Embedding = await embeddingService.GenerateEmbeddingAsync(existing.Content, cancellationToken);
                    existing.ContentHash = ComputeContentHash(existing.Content);
                    existing.RecordAccess();
                    existing.MarkUpdated();
                    await memoryStore.UpdateAsync(existing, cancellationToken);
                    return existing;

                case DuplicateAction.Update:
                    // Medium similarity - update with new content
                    existing.Content = content;
                    existing.Embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);
                    existing.ContentHash = ComputeContentHash(content);
                    existing.RecordAccess();
                    existing.MarkUpdated();
                    if (importance.HasValue)
                    {
                        existing.ImportanceScore = importance.Value;
                    }
                    await memoryStore.UpdateAsync(existing, cancellationToken);
                    return existing;

                case DuplicateAction.AddWithRelation:
                    // Low similarity - add as new but link to similar memory
                    metadata ??= [];
                    metadata["RelatedMemoryId"] = existing.Id.ToString();
                    metadata["RelationshipType"] = "similar";
                    metadata["SimilarityScore"] = duplicateCheck.SimilarityScore.ToString("F3");
                    break;

                case DuplicateAction.Add:
                default:
                    // No duplicate action - continue with normal storage
                    break;
            }
        }

        // Generate embedding
        var embedding = await embeddingService.GenerateEmbeddingAsync(content, cancellationToken);

        // Prepare metadata with topic (Phase 22.1)
        metadata ??= [];
        if (topic != null)
        {
            metadata["Topic"] = topic;
        }

        // Create memory unit for conflict resolution
        var memory = new MemoryUnit
        {
            UserId = userId,
            SessionId = sessionId,
            Content = content,
            Embedding = embedding,
            Type = type,
            ImportanceScore = importanceScore, // Use calculated score from Phase 22.1 filtering
            ContentHash = ComputeContentHash(content),
            Metadata = metadata
        };

        // Phase 26: Conflict resolution (semantic conflict detection)
        if (conflictResolver != null)
        {
            // Get similar memories for conflict analysis (use vector search)
            var searchOptions = new MemorySearchOptions
            {
                UserId = userId,
                Limit = 20, // Check last 20 memories for conflicts
                MinScore = 0.65f // Lower threshold than deduplication
            };

            var similarResults = await memoryStore.SearchAsync(embedding, searchOptions, cancellationToken);
            var similarMemories = similarResults.Select(r => r.Memory).ToList();

            if (similarMemories.Count > 0)
            {
                var resolution = await conflictResolver.ResolveAsync(memory, similarMemories, cancellationToken);

                // Handle conflict resolution actions
                switch (resolution.Action)
                {
                    case MemoryAction.NoOp:
                        // Skip storing, conflict detected - existing memory is sufficient
                        metadata["ConflictAction"] = "NoOp";
                        metadata["ConflictReason"] = resolution.Reasoning ?? "Existing memory is sufficient";
                        return similarMemories[0]; // Return most similar existing memory

                    case MemoryAction.Replace:
                        // Replace existing memory with new content
                        if (!string.IsNullOrEmpty(resolution.TargetMemoryId))
                        {
                            var targetId = Guid.Parse(resolution.TargetMemoryId);
                            var existing = await memoryStore.GetByIdAsync(targetId, cancellationToken);
                            if (existing != null)
                            {
                                existing.Content = content;
                                existing.Embedding = embedding;
                                existing.ContentHash = ComputeContentHash(content);
                                existing.ImportanceScore = importanceScore;
                                existing.RecordAccess();
                                existing.MarkUpdated();
                                existing.Metadata ??= [];
                                existing.Metadata["ConflictAction"] = "Replace";
                                existing.Metadata["ConflictType"] = resolution.ConflictType.ToString();
                                existing.Metadata["ConflictReason"] = resolution.Reasoning ?? "Newer content supersedes";
                                await memoryStore.UpdateAsync(existing, cancellationToken);
                                return existing;
                            }
                        }
                        break;

                    case MemoryAction.Merge:
                        // Merge new content with existing memory
                        if (!string.IsNullOrEmpty(resolution.TargetMemoryId))
                        {
                            var targetId = Guid.Parse(resolution.TargetMemoryId);
                            var existing = await memoryStore.GetByIdAsync(targetId, cancellationToken);
                            if (existing != null)
                            {
                                // Use UpdatedContent if provided, otherwise append
                                existing.Content = resolution.UpdatedContent ?? $"{existing.Content}\n\n[MERGED] {content}";
                                existing.Embedding = await embeddingService.GenerateEmbeddingAsync(existing.Content, cancellationToken);
                                existing.ContentHash = ComputeContentHash(existing.Content);
                                existing.RecordAccess();
                                existing.MarkUpdated();
                                existing.Metadata ??= [];
                                existing.Metadata["ConflictAction"] = "Merge";
                                existing.Metadata["ConflictType"] = resolution.ConflictType.ToString();
                                existing.Metadata["ConflictReason"] = resolution.Reasoning ?? "Content merged";
                                await memoryStore.UpdateAsync(existing, cancellationToken);
                                return existing;
                            }
                        }
                        break;

                    case MemoryAction.Archive:
                        // Archive old memory, add new with temporal marker
                        if (!string.IsNullOrEmpty(resolution.TargetMemoryId))
                        {
                            var targetId = Guid.Parse(resolution.TargetMemoryId);
                            var existing = await memoryStore.GetByIdAsync(targetId, cancellationToken);
                            if (existing != null)
                            {
                                // Mark existing as archived
                                existing.Metadata ??= [];
                                existing.Metadata["Archived"] = "true";
                                existing.Metadata["ArchivedReason"] = "Temporal evolution";
                                existing.Metadata["ValidUntil"] = DateTime.UtcNow.ToString("O");
                                await memoryStore.UpdateAsync(existing, cancellationToken);

                                // Add new memory with temporal marker
                                metadata["ConflictAction"] = "Archive";
                                metadata["ConflictType"] = resolution.ConflictType.ToString();
                                metadata["ConflictReason"] = resolution.Reasoning ?? "Temporal evolution";
                                metadata["ReplacedMemoryId"] = targetId.ToString();
                            }
                        }
                        break;

                    case MemoryAction.MarkConflict:
                        // Store both, mark conflict for review
                        metadata["ConflictAction"] = "MarkConflict";
                        metadata["ConflictType"] = resolution.ConflictType.ToString();
                        metadata["ConflictReason"] = resolution.Reasoning ?? "Unresolved conflict";
                        metadata["ConflictConfidence"] = resolution.Confidence.ToString("F2");
                        if (!string.IsNullOrEmpty(resolution.TargetMemoryId))
                        {
                            metadata["ConflictingMemoryId"] = resolution.TargetMemoryId;
                        }
                        break;

                    case MemoryAction.Add:
                    default:
                        // No conflict detected, continue with normal storage
                        break;
                }
            }
        }

        var result = await memoryStore.StoreAsync(memory, cancellationToken);

        // Phase 22.1: Record successful storage for growth monitoring
        await growthMonitor.RecordMemoryStorageAsync(userId, filtered: false, cancellationToken: cancellationToken);

        return result;
    }

    /// <summary>
    /// Recalls memories relevant to the given query.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="query">The search query.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="sessionId">Optional session ID filter.</param>
    /// <param name="types">Optional memory type filter.</param>
    /// <param name="metadataFilter">Optional metadata filter (Phase 28).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Relevant memories with scores.</returns>
    public async Task<IReadOnlyList<MemorySearchResult>> RecallAsync(
        string userId,
        string query,
        int limit = 5,
        string? sessionId = null,
        MemoryType[]? types = null,
        Dictionary<string, string>? metadataFilter = null,
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
            Types = types,
            MetadataFilter = metadataFilter // Phase 28
        };

        var results = await memoryStore.SearchAsync(queryEmbedding, searchOptions, cancellationToken);

        // Re-rank using hybrid scoring (semantic + keyword + content-type boost)
        var rerankedResults = results
            .Select(r =>
            {
                // Record access
                r.Memory.RecordAccess();

                // Calculate hybrid score (includes keyword matching and content-type boost)
                var hybridScore = scoringService.CalculateHybridScore(r.Memory, query, queryEmbedding);

                return new MemorySearchResult
                {
                    Memory = r.Memory,
                    // Blend vector similarity with hybrid score
                    Score = (r.Score + hybridScore) / 2
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

    /// <summary>
    /// Extracts a topic from content for topic-based deduplication.
    /// Phase 22.1: Simple implementation using first sentence or first 50 characters.
    /// </summary>
    private static string ExtractTopic(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        // Remove extra whitespace and normalize
        var normalized = content.Trim();

        // Try to extract first sentence (ending with ., ?, !, or newline)
        var sentenceEnd = normalized.IndexOfAny(['.', '?', '!', '\n']);
        if (sentenceEnd > 0 && sentenceEnd <= 100)
        {
            return normalized[..sentenceEnd].Trim();
        }

        // If no sentence end found or sentence too long, take first 50 chars
        if (normalized.Length > 50)
        {
            return normalized[..50].Trim();
        }

        return normalized;
    }
}
