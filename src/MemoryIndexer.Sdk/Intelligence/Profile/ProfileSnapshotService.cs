using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Profile;

/// <summary>
/// Service for managing user profile snapshots.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public partial class ProfileSnapshotService : IProfileSnapshotService
{
    private readonly IArchiveStore _archiveStore;
    private readonly IConfidenceDecayStrategy _decayStrategy;
    private readonly ILogger<ProfileSnapshotService> _logger;

    // In-memory storage for snapshots (in production, use persistent storage)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, ProfileSnapshot>> _snapshots = new();

    // Track last session snapshots per user
    private readonly ConcurrentDictionary<string, string> _lastSessionIds = new();

    public ProfileSnapshotService(
        IArchiveStore archiveStore,
        IConfidenceDecayStrategy decayStrategy,
        ILogger<ProfileSnapshotService> logger)
    {
        _archiveStore = archiveStore;
        _decayStrategy = decayStrategy;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProfileSnapshot> CreateSnapshotAsync(
        string userId,
        string? label = null,
        CancellationToken cancellationToken = default)
    {
        // Get all current facts
        var allFacts = await _archiveStore.GetAllAsync(userId, cancellationToken);
        var factsList = allFacts.ToList();

        // Calculate stats
        var stats = CalculateStats(factsList);

        // Create snapshot
        var snapshot = new ProfileSnapshot
        {
            UserId = userId,
            Label = label,
            Facts = factsList,
            Stats = stats
        };

        // Store snapshot
        var userSnapshots = _snapshots.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, ProfileSnapshot>());
        userSnapshots[snapshot.Id] = snapshot;

        LogCreatedProfileSnapshotSnapshotIdUser(_logger, snapshot.Id, userId, factsList.Count);

        return snapshot;
    }

    /// <inheritdoc />
    public Task<ProfileSnapshot?> GetSnapshotAsync(
        string userId,
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        if (_snapshots.TryGetValue(userId, out var userSnapshots) &&
            userSnapshots.TryGetValue(snapshotId, out var snapshot))
        {
            return Task.FromResult<ProfileSnapshot?>(snapshot);
        }

        return Task.FromResult<ProfileSnapshot?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ProfileSnapshotSummary>> ListSnapshotsAsync(
        string userId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var summaries = new List<ProfileSnapshotSummary>();

        if (_snapshots.TryGetValue(userId, out var userSnapshots))
        {
            summaries = userSnapshots.Values
                .OrderByDescending(s => s.CreatedAt)
                .Take(limit)
                .Select(s => new ProfileSnapshotSummary
                {
                    Id = s.Id,
                    UserId = s.UserId,
                    Label = s.Label,
                    CreatedAt = s.CreatedAt,
                    FactCount = s.Facts.Count,
                    SessionId = s.SessionId
                })
                .ToList();
        }

        return Task.FromResult<IReadOnlyList<ProfileSnapshotSummary>>(summaries);
    }

    /// <inheritdoc />
    public async Task<ProfileDiff> CompareSnapshotsAsync(
        string userId,
        Guid olderSnapshotId,
        Guid? newerSnapshotId = null,
        CancellationToken cancellationToken = default)
    {
        // Get older snapshot
        var olderSnapshot = await GetSnapshotAsync(userId, olderSnapshotId, cancellationToken);
        if (olderSnapshot == null)
        {
            throw new ArgumentException($"Snapshot {olderSnapshotId} not found", nameof(olderSnapshotId));
        }

        // Get newer snapshot or current state
        IReadOnlyList<SemanticStoreEntry> newerFacts;
        DateTime newerTimestamp;

        if (newerSnapshotId.HasValue)
        {
            var newerSnapshot = await GetSnapshotAsync(userId, newerSnapshotId.Value, cancellationToken);
            if (newerSnapshot == null)
            {
                throw new ArgumentException($"Snapshot {newerSnapshotId} not found", nameof(newerSnapshotId));
            }
            newerFacts = newerSnapshot.Facts;
            newerTimestamp = newerSnapshot.CreatedAt;
        }
        else
        {
            newerFacts = await _archiveStore.GetAllAsync(userId, cancellationToken);
            newerTimestamp = DateTime.UtcNow;
        }

        return ComputeDiff(userId, olderSnapshot.Facts, newerFacts,
            olderSnapshot.Id, newerSnapshotId,
            olderSnapshot.CreatedAt, newerTimestamp);
    }

    /// <inheritdoc />
    public async Task<ProfileDiff> GetChangeSinceLastSessionAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        // Get last session's snapshot if exists
        if (_lastSessionIds.TryGetValue(userId, out var lastSessionId) && lastSessionId != sessionId)
        {
            // Find snapshot for last session
            if (_snapshots.TryGetValue(userId, out var userSnapshots))
            {
                var lastSessionSnapshot = userSnapshots.Values
                    .FirstOrDefault(s => s.SessionId == lastSessionId);

                if (lastSessionSnapshot != null)
                {
                    var currentFacts = await _archiveStore.GetAllAsync(userId, cancellationToken);
                    return ComputeDiff(userId, lastSessionSnapshot.Facts, currentFacts,
                        lastSessionSnapshot.Id, null,
                        lastSessionSnapshot.CreatedAt, DateTime.UtcNow);
                }
            }
        }

        // No previous session or same session - return empty diff
        var facts = await _archiveStore.GetAllAsync(userId, cancellationToken);
        return new ProfileDiff
        {
            UserId = userId,
            OlderSnapshotId = null,
            NewerSnapshotId = null,
            OlderTimestamp = DateTime.UtcNow,
            NewerTimestamp = DateTime.UtcNow,
            Summary = new DiffSummary()
        };
    }

    /// <inheritdoc />
    public Task<bool> DeleteSnapshotAsync(
        string userId,
        Guid snapshotId,
        CancellationToken cancellationToken = default)
    {
        if (_snapshots.TryGetValue(userId, out var userSnapshots))
        {
            var removed = userSnapshots.TryRemove(snapshotId, out _);
            if (removed)
            {
                LogDeletedSnapshotSnapshotIdUserUserId(_logger, snapshotId, userId);
            }
            return Task.FromResult(removed);
        }

        return Task.FromResult(false);
    }

    /// <summary>
    /// Creates a session-linked snapshot and updates last session tracking.
    /// </summary>
    public async Task<ProfileSnapshot> CreateSessionSnapshotAsync(
        string userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await CreateSnapshotAsync(userId, $"Session: {sessionId}", cancellationToken);
        snapshot.SessionId = sessionId;

        // Update last session tracking
        _lastSessionIds[userId] = sessionId;

        return snapshot;
    }

    #region Helper Methods

    private ProfileStats CalculateStats(List<SemanticStoreEntry> facts)
    {
        var activeFacts = facts.Where(f => f.IsActive).ToList();
        var staleFacts = activeFacts.Count(f => _decayStrategy.NeedsReconfirmation(f));

        var byCategory = activeFacts
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        // Calculate completeness score based on category coverage
        var totalCategories = Enum.GetValues<SemanticStoreCategory>().Length;
        var coveredCategories = byCategory.Count;
        var completeness = (float)coveredCategories / totalCategories;

        // Adjust by fact count (more facts in key categories = more complete)
        var keyCategories = new[]
        {
            SemanticStoreCategory.Fact,
            SemanticStoreCategory.Preference,
            SemanticStoreCategory.Skill,
            SemanticStoreCategory.Work
        };
        var keyFactCount = keyCategories.Sum(c => byCategory.GetValueOrDefault(c, 0));
        var keyCategoryBonus = Math.Min(0.3f, keyFactCount * 0.05f);
        completeness = Math.Min(1f, completeness + keyCategoryBonus);

        return new ProfileStats
        {
            TotalFacts = facts.Count,
            ConfirmedFacts = activeFacts.Count(f => f.IsConfirmed),
            StaleFactCount = staleFacts,
            AverageConfidence = activeFacts.Count > 0
                ? activeFacts.Average(f => f.Confidence)
                : 0f,
            ByCategory = byCategory,
            CompletenessScore = completeness
        };
    }

    private static ProfileDiff ComputeDiff(
        string userId,
        IReadOnlyList<SemanticStoreEntry> olderFacts,
        IReadOnlyList<SemanticStoreEntry> newerFacts,
        Guid? olderSnapshotId,
        Guid? newerSnapshotId,
        DateTime olderTimestamp,
        DateTime newerTimestamp)
    {
        var olderByKey = olderFacts.ToDictionary(f => f.Key);
        var newerByKey = newerFacts.ToDictionary(f => f.Key);

        var added = new List<SemanticStoreEntry>();
        var removed = new List<SemanticStoreEntry>();
        var modified = new List<FactChange>();

        // Find added and modified
        foreach (var (key, newer) in newerByKey)
        {
            if (olderByKey.TryGetValue(key, out var older))
            {
                // Check if modified
                if (!string.Equals(older.Value, newer.Value, StringComparison.Ordinal) || Math.Abs(older.Confidence - newer.Confidence) > 0.01f)
                {
                    var changeType = DetermineChangeType(older, newer);
                    modified.Add(new FactChange
                    {
                        Key = key,
                        OldValue = older.Value,
                        NewValue = newer.Value,
                        OldConfidence = older.Confidence,
                        NewConfidence = newer.Confidence,
                        Category = newer.Category,
                        ChangeType = changeType
                    });
                }
            }
            else
            {
                added.Add(newer);
            }
        }

        // Find removed
        foreach (var (key, older) in olderByKey)
        {
            if (!newerByKey.ContainsKey(key))
            {
                removed.Add(older);
            }
        }

        // Build category summary
        var categoryChanges = added.Concat(removed)
            .Concat(modified.Select(m => newerByKey[m.Key]))
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        return new ProfileDiff
        {
            UserId = userId,
            OlderSnapshotId = olderSnapshotId,
            NewerSnapshotId = newerSnapshotId,
            OlderTimestamp = olderTimestamp,
            NewerTimestamp = newerTimestamp,
            AddedFacts = added,
            RemovedFacts = removed,
            ModifiedFacts = modified,
            Summary = new DiffSummary
            {
                AddedCount = added.Count,
                RemovedCount = removed.Count,
                ModifiedCount = modified.Count,
                ByCategory = categoryChanges
            }
        };
    }

    private static FactChangeType DetermineChangeType(SemanticStoreEntry older, SemanticStoreEntry newer)
    {
        if (!string.Equals(older.Value, newer.Value, StringComparison.Ordinal))
            return FactChangeType.ValueChange;

        if (newer.Confidence > older.Confidence)
            return newer.IsConfirmed && !older.IsConfirmed
                ? FactChangeType.Confirmed
                : FactChangeType.ConfidenceIncrease;

        if (newer.Confidence < older.Confidence)
            return !newer.IsActive && older.IsActive
                ? FactChangeType.Invalidated
                : FactChangeType.ConfidenceDecrease;

        return FactChangeType.ValueChange;
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created profile snapshot {SnapshotId} for user {UserId} with {FactCount} facts")]
    private static partial void LogCreatedProfileSnapshotSnapshotIdUser(ILogger logger, Guid snapshotId, string userId, int factCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted snapshot {SnapshotId} for user {UserId}")]
    private static partial void LogDeletedSnapshotSnapshotIdUserUserId(ILogger logger, Guid snapshotId, string userId);
}
