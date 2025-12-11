using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Security;

/// <summary>
/// In-memory implementation of memory lineage tracking.
/// Suitable for development and testing. For production, use a persistent store.
/// </summary>
public sealed class InMemoryLineageTracker : IMemoryLineageTracker
{
    private readonly ConcurrentDictionary<Guid, List<MemoryLineageEvent>> _eventsByMemory = new();
    private readonly ConcurrentDictionary<Guid, List<MemoryRelation>> _relationsByMemory = new();
    private readonly ILogger<InMemoryLineageTracker> _logger;

    public InMemoryLineageTracker(ILogger<InMemoryLineageTracker> logger)
    {
        _logger = logger;
    }

    public Task RecordCreationAsync(
        Guid memoryId,
        string userId,
        MemorySource source,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var eventDetails = new Dictionary<string, string>
        {
            ["source"] = source.ToString()
        };

        if (metadata != null)
        {
            foreach (var (key, value) in metadata)
            {
                eventDetails[$"metadata_{key}"] = value;
            }
        }

        var lineageEvent = new MemoryLineageEvent
        {
            MemoryId = memoryId,
            EventType = LineageEventType.Created,
            UserId = userId,
            Details = eventDetails
        };

        AddEvent(memoryId, lineageEvent);
        _logger.LogDebug("Recorded creation for memory {MemoryId} by user {UserId}", memoryId, userId);

        return Task.CompletedTask;
    }

    public Task RecordUpdateAsync(
        Guid memoryId,
        string userId,
        MemoryChangeType changeType,
        string? previousContentHash,
        string? newContentHash,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var eventDetails = new Dictionary<string, string>
        {
            ["changeType"] = changeType.ToString()
        };

        if (reason != null)
        {
            eventDetails["reason"] = reason;
        }

        var lineageEvent = new MemoryLineageEvent
        {
            MemoryId = memoryId,
            EventType = LineageEventType.Updated,
            UserId = userId,
            Details = eventDetails,
            PreviousContentHash = previousContentHash,
            NewContentHash = newContentHash
        };

        AddEvent(memoryId, lineageEvent);
        _logger.LogDebug("Recorded update ({ChangeType}) for memory {MemoryId} by user {UserId}", changeType, memoryId, userId);

        return Task.CompletedTask;
    }

    public Task RecordAccessAsync(
        Guid memoryId,
        string userId,
        MemoryAccessType accessType,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        var eventDetails = new Dictionary<string, string>
        {
            ["accessType"] = accessType.ToString()
        };

        if (context != null)
        {
            eventDetails["context"] = context;
        }

        var lineageEvent = new MemoryLineageEvent
        {
            MemoryId = memoryId,
            EventType = LineageEventType.Accessed,
            UserId = userId,
            Details = eventDetails
        };

        AddEvent(memoryId, lineageEvent);
        _logger.LogTrace("Recorded access ({AccessType}) for memory {MemoryId} by user {UserId}", accessType, memoryId, userId);

        return Task.CompletedTask;
    }

    public Task RecordDeletionAsync(
        Guid memoryId,
        string userId,
        bool isHardDelete,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var eventDetails = new Dictionary<string, string>
        {
            ["isHardDelete"] = isHardDelete.ToString()
        };

        if (reason != null)
        {
            eventDetails["reason"] = reason;
        }

        var lineageEvent = new MemoryLineageEvent
        {
            MemoryId = memoryId,
            EventType = LineageEventType.Deleted,
            UserId = userId,
            Details = eventDetails
        };

        AddEvent(memoryId, lineageEvent);
        _logger.LogDebug("Recorded deletion (hard={IsHardDelete}) for memory {MemoryId} by user {UserId}", isHardDelete, memoryId, userId);

        return Task.CompletedTask;
    }

    public Task RecordMergeAsync(
        Guid resultMemoryId,
        IEnumerable<Guid> sourceMemoryIds,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var sourceIds = sourceMemoryIds.ToList();

        var lineageEvent = new MemoryLineageEvent
        {
            MemoryId = resultMemoryId,
            EventType = LineageEventType.Merged,
            UserId = userId,
            RelatedMemoryIds = sourceIds,
            Details = new Dictionary<string, string>
            {
                ["sourceCount"] = sourceIds.Count.ToString()
            }
        };

        AddEvent(resultMemoryId, lineageEvent);

        // Record relations
        foreach (var sourceId in sourceIds)
        {
            AddRelation(resultMemoryId, new MemoryRelation
            {
                RelatedMemoryId = sourceId,
                RelationType = MemoryLineageRelation.MergedFrom,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        _logger.LogDebug("Recorded merge of {SourceCount} memories into {MemoryId} by user {UserId}", sourceIds.Count, resultMemoryId, userId);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MemoryLineageEvent>> GetLineageAsync(
        Guid memoryId,
        LineageQueryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new LineageQueryOptions();

        if (!_eventsByMemory.TryGetValue(memoryId, out var events))
        {
            return Task.FromResult<IReadOnlyList<MemoryLineageEvent>>(Array.Empty<MemoryLineageEvent>());
        }

        var query = events.AsEnumerable();

        if (options.EventTypes != null && options.EventTypes.Length > 0)
        {
            var eventTypes = options.EventTypes.ToHashSet();
            query = query.Where(e => eventTypes.Contains(e.EventType));
        }

        if (options.StartTime.HasValue)
        {
            query = query.Where(e => e.Timestamp >= options.StartTime.Value);
        }

        if (options.EndTime.HasValue)
        {
            query = query.Where(e => e.Timestamp <= options.EndTime.Value);
        }

        var result = query
            .OrderByDescending(e => e.Timestamp)
            .Take(options.Limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<MemoryLineageEvent>>(result);
    }

    public Task<IReadOnlyList<MemoryRelation>> GetRelatedMemoriesAsync(
        Guid memoryId,
        MemoryLineageRelation[]? relationTypes = null,
        CancellationToken cancellationToken = default)
    {
        if (!_relationsByMemory.TryGetValue(memoryId, out var relations))
        {
            return Task.FromResult<IReadOnlyList<MemoryRelation>>(Array.Empty<MemoryRelation>());
        }

        var query = relations.AsEnumerable();

        if (relationTypes != null && relationTypes.Length > 0)
        {
            var types = relationTypes.ToHashSet();
            query = query.Where(r => types.Contains(r.RelationType));
        }

        return Task.FromResult<IReadOnlyList<MemoryRelation>>(query.ToList());
    }

    private void AddEvent(Guid memoryId, MemoryLineageEvent lineageEvent)
    {
        _eventsByMemory.AddOrUpdate(
            memoryId,
            [lineageEvent],
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(lineageEvent);
                    return existing;
                }
            });
    }

    private void AddRelation(Guid memoryId, MemoryRelation relation)
    {
        _relationsByMemory.AddOrUpdate(
            memoryId,
            [relation],
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Add(relation);
                    return existing;
                }
            });
    }
}
