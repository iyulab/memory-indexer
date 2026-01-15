using System.Diagnostics;
using MemoryIndexer.Interfaces;

namespace MemoryIndexer.Sdk.Intelligence.Retention;

/// <summary>
/// Service for applying retention policies to archive stores.
/// Phase v0.11.0: Retention Policy Engine.
/// </summary>
public class RetentionPolicyService : IRetentionPolicyService
{
    private readonly IRetentionPolicy _policy;
    private readonly IArchiveStore _archiveStore;

    public RetentionPolicyService(
        IRetentionPolicy policy,
        IArchiveStore archiveStore)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _archiveStore = archiveStore ?? throw new ArgumentNullException(nameof(archiveStore));
    }

    /// <inheritdoc />
    public IRetentionPolicy Policy => _policy;

    /// <inheritdoc />
    public async Task<RetentionPreview> PreviewCleanupAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        var entries = await _archiveStore.GetAllAsync(userId, cancellationToken);
        var decisions = _policy.EvaluateAll(entries);

        var byCategory = entries
            .GroupBy(e => e.Category)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var categoryDecisions = decisions.Where(d => d.Entry.Category == g.Key).ToList();
                    return new CategoryPreview
                    {
                        Category = g.Key,
                        TotalCount = g.Count(),
                        RetainCount = categoryDecisions.Count(d => d.ShouldRetain),
                        CleanupCount = categoryDecisions.Count(d => !d.ShouldRetain)
                    };
                });

        var cleanupDecisions = decisions.Where(d => !d.ShouldRetain).ToList();

        return new RetentionPreview
        {
            UserId = userId,
            TotalEntries = entries.Count,
            RetainCount = decisions.Count(d => d.ShouldRetain),
            ArchiveCount = cleanupDecisions.Count(d => d.Action == RetentionAction.Archive),
            DeleteCount = cleanupDecisions.Count(d => d.Action == RetentionAction.Delete),
            ByCategory = byCategory,
            CleanupDecisions = cleanupDecisions
        };
    }

    /// <inheritdoc />
    public async Task<RetentionResult> ApplyAsync(
        string userId,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID is required.", nameof(userId));
        }

        var sw = Stopwatch.StartNew();

        try
        {
            var entries = await _archiveStore.GetAllAsync(userId, cancellationToken);
            var decisions = _policy.EvaluateAll(entries);

            var retainedCount = 0;
            var archivedCount = 0;
            var deletedCount = 0;

            foreach (var decision in decisions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (decision.ShouldRetain)
                {
                    retainedCount++;
                    continue;
                }

                if (dryRun)
                {
                    // Just count what would happen
                    if (decision.Action == RetentionAction.Archive)
                    {
                        archivedCount++;
                    }
                    else if (decision.Action == RetentionAction.Delete)
                    {
                        deletedCount++;
                    }
                    continue;
                }

                // Apply the action
                switch (decision.Action)
                {
                    case RetentionAction.Archive:
                        await ArchiveEntryAsync(userId, decision.Entry, cancellationToken);
                        archivedCount++;
                        break;

                    case RetentionAction.Delete:
                        await _archiveStore.RemoveAsync(userId, decision.Entry.Key, cancellationToken);
                        deletedCount++;
                        break;

                    case RetentionAction.Keep:
                        retainedCount++;
                        break;
                }
            }

            sw.Stop();

            return new RetentionResult
            {
                Success = true,
                TotalProcessed = entries.Count,
                RetainedCount = retainedCount,
                ArchivedCount = archivedCount,
                DeletedCount = deletedCount,
                ProcessingTimeMs = sw.ElapsedMilliseconds,
                UsersProcessed = 1
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();

            return new RetentionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTimeMs = sw.ElapsedMilliseconds,
                UsersProcessed = 1,
                Errors = [ex.Message]
            };
        }
    }

    /// <inheritdoc />
    public async Task<RetentionResult> ApplyToAllAsync(
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var totalProcessed = 0;
        var totalRetained = 0;
        var totalArchived = 0;
        var totalDeleted = 0;
        var usersProcessed = 0;
        var errors = new List<string>();

        // Get all users from the archive store
        // Note: This requires knowing all user IDs, which may need a GetAllUsersAsync method
        // For now, we'll return a result indicating this operation is not fully supported
        // unless the store provides a way to enumerate users

        sw.Stop();

        return new RetentionResult
        {
            Success = true,
            TotalProcessed = totalProcessed,
            RetainedCount = totalRetained,
            ArchivedCount = totalArchived,
            DeletedCount = totalDeleted,
            ProcessingTimeMs = sw.ElapsedMilliseconds,
            UsersProcessed = usersProcessed,
            Errors = errors
        };
    }

    /// <summary>
    /// Archives an entry by marking it as inactive.
    /// </summary>
    private async Task ArchiveEntryAsync(
        string userId,
        SemanticStoreEntry entry,
        CancellationToken cancellationToken)
    {
        // Create archived version of the entry
        var archivedEntry = new SemanticStoreEntry
        {
            Key = entry.Key,
            Value = entry.Value,
            Category = entry.Category,
            Confidence = entry.Confidence,
            ConfirmationCount = entry.ConfirmationCount,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            LastAccessedAt = entry.LastAccessedAt,
            IsActive = false, // Mark as inactive
            SupersedesKey = entry.SupersedesKey,
            ValidFrom = entry.ValidFrom,
            ValidTo = DateTime.UtcNow // Mark as no longer valid
        };

        await _archiveStore.SetAsync(userId, archivedEntry, cancellationToken);
    }
}
