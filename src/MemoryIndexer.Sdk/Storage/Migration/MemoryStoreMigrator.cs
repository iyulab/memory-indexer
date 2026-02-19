using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Storage.Migration;

/// <summary>
/// Utility for migrating memories between different storage backends.
/// Supports any IMemoryStore implementation migration paths.
/// </summary>
public sealed partial class MemoryStoreMigrator
{
    private readonly ILogger<MemoryStoreMigrator> _logger;

    public MemoryStoreMigrator(ILogger<MemoryStoreMigrator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Migrates all memories from source to destination store.
    /// </summary>
    /// <param name="source">Source memory store.</param>
    /// <param name="destination">Destination memory store.</param>
    /// <param name="userIds">User IDs to migrate. If null, discovers all users.</param>
    /// <param name="batchSize">Number of memories to process per batch.</param>
    /// <param name="progress">Optional progress callback (current, total).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Migration result with statistics.</returns>
    public async Task<MigrationResult> MigrateAsync(
        IMemoryStore source,
        IMemoryStore destination,
        IEnumerable<string>? userIds = null,
        int batchSize = 100,
        Action<long, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new MigrationResult();
        var startTime = DateTime.UtcNow;

        var sourceValue = source.GetType().Name;
        var destinationValue = destination.GetType().Name;
        LogStartingMigrationSourceDestination(_logger, sourceValue, destinationValue);

        try
        {
            // Ensure destination collection exists
            await destination.EnsureCollectionExistsAsync(cancellationToken);

            // Get user list
            var users = userIds?.ToList();
            if (users == null || users.Count == 0)
            {
                LogUserIDsProvidedMigrationWill(_logger);
                result.Status = MigrationStatus.Skipped;
                result.Message = "No user IDs provided for migration.";
                return result;
            }

            long totalMigrated = 0;
            long totalFailed = 0;
            long totalSkipped = 0;

            // Pre-compute total count for progress reporting (avoid sync-over-async in loop)
            long totalCount = 0;
            if (progress != null)
            {
                foreach (var userId in users)
                {
                    totalCount += await source.GetCountAsync(userId, cancellationToken);
                }
            }

            foreach (var userId in users)
            {
                cancellationToken.ThrowIfCancellationRequested();

                LogMigratingMemoriesUserUserId(_logger, userId);

                var userMemories = await source.GetAllAsync(userId, cancellationToken: cancellationToken);
                var memoryList = userMemories.ToList();

                LogFoundCountMemoriesUserUserId(_logger, memoryList.Count, userId);

                // Process in batches
                for (var i = 0; i < memoryList.Count; i += batchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = memoryList.Skip(i).Take(batchSize).ToList();

                    foreach (var memory in batch)
                    {
                        try
                        {
                            // Check if already exists in destination
                            var existing = await destination.GetByIdAsync(memory.Id, cancellationToken);
                            if (existing != null)
                            {
                                totalSkipped++;
                                LogMemoryMemoryIdAlreadyExistsDestination(_logger, memory.Id);
                                continue;
                            }

                            // Store in destination
                            await destination.StoreAsync(memory, cancellationToken);
                            totalMigrated++;
                        }
                        catch (Exception ex)
                        {
                            totalFailed++;
                            LogFailedMigrateMemoryMemoryId(_logger, ex, memory.Id);
                            result.FailedMemoryIds.Add(memory.Id);
                        }
                    }

                    // Report progress
                    progress?.Invoke(totalMigrated + totalFailed + totalSkipped, totalCount);
                }

                result.UsersMigrated.Add(userId);
            }

            result.TotalMigrated = totalMigrated;
            result.TotalFailed = totalFailed;
            result.TotalSkipped = totalSkipped;
            result.Duration = DateTime.UtcNow - startTime;
            result.Status = totalFailed == 0 ? MigrationStatus.Success : MigrationStatus.PartialSuccess;
            result.Message = $"Migrated {totalMigrated} memories, {totalSkipped} skipped, {totalFailed} failed.";

            LogMigrationCompletedMigratedMigratedSkipped(_logger, totalMigrated, totalSkipped, totalFailed, result.Duration);
        }
        catch (OperationCanceledException)
        {
            result.Status = MigrationStatus.Cancelled;
            result.Message = "Migration was cancelled.";
            result.Duration = DateTime.UtcNow - startTime;
            LogMigrationCancelled(_logger);
        }
        catch (Exception ex)
        {
            result.Status = MigrationStatus.Failed;
            result.Message = $"Migration failed: {ex.Message}";
            result.Duration = DateTime.UtcNow - startTime;
            LogMigrationFailed(_logger, ex);
        }

        return result;
    }

    /// <summary>
    /// Validates that memories were correctly migrated by comparing counts.
    /// </summary>
    /// <param name="source">Source memory store.</param>
    /// <param name="destination">Destination memory store.</param>
    /// <param name="userIds">User IDs to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result.</returns>
    public async Task<ValidationResult> ValidateAsync(
        IMemoryStore source,
        IMemoryStore destination,
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceCount = await source.GetCountAsync(userId, cancellationToken);
            var destCount = await destination.GetCountAsync(userId, cancellationToken);

            result.UserCounts[userId] = new CountComparison
            {
                SourceCount = sourceCount,
                DestinationCount = destCount,
                Match = sourceCount == destCount
            };

            if (sourceCount != destCount)
            {
                LogCountMismatchUserUserIdSource(_logger, userId, sourceCount, destCount);
            }
        }

        result.IsValid = result.UserCounts.Values.All(c => c.Match);
        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting migration from {Source} to {Destination}")]
    private static partial void LogStartingMigrationSourceDestination(ILogger logger, string source, string destination);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No user IDs provided. Migration will be skipped.")]
    private static partial void LogUserIDsProvidedMigrationWill(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Migrating memories for user {UserId}")]
    private static partial void LogMigratingMemoriesUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} memories for user {UserId}")]
    private static partial void LogFoundCountMemoriesUserUserId(ILogger logger, long count, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Memory {MemoryId} already exists in destination, skipping")]
    private static partial void LogMemoryMemoryIdAlreadyExistsDestination(ILogger logger, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to migrate memory {MemoryId}")]
    private static partial void LogFailedMigrateMemoryMemoryId(ILogger logger, Exception ex, Guid memoryId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration completed: {Migrated} migrated, {Skipped} skipped, {Failed} failed in {Duration}")]
    private static partial void LogMigrationCompletedMigratedMigratedSkipped(ILogger logger, long migrated, long skipped, long failed, TimeSpan duration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration was cancelled")]
    private static partial void LogMigrationCancelled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration failed")]
    private static partial void LogMigrationFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Count mismatch for user {UserId}: source={Source}, destination={Dest}")]
    private static partial void LogCountMismatchUserUserIdSource(ILogger logger, string userId, long source, long dest);
}

/// <summary>
/// Result of a migration operation.
/// </summary>
public sealed class MigrationResult
{
    /// <summary>
    /// Migration status.
    /// </summary>
    public MigrationStatus Status { get; set; } = MigrationStatus.Pending;

    /// <summary>
    /// Human-readable message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Number of memories successfully migrated.
    /// </summary>
    public long TotalMigrated { get; set; }

    /// <summary>
    /// Number of memories that failed to migrate.
    /// </summary>
    public long TotalFailed { get; set; }

    /// <summary>
    /// Number of memories skipped (already exist in destination).
    /// </summary>
    public long TotalSkipped { get; set; }

    /// <summary>
    /// Time taken for migration.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// List of user IDs that were migrated.
    /// </summary>
    public List<string> UsersMigrated { get; set; } = [];

    /// <summary>
    /// List of memory IDs that failed to migrate.
    /// </summary>
    public List<Guid> FailedMemoryIds { get; set; } = [];
}

/// <summary>
/// Migration status enum.
/// </summary>
public enum MigrationStatus
{
    Pending,
    Success,
    PartialSuccess,
    Failed,
    Cancelled,
    Skipped
}

/// <summary>
/// Result of migration validation.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Whether all counts match.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Count comparison per user.
    /// </summary>
    public Dictionary<string, CountComparison> UserCounts { get; set; } = [];
}

/// <summary>
/// Count comparison between source and destination.
/// </summary>
public sealed class CountComparison
{
    public long SourceCount { get; set; }
    public long DestinationCount { get; set; }
    public bool Match { get; set; }
}
