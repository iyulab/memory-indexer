using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Profile;

/// <summary>
/// In-memory implementation of user profile service.
/// Stores long-term user facts, preferences, and accumulated knowledge.
/// </summary>
/// <remarks>
/// AND logic promotion from Session→User:
/// - Minimum 3 confirmations across sessions
/// - Confidence threshold >= 0.8
/// - Consistent evidence across sources
/// </remarks>
public sealed class UserProfileService : IUserProfile
{
    private readonly IEmbeddingService _embeddingService;
    private readonly UserProfileOptions _options;
    private readonly ILogger<UserProfileService> _logger;

    // userId -> (key -> entry)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, UserProfileEntry>> _profiles = new();

    public UserProfileService(
        IEmbeddingService embeddingService,
        IOptions<UserProfileOptions> options,
        ILogger<UserProfileService> logger)
    {
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<UserProfileEntry?> GetAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<UserProfileEntry?>(null);
        }

        if (userProfile.TryGetValue(key, out var entry))
        {
            entry.LastAccessedAt = DateTime.UtcNow;
            return Task.FromResult<UserProfileEntry?>(entry);
        }

        return Task.FromResult<UserProfileEntry?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserProfileEntry>> GetAllAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<IReadOnlyList<UserProfileEntry>>([]);
        }

        var entries = userProfile.Values
            .OrderByDescending(e => e.Confidence)
            .ThenByDescending(e => e.ConfirmationCount)
            .ToList();

        return Task.FromResult<IReadOnlyList<UserProfileEntry>>(entries);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserProfileEntry>> GetByCategoryAsync(
        string userId,
        UserProfileCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<IReadOnlyList<UserProfileEntry>>([]);
        }

        var entries = userProfile.Values
            .Where(e => e.Category == category)
            .OrderByDescending(e => e.Confidence)
            .ToList();

        return Task.FromResult<IReadOnlyList<UserProfileEntry>>(entries);
    }

    /// <inheritdoc />
    public async Task<bool> SetAsync(
        string userId,
        UserProfileEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);

        var userProfile = _profiles.GetOrAdd(userId, _ => new ConcurrentDictionary<string, UserProfileEntry>());

        // Check max entries limit
        if (userProfile.Count >= _options.MaxEntriesPerUser && !userProfile.ContainsKey(entry.Key))
        {
            _logger.LogWarning(
                "User {UserId} profile at capacity ({Max} entries), cannot add new entry",
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
                _logger.LogWarning(ex, "Failed to generate embedding for profile entry {Key}", entry.Key);
            }
        }

        var isUpdate = userProfile.ContainsKey(entry.Key);

        if (isUpdate)
        {
            // Update existing entry
            entry.UpdatedAt = DateTime.UtcNow;
            userProfile[entry.Key] = entry;
            _logger.LogDebug("Updated profile entry {Key} for user {UserId}", entry.Key, userId);
        }
        else
        {
            // Add new entry
            userProfile[entry.Key] = entry;
            _logger.LogDebug("Created profile entry {Key} for user {UserId}", entry.Key, userId);
        }

        return isUpdate;
    }

    /// <inheritdoc />
    public Task<UserProfileEntry?> ConfirmAsync(
        string userId,
        string key,
        string? evidence = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!_profiles.TryGetValue(userId, out var userProfile))
        {
            return Task.FromResult<UserProfileEntry?>(null);
        }

        if (!userProfile.TryGetValue(key, out var entry))
        {
            return Task.FromResult<UserProfileEntry?>(null);
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
            "Confirmed profile entry {Key} for user {UserId}: Count={Count}, Confidence={Confidence}",
            key, userId, entry.ConfirmationCount, entry.Confidence);

        return Task.FromResult<UserProfileEntry?>(entry);
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
            _logger.LogDebug("Removed profile entry {Key} for user {UserId}", key, userId);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserProfileEntry>> SearchAsync(
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
    public UserProfileStats GetStats(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!_profiles.TryGetValue(userId, out var userProfile) || userProfile.IsEmpty)
        {
            return UserProfileStats.Empty(userId);
        }

        var entries = userProfile.Values.ToList();

        var byCategory = entries
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        return new UserProfileStats
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
