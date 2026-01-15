using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Profile;

/// <summary>
/// In-memory implementation of semantic store service.
/// Stores long-term user facts, preferences, and accumulated knowledge.
/// Implements Tulving's Semantic Memory System for context-independent knowledge.
/// </summary>
/// <remarks>
/// AND logic promotion from Episodic→Semantic:
/// - Minimum 3 confirmations across sessions
/// - Confidence threshold >= 0.8
/// - Consistent evidence across sources
/// </remarks>
public sealed class ArchiveStoreService : IArchiveStore
{
    private readonly IEmbeddingService _embeddingService;
    private readonly SemanticStoreOptions _options;
    private readonly ILogger<ArchiveStoreService> _logger;

    // userId -> (key -> entry)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, SemanticStoreEntry>> _profiles = new();

    public ArchiveStoreService(
        IEmbeddingService embeddingService,
        IOptions<SemanticStoreOptions> options,
        ILogger<ArchiveStoreService> logger)
    {
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<SemanticStoreEntry?> GetAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<SemanticStoreEntry?>(null);
        }

        if (userProfile.TryGetValue(key, out var entry))
        {
            entry.LastAccessedAt = DateTime.UtcNow;
            return Task.FromResult<SemanticStoreEntry?>(entry);
        }

        return Task.FromResult<SemanticStoreEntry?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SemanticStoreEntry>> GetAllAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<IReadOnlyList<SemanticStoreEntry>>([]);
        }

        var entries = userProfile.Values
            .OrderByDescending(e => e.Confidence)
            .ThenByDescending(e => e.ConfirmationCount)
            .ToList();

        return Task.FromResult<IReadOnlyList<SemanticStoreEntry>>(entries);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SemanticStoreEntry>> GetByCategoryAsync(
        string userId,
        SemanticStoreCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<IReadOnlyList<SemanticStoreEntry>>([]);
        }

        var entries = userProfile.Values
            .Where(e => e.Category == category)
            .OrderByDescending(e => e.Confidence)
            .ToList();

        return Task.FromResult<IReadOnlyList<SemanticStoreEntry>>(entries);
    }

    /// <inheritdoc />
    public async Task<bool> SetAsync(
        string userId,
        SemanticStoreEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);

        var userProfile = _profiles.GetOrAdd(userId, _ => new ConcurrentDictionary<string, SemanticStoreEntry>());

        // Check max entries limit
        if (userProfile.Count >= _options.MaxEntriesPerUser && !userProfile.ContainsKey(entry.Key))
        {
            _logger.LogWarning(
                "User {UserId} semantic store at capacity ({Max} entries), cannot add new entry",
                userId, _options.MaxEntriesPerUser);
            return false;
        }

        // Generate embedding for semantic search if enabled
        if (_options.EnableSemanticSearch && !entry.Embedding.HasValue)
        {
            try
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(
                    $"{entry.Key}: {entry.Value}", cancellationToken);
                entry.Embedding = embedding;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for semantic entry {Key}", entry.Key);
            }
        }

        var isUpdate = userProfile.ContainsKey(entry.Key);

        if (isUpdate)
        {
            // Update existing entry
            entry.UpdatedAt = DateTime.UtcNow;
            userProfile[entry.Key] = entry;
            _logger.LogDebug("Updated semantic entry {Key} for user {UserId}", entry.Key, userId);
        }
        else
        {
            // Add new entry
            userProfile[entry.Key] = entry;
            _logger.LogDebug("Created semantic entry {Key} for user {UserId}", entry.Key, userId);
        }

        return isUpdate;
    }

    /// <inheritdoc />
    public Task<SemanticStoreEntry?> ConfirmAsync(
        string userId,
        string key,
        string? evidence = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<SemanticStoreEntry?>(null);
        }

        if (!userProfile.TryGetValue(key, out var entry))
        {
            return Task.FromResult<SemanticStoreEntry?>(null);
        }

        // Increment confirmation count
        entry.ConfirmationCount++;
        entry.UpdatedAt = DateTime.UtcNow;

        // Boost confidence
        entry.Confidence = Math.Min(1.0f, entry.Confidence + _options.ConfidenceBoostPerConfirmation);

        // Add evidence to metadata if provided
        if (!string.IsNullOrEmpty(evidence))
        {
            var evidenceKey = $"evidence_{entry.ConfirmationCount}";
            entry.Metadata[evidenceKey] = evidence;
        }

        _logger.LogDebug(
            "Confirmed semantic entry {Key} for user {UserId}: Count={Count}, Confidence={Confidence}",
            key, userId, entry.ConfirmationCount, entry.Confidence);

        return Task.FromResult<SemanticStoreEntry?>(entry);
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult(false);
        }

        var removed = userProfile.TryRemove(key, out _);
        if (removed)
        {
            _logger.LogDebug("Removed semantic entry {Key} for user {UserId}", key, userId);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemanticStoreEntry>> SearchAsync(
        string userId,
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (!_profiles.TryGetValue(userId, out var userProfile) || userProfile.IsEmpty)
        {
            return [];
        }

        var entries = userProfile.Values.ToList();

        if (_options.EnableSemanticSearch)
        {
            // Semantic search using embeddings
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

            var scoredEntries = entries
                .Where(e => e.Embedding.HasValue)
                .Select(e => (Entry: e, Score: CosineSimilarity(queryEmbedding.Span, e.Embedding!.Value.Span)))
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => x.Entry)
                .ToList();

            // Also include keyword matches that might not have embeddings
            var keywordMatches = entries
                .Where(e => !e.Embedding.HasValue)
                .Where(e => e.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           e.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(limit);

            return scoredEntries.Concat(keywordMatches).Take(limit).ToList();
        }
        else
        {
            // Keyword search
            return entries
                .Where(e => e.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           e.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.Confidence)
                .Take(limit)
                .ToList();
        }
    }

    /// <inheritdoc />
    public SemanticStoreStats GetStats(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_profiles.TryGetValue(userId, out var userProfile) || userProfile.IsEmpty)
        {
            return SemanticStoreStats.Empty(userId);
        }

        var entries = userProfile.Values.ToList();

        var byCategory = entries
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        return new SemanticStoreStats
        {
            UserId = userId,
            TotalEntries = entries.Count,
            ConfirmedEntries = entries.Count(e => e.IsConfirmed),
            EntriesByCategory = byCategory,
            AverageConfidence = entries.Count > 0 ? entries.Average(e => e.Confidence) : 0,
            FirstEntryAt = entries.MinBy(e => e.CreatedAt)?.CreatedAt,
            LastUpdatedAt = entries.MaxBy(e => e.UpdatedAt)?.UpdatedAt
        };
    }

    /// <inheritdoc />
    public bool HasProfile(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return _profiles.TryGetValue(userId, out var profile) && !profile.IsEmpty;
    }

    #region Temporal Query Methods (v0.9.2)

    /// <inheritdoc />
    public Task<IReadOnlyList<SemanticStoreEntry>> GetHistoryAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<IReadOnlyList<SemanticStoreEntry>>([]);
        }

        // Build the supersession chain
        var history = new List<SemanticStoreEntry>();
        var currentKey = key;

        while (!string.IsNullOrEmpty(currentKey))
        {
            if (userProfile.TryGetValue(currentKey, out var entry))
            {
                history.Add(entry);
                currentKey = entry.SupersedesKey;
            }
            else
            {
                break;
            }
        }

        // Also find entries that supersede this one (newer versions)
        var supersedingEntries = userProfile.Values
            .Where(e => e.SupersedesKey == key && !history.Contains(e))
            .OrderByDescending(e => e.Version)
            .ToList();

        // Return newest to oldest
        var result = supersedingEntries.Concat(history)
            .OrderByDescending(e => e.Version)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<SemanticStoreEntry>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SemanticStoreEntry>> GetValidAtAsync(
        string userId,
        DateTime asOfDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<IReadOnlyList<SemanticStoreEntry>>([]);
        }

        var validEntries = userProfile.Values
            .Where(e => e.WasValidAt(asOfDate))
            .OrderByDescending(e => e.Confidence)
            .ThenByDescending(e => e.ConfirmationCount)
            .ToList();

        return Task.FromResult<IReadOnlyList<SemanticStoreEntry>>(validEntries);
    }

    /// <inheritdoc />
    public async Task<SemanticStoreEntry?> ArchiveAndUpdateAsync(
        string userId,
        string key,
        string newValue,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(newValue);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return null;
        }

        if (!userProfile.TryGetValue(key, out var existingEntry))
        {
            return null;
        }

        // Create superseding version
        var newEntry = existingEntry.CreateSupersedingVersion(newValue);

        // Generate new key for archived entry (append version)
        var archivedKey = $"{key}:v{existingEntry.Version}";
        existingEntry.Key.GetType(); // Key is init-only, so we need to create a new entry for archive

        // Archive old entry with versioned key
        var archivedEntry = new SemanticStoreEntry
        {
            Key = archivedKey,
            Value = existingEntry.Value,
            Category = existingEntry.Category,
            Confidence = existingEntry.Confidence,
            ConfirmationCount = existingEntry.ConfirmationCount,
            SourceSessions = existingEntry.SourceSessions.ToList(),
            CreatedAt = existingEntry.CreatedAt,
            UpdatedAt = existingEntry.UpdatedAt,
            LastAccessedAt = existingEntry.LastAccessedAt,
            Embedding = existingEntry.Embedding,
            Metadata = new Dictionary<string, string>(existingEntry.Metadata),
            ValidFrom = existingEntry.ValidFrom,
            ValidTo = DateTime.UtcNow, // Mark as no longer valid
            SupersedesKey = existingEntry.SupersedesKey,
            Version = existingEntry.Version,
            IsActive = false
        };

        // Store archived version
        userProfile[archivedKey] = archivedEntry;

        // Update the main entry with new value
        userProfile[key] = new SemanticStoreEntry
        {
            Key = key,
            Value = newValue,
            Category = existingEntry.Category,
            Confidence = existingEntry.Confidence,
            ConfirmationCount = 1, // Reset for new version
            SourceSessions = [],
            ValidFrom = DateTime.UtcNow,
            ValidTo = null,
            SupersedesKey = archivedKey,
            Version = existingEntry.Version + 1,
            IsActive = true,
            Metadata = new Dictionary<string, string>(existingEntry.Metadata)
        };

        // Generate embedding for the updated entry
        if (_options.EnableSemanticSearch)
        {
            try
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(
                    $"{key}: {newValue}", cancellationToken);
                userProfile[key].Embedding = embedding;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate embedding for updated entry {Key}", key);
            }
        }

        _logger.LogDebug(
            "Archived entry {OldKey} (v{OldVersion}) and created new version {NewKey} (v{NewVersion})",
            archivedKey, existingEntry.Version, key, existingEntry.Version + 1);

        return userProfile[key];
    }

    #endregion

    /// <summary>
    /// Calculates cosine similarity between two embeddings.
    /// </summary>
    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0 : dotProduct / denominator;
    }
}
