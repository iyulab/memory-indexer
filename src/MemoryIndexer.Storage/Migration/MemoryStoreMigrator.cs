using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Storage.Migration;

/// <summary>
/// Utility for migrating memories between different storage backends.
/// Supports InMemory -> SQLite -> Qdrant migration paths.
/// </summary>
public sealed class MemoryStoreMigrator
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

        _logger.LogInformation("Starting migration from {Source} to {Destination}",
            source.GetType().Name, destination.GetType().Name);

        try
        {
            // Ensure destination collection exists
            await destination.EnsureCollectionExistsAsync(cancellationToken);

            // Get user list
            var users = userIds?.ToList();
            if (users == null || users.Count == 0)
            {
                _logger.LogWarning("No user IDs provided. Migration will be skipped.");
                result.Status = MigrationStatus.Skipped;
                result.Message = "No user IDs provided for migration.";
                return result;
            }

            long totalMigrated = 0;
            long totalFailed = 0;
            long totalSkipped = 0;

            foreach (var userId in users)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Migrating memories for user {UserId}", userId);

                var userMemories = await source.GetAllAsync(userId, cancellationToken: cancellationToken);
                var memoryList = userMemories.ToList();

                _logger.LogDebug("Found {Count} memories for user {UserId}", memoryList.Count, userId);

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
                                _logger.LogDebug("Memory {MemoryId} already exists in destination, skipping",
                                    memory.Id);
                                continue;
                            }

                            // Store in destination
                            await destination.StoreAsync(memory, cancellationToken);
                            totalMigrated++;
                        }
                        catch (Exception ex)
                        {
                            totalFailed++;
                            _logger.LogWarning(ex, "Failed to migrate memory {MemoryId}", memory.Id);
                            result.FailedMemoryIds.Add(memory.Id);
                        }
                    }

                    // Report progress
                    progress?.Invoke(totalMigrated + totalFailed + totalSkipped,
                        users.Sum(u => source.GetCountAsync(u).GetAwaiter().GetResult()));
                }

                result.UsersMigrated.Add(userId);
            }

            result.TotalMigrated = totalMigrated;
            result.TotalFailed = totalFailed;
            result.TotalSkipped = totalSkipped;
            result.Duration = DateTime.UtcNow - startTime;
            result.Status = totalFailed == 0 ? MigrationStatus.Success : MigrationStatus.PartialSuccess;
            result.Message = $"Migrated {totalMigrated} memories, {totalSkipped} skipped, {totalFailed} failed.";

            _logger.LogInformation(
                "Migration completed: {Migrated} migrated, {Skipped} skipped, {Failed} failed in {Duration}",
                totalMigrated, totalSkipped, totalFailed, result.Duration);
        }
        catch (OperationCanceledException)
        {
            result.Status = MigrationStatus.Cancelled;
            result.Message = "Migration was cancelled.";
            result.Duration = DateTime.UtcNow - startTime;
            _logger.LogWarning("Migration was cancelled");
        }
        catch (Exception ex)
        {
            result.Status = MigrationStatus.Failed;
            result.Message = $"Migration failed: {ex.Message}";
            result.Duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Migration failed");
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
                _logger.LogWarning(
                    "Count mismatch for user {UserId}: source={Source}, destination={Dest}",
                    userId, sourceCount, destCount);
            }
        }

        result.IsValid = result.UserCounts.Values.All(c => c.Match);
        return result;
    }
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
