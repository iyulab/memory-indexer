using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Profile;

/// <summary>
/// Semantic store (the user's profile) kept in the configured <see cref="IMemoryStore"/>.
/// Stores long-term user facts, preferences, and accumulated knowledge.
/// Implements Tulving's Semantic Memory System for context-independent knowledge.
/// </summary>
/// <remarks>
/// Each entry is one <see cref="MemoryUnit"/> under the user key <see cref="ArchiveUserKey.For"/>(userId), with a
/// deterministic id derived from the user and the entry key, so the profile persists wherever the memories do (SQLite,
/// a vector store) and a recall for the user never returns it. The entry's own properties travel in the memory's
/// metadata (keys prefixed <c>archive.</c>), because not every store has a column for each of them.
///
/// AND logic promotion from Episodic→Semantic:
/// - Minimum 3 confirmations across sessions
/// - Confidence threshold >= 0.8
/// - Consistent evidence across sources
/// </remarks>
public sealed partial class ArchiveStoreService : IArchiveStore
{
    /// <summary>The metadata key prefix under which an entry's properties are stored.</summary>
    internal const string MetadataPrefix = "archive.";

    /// <summary>The metadata key prefix under which an entry's own metadata pairs are stored.</summary>
    internal const string EntryMetadataPrefix = MetadataPrefix + "meta.";

    /// <summary>The unprefixed metadata key that names the entry key; fact validation reads it to replace a conflicting entry.</summary>
    internal const string SemanticKeyMetadata = "semantic_key";

    private const string KeyMeta = MetadataPrefix + "key";
    private const string CategoryMeta = MetadataPrefix + "category";
    private const string ConfidenceMeta = MetadataPrefix + "confidence";
    private const string ConfirmationsMeta = MetadataPrefix + "confirmations";
    private const string SessionsMeta = MetadataPrefix + "source-sessions";
    private const string CreatedAtMeta = MetadataPrefix + "created-at";
    private const string UpdatedAtMeta = MetadataPrefix + "updated-at";
    private const string LastAccessedAtMeta = MetadataPrefix + "last-accessed-at";

    /// <summary>The finest granularity at which a read's time is written back to the store.</summary>
    private static readonly TimeSpan AccessWriteInterval = TimeSpan.FromHours(1);
    private const string ValidFromMeta = MetadataPrefix + "valid-from";
    private const string ValidToMeta = MetadataPrefix + "valid-to";
    private const string SupersedesMeta = MetadataPrefix + "supersedes";
    private const string VersionMeta = MetadataPrefix + "version";
    private const string ActiveMeta = MetadataPrefix + "active";

    private readonly IMemoryStore _store;
    private readonly IEmbeddingService _embeddingService;
    private readonly SemanticStoreOptions _options;
    private readonly ILogger<ArchiveStoreService> _logger;

    public ArchiveStoreService(
        IMemoryStore store,
        IEmbeddingService embeddingService,
        IOptions<SemanticStoreOptions> options,
        ILogger<ArchiveStoreService> logger)
    {
        _store = store;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SemanticStoreEntry?> GetAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var entry = await ReadAsync(userId, key, cancellationToken);
        if (entry is not null)
        {
            var now = DateTime.UtcNow;

            // Retention and decay judge an entry by when it was last read, in days, so the read time is kept — at most one
            // store write per entry per AccessWriteInterval rather than one per read
            var persist = now - entry.LastAccessedAt >= AccessWriteInterval;
            entry.LastAccessedAt = now;
            if (persist)
            {
                await _store.UpdateAsync(ToUnit(userId, entry), cancellationToken);
            }
        }

        return entry;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemanticStoreEntry>> GetAllAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var entries = await ReadAllAsync(userId, cancellationToken);
        return entries
            .OrderByDescending(e => e.Confidence)
            .ThenByDescending(e => e.ConfirmationCount)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemanticStoreEntry>> GetByCategoryAsync(
        string userId,
        SemanticStoreCategory category,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var entries = await ReadAllAsync(userId, cancellationToken);
        return entries
            .Where(e => e.Category == category)
            .OrderByDescending(e => e.Confidence)
            .ToList();
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

        var archiveKey = ArchiveUserKey.For(userId);
        var isUpdate = await ExistsAsync(archiveKey, IdFor(userId, entry.Key), cancellationToken);

        // Check max entries limit
        if (!isUpdate && await _store.GetCountAsync(archiveKey, cancellationToken) >= _options.MaxEntriesPerUser)
        {
            LogUserUserIdSemanticStoreCapacity(_logger, userId, _options.MaxEntriesPerUser);
            return false;
        }

        // Generate embedding for semantic search if enabled
        if (_options.EnableSemanticSearch && !entry.Embedding.HasValue)
        {
            entry.Embedding = await TryEmbedAsync(entry.Key, entry.Value, updated: false, cancellationToken);
        }

        if (isUpdate)
        {
            // Update existing entry
            entry.UpdatedAt = DateTime.UtcNow;
            await _store.UpdateAsync(ToUnit(userId, entry), cancellationToken);
            LogUpdatedSemanticEntryKeyUser(_logger, new FingerprintedValue(entry.Key), userId);
        }
        else
        {
            // Add new entry
            await _store.StoreAsync(ToUnit(userId, entry), cancellationToken);
            LogCreatedSemanticEntryKeyUser(_logger, new FingerprintedValue(entry.Key), userId);
        }

        return isUpdate;
    }

    /// <inheritdoc />
    public async Task<SemanticStoreEntry?> ConfirmAsync(
        string userId,
        string key,
        string? evidence = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var entry = await ReadAsync(userId, key, cancellationToken);
        if (entry is null)
        {
            return null;
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

        await _store.UpdateAsync(ToUnit(userId, entry), cancellationToken);

        LogConfirmedSemanticEntryKeyUser(_logger, new FingerprintedValue(key), userId, entry.ConfirmationCount, entry.Confidence);

        return entry;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var removed = await _store.DeleteAsync(
            ArchiveUserKey.For(userId), IdFor(userId, key), hardDelete: true, cancellationToken);
        if (removed)
        {
            LogRemovedSemanticEntryKeyUser(_logger, new FingerprintedValue(key), userId);
        }

        return removed;
    }

    /// <inheritdoc />
    public async Task<int> RemoveAllAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var count = await _store.DeleteByUserAsync(ArchiveUserKey.For(userId), hardDelete: true, cancellationToken);
        if (count > 0)
        {
            LogRemovedProfile(_logger, count, userId);
        }

        return count;
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

        var entries = await ReadAllAsync(userId, cancellationToken);
        if (entries.Count == 0)
        {
            return [];
        }

        if (_options.EnableSemanticSearch)
        {
            // Semantic search using embeddings
            var queryEmbedding = await _embeddingService.GenerateQueryEmbeddingAsync(query, cancellationToken);

            // An empty query vector cannot be compared with anything: the keyword search below answers instead
            if (queryEmbedding.Length > 0)
            {
                var hits = await _store.SearchAsync(
                    queryEmbedding,
                    new MemorySearchOptions { UserId = ArchiveUserKey.For(userId), Limit = limit },
                    cancellationToken);

                var scoredEntries = hits
                    .OrderByDescending(h => h.Score)
                    .Select(h => ToEntry(h.Memory))
                    .OfType<SemanticStoreEntry>();

                // Also include keyword matches that might not have embeddings
                var keywordMatches = entries
                    .Where(e => !e.Embedding.HasValue)
                    .Where(e => Matches(e, query))
                    .Take(limit);

                return scoredEntries.Concat(keywordMatches).Take(limit).ToList();
            }
        }

        // Keyword search
        return entries
            .Where(e => Matches(e, query))
            .OrderByDescending(e => e.Confidence)
            .Take(limit)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<SemanticStoreStats> GetStatsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var entries = await ReadAllAsync(userId, cancellationToken);
        if (entries.Count == 0)
        {
            return SemanticStoreStats.Empty(userId);
        }

        var byCategory = entries
            .GroupBy(e => e.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        return new SemanticStoreStats
        {
            UserId = userId,
            TotalEntries = entries.Count,
            ConfirmedEntries = entries.Count(e => e.IsConfirmed),
            EntriesByCategory = byCategory,
            AverageConfidence = entries.Average(e => e.Confidence),
            FirstEntryAt = entries.MinBy(e => e.CreatedAt)?.CreatedAt,
            LastUpdatedAt = entries.MaxBy(e => e.UpdatedAt)?.UpdatedAt
        };
    }

    /// <inheritdoc />
    public async Task<bool> HasProfileAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return await _store.GetCountAsync(ArchiveUserKey.For(userId), cancellationToken) > 0;
    }

    #region Temporal Query Methods (v0.9.2)

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemanticStoreEntry>> GetHistoryAsync(
        string userId,
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var userProfile = (await ReadAllAsync(userId, cancellationToken))
            .GroupBy(e => e.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // Build the supersession chain
        var history = new List<SemanticStoreEntry>();
        var currentKey = key;

        while (!string.IsNullOrEmpty(currentKey))
        {
            if (userProfile.TryGetValue(currentKey, out var entry) && !history.Contains(entry))
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
        return supersedingEntries.Concat(history)
            .OrderByDescending(e => e.Version)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SemanticStoreEntry>> GetValidAtAsync(
        string userId,
        DateTime asOfDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var entries = await ReadAllAsync(userId, cancellationToken);
        return entries
            .Where(e => e.WasValidAt(asOfDate))
            .OrderByDescending(e => e.Confidence)
            .ThenByDescending(e => e.ConfirmationCount)
            .ToList();
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

        var existingEntry = await ReadAsync(userId, key, cancellationToken);
        if (existingEntry is null)
        {
            return null;
        }

        // Generate new key for archived entry (append version)
        var archivedKey = $"{key}:v{existingEntry.Version}";

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

        // Store archived version (a new id; storing keeps the times it carries)
        await _store.StoreAsync(ToUnit(userId, archivedEntry), cancellationToken);

        // Update the main entry with new value
        var updatedEntry = new SemanticStoreEntry
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
            updatedEntry.Embedding = await TryEmbedAsync(key, newValue, updated: true, cancellationToken);
        }

        await _store.UpdateAsync(ToUnit(userId, updatedEntry), cancellationToken);

        LogArchivedEntryOldKeyOldVersionCreated(_logger, archivedKey, existingEntry.Version, key, existingEntry.Version + 1);

        return updatedEntry;
    }

    #endregion

    #region Store mapping

    /// <summary>
    /// The id of the memory that holds <paramref name="key"/> in the profile of <paramref name="userId"/>: a name-based
    /// (RFC 4122 version 5 layout) Guid over SHA-256 of the user and the key, so an entry is found without a lookup table.
    /// </summary>
    internal static Guid IdFor(string userId, string key)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{key}"), hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>
    /// The memory that holds <paramref name="entry"/> in the profile of <paramref name="userId"/>.
    /// </summary>
    internal static MemoryUnit ToUnit(string userId, SemanticStoreEntry entry)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SemanticKeyMetadata] = entry.Key,
            [KeyMeta] = entry.Key,
            [CategoryMeta] = entry.Category.ToString(),
            [ConfidenceMeta] = entry.Confidence.ToString("R", CultureInfo.InvariantCulture),
            [ConfirmationsMeta] = entry.ConfirmationCount.ToString(CultureInfo.InvariantCulture),
            [SessionsMeta] = JsonSerializer.Serialize(entry.SourceSessions, ArchiveJsonContext.Default.ListString),
            [CreatedAtMeta] = FormatTime(entry.CreatedAt),
            [UpdatedAtMeta] = FormatTime(entry.UpdatedAt),
            [LastAccessedAtMeta] = FormatTime(entry.LastAccessedAt),
            [VersionMeta] = entry.Version.ToString(CultureInfo.InvariantCulture),
            [ActiveMeta] = entry.IsActive ? "true" : "false",
        };

        if (entry.ValidFrom is { } validFrom)
            metadata[ValidFromMeta] = FormatTime(validFrom);
        if (entry.ValidTo is { } validTo)
            metadata[ValidToMeta] = FormatTime(validTo);
        if (entry.SupersedesKey is { } supersedes)
            metadata[SupersedesMeta] = supersedes;

        foreach (var (name, value) in entry.Metadata)
        {
            metadata[EntryMetadataPrefix + name] = value;
        }

        return new MemoryUnit
        {
            Id = IdFor(userId, entry.Key),
            UserId = ArchiveUserKey.For(userId),
            Content = entry.Value,
            Embedding = entry.Embedding,
            Type = MemoryType.Fact,
            Tier = Tier.Archive,
            Scope = Scope.User,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
            LastAccessedAt = entry.LastAccessedAt,
            Confidence = entry.Confidence,
            ConfirmCount = entry.ConfirmationCount,
            Metadata = metadata,
        };
    }

    /// <summary>
    /// The profile entry a memory holds, or null when the memory is not one (it carries no entry key).
    /// </summary>
    internal static SemanticStoreEntry? ToEntry(MemoryUnit unit)
    {
        var metadata = unit.Metadata;
        if (metadata is null || !metadata.TryGetValue(KeyMeta, out var key) || string.IsNullOrEmpty(key))
        {
            return null;
        }

        var entryMetadata = new Dictionary<string, string>();
        foreach (var (name, value) in metadata)
        {
            if (name.StartsWith(EntryMetadataPrefix, StringComparison.Ordinal))
            {
                entryMetadata[name[EntryMetadataPrefix.Length..]] = value;
            }
        }

        var sessions = metadata.TryGetValue(SessionsMeta, out var sessionsJson) && !string.IsNullOrEmpty(sessionsJson)
            ? JsonSerializer.Deserialize(sessionsJson, ArchiveJsonContext.Default.ListString) ?? []
            : [];

        return new SemanticStoreEntry
        {
            Key = key,
            Value = unit.Content,
            Category = metadata.TryGetValue(CategoryMeta, out var category)
                && Enum.TryParse<SemanticStoreCategory>(category, ignoreCase: false, out var parsed)
                    ? parsed
                    : SemanticStoreCategory.Other,
            Confidence = ReadFloat(metadata, ConfidenceMeta) ?? unit.Confidence,
            ConfirmationCount = ReadInt(metadata, ConfirmationsMeta) ?? 1,
            SourceSessions = sessions,
            CreatedAt = ReadTime(metadata, CreatedAtMeta) ?? unit.CreatedAt,
            UpdatedAt = ReadTime(metadata, UpdatedAtMeta) ?? unit.UpdatedAt,
            LastAccessedAt = ReadTime(metadata, LastAccessedAtMeta) ?? unit.LastAccessedAt ?? unit.UpdatedAt,
            Embedding = unit.Embedding,
            Metadata = entryMetadata,
            ValidFrom = ReadTime(metadata, ValidFromMeta),
            ValidTo = ReadTime(metadata, ValidToMeta),
            SupersedesKey = metadata.GetValueOrDefault(SupersedesMeta),
            Version = ReadInt(metadata, VersionMeta) ?? 1,
            IsActive = !metadata.TryGetValue(ActiveMeta, out var active) || active != "false",
        };
    }

    private async Task<SemanticStoreEntry?> ReadAsync(string userId, string key, CancellationToken cancellationToken)
    {
        var unit = await _store.GetByIdAsync(ArchiveUserKey.For(userId), IdFor(userId, key), cancellationToken);
        return unit is null || unit.IsDeleted ? null : ToEntry(unit);
    }

    private async Task<List<SemanticStoreEntry>> ReadAllAsync(string userId, CancellationToken cancellationToken)
    {
        var units = await _store.GetAllAsync(ArchiveUserKey.For(userId), options: null, cancellationToken);
        return units
            .Where(u => !u.IsDeleted)
            .Select(ToEntry)
            .OfType<SemanticStoreEntry>()
            .ToList();
    }

    private async Task<bool> ExistsAsync(string archiveKey, Guid id, CancellationToken cancellationToken)
        => await _store.GetByIdAsync(archiveKey, id, cancellationToken) is { IsDeleted: false };

    private async Task<ReadOnlyMemory<float>?> TryEmbedAsync(
        string key, string value, bool updated, CancellationToken cancellationToken)
    {
        try
        {
            return await _embeddingService.GenerateEmbeddingAsync($"{key}: {value}", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (updated)
                LogFailedGenerateEmbeddingUpdatedEntry(_logger, ex, new FingerprintedValue(key));
            else
                LogFailedGenerateEmbeddingSemanticEntry(_logger, ex, new FingerprintedValue(key));
            return null;
        }
    }

    private static bool Matches(SemanticStoreEntry entry, string query)
        => entry.Key.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string FormatTime(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime? ReadTime(Dictionary<string, string> metadata, string name)
        => metadata.TryGetValue(name, out var text)
            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
                ? value
                : null;

    private static int? ReadInt(Dictionary<string, string> metadata, string name)
        => metadata.TryGetValue(name, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

    private static float? ReadFloat(Dictionary<string, string> metadata, string name)
        => metadata.TryGetValue(name, out var text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;

    #endregion

    [LoggerMessage(Level = LogLevel.Warning, Message = "User {UserId} semantic store at capacity ({Max} entries), cannot add new entry")]
    private static partial void LogUserUserIdSemanticStoreCapacity(ILogger logger, string userId, object max);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate embedding for semantic entry {KeyHash}")]
    private static partial void LogFailedGenerateEmbeddingSemanticEntry(ILogger logger, Exception ex, FingerprintedValue keyHash);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updated semantic entry {KeyHash} for user {UserId}")]
    private static partial void LogUpdatedSemanticEntryKeyUser(ILogger logger, FingerprintedValue keyHash, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created semantic entry {KeyHash} for user {UserId}")]
    private static partial void LogCreatedSemanticEntryKeyUser(ILogger logger, FingerprintedValue keyHash, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Confirmed semantic entry {KeyHash} for user {UserId}: Count={Count}, Confidence={Confidence}")]
    private static partial void LogConfirmedSemanticEntryKeyUser(ILogger logger, FingerprintedValue keyHash, string userId, int count, float confidence);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Removed semantic entry {KeyHash} for user {UserId}")]
    private static partial void LogRemovedSemanticEntryKeyUser(ILogger logger, FingerprintedValue keyHash, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed the profile of user {UserId} ({Count} entries)")]
    private static partial void LogRemovedProfile(ILogger logger, int count, string userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate embedding for updated entry {KeyHash}")]
    private static partial void LogFailedGenerateEmbeddingUpdatedEntry(ILogger logger, Exception ex, FingerprintedValue keyHash);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Archived entry {OldKey} (v{OldVersion}) and created new version {NewKey} (v{NewVersion})")]
    private static partial void LogArchivedEntryOldKeyOldVersionCreated(ILogger logger, string oldKey, int oldVersion, string newKey, int newVersion);
}

/// <summary>
/// Source-generated JSON for the profile entry fields kept as JSON in a memory's metadata.
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(List<string>))]
internal sealed partial class ArchiveJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
