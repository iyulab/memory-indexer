using System.Collections.Concurrent;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Storage.InMemory;

/// <summary>
/// In-memory implementation of ITemporalEntityStore.
/// Supports bitemporal queries and version chains.
/// </summary>
public sealed class InMemoryTemporalEntityStore(ILogger<InMemoryTemporalEntityStore> logger) : ITemporalEntityStore
{
    private readonly ConcurrentDictionary<Guid, EntityTriple> _triples = new();

    /// <inheritdoc />
    public Task<EntityTriple> StoreAsync(EntityTriple triple, CancellationToken cancellationToken = default)
    {
        triple.Id = triple.Id == Guid.Empty ? Guid.NewGuid() : triple.Id;
        triple.CreatedAt = DateTime.UtcNow;
        triple.UpdatedAt = DateTime.UtcNow;
        triple.TransactionTime = DateTime.UtcNow;

        _triples[triple.Id] = triple;
        logger.LogDebug("Stored entity triple {TripleId}: {Subject} - {Predicate} - {Object}",
            triple.Id, triple.Subject, triple.Predicate, triple.ObjectValue);

        return Task.FromResult(triple);
    }

    /// <inheritdoc />
    public Task<EntityTriple?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _triples.TryGetValue(id, out var triple);
        return Task.FromResult(triple);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityTriple>> QueryAsync(
        TemporalEntityQuery query,
        CancellationToken cancellationToken = default)
    {
        var results = _triples.Values
            .Where(t => t.UserId == query.UserId)
            .Where(t => query.IncludeInactive || t.IsActive)
            .Where(t => !query.CurrentVersionsOnly || !IsSuperseded(t))
            .Where(t => string.IsNullOrEmpty(query.Subject) ||
                        t.Subject.Contains(query.Subject, StringComparison.OrdinalIgnoreCase))
            .Where(t => string.IsNullOrEmpty(query.Predicate) ||
                        t.Predicate.Contains(query.Predicate, StringComparison.OrdinalIgnoreCase))
            .Where(t => string.IsNullOrEmpty(query.ObjectValue) ||
                        t.ObjectValue.Contains(query.ObjectValue, StringComparison.OrdinalIgnoreCase))
            .Where(t => !query.AsOfDate.HasValue || t.WasValidAt(query.AsOfDate.Value))
            .Where(t => !query.CreatedAfter.HasValue || t.CreatedAt >= query.CreatedAfter.Value)
            .Where(t => !query.CreatedBefore.HasValue || t.CreatedAt <= query.CreatedBefore.Value)
            .Where(t => !query.MinConfidence.HasValue || t.Confidence >= query.MinConfidence.Value)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        if (query.Limit.HasValue)
        {
            results = results.Take(query.Limit.Value).ToList();
        }

        return Task.FromResult<IReadOnlyList<EntityTriple>>(results);
    }

    /// <inheritdoc />
    public Task<EntityTriple?> GetCurrentValueAsync(
        string userId,
        string subject,
        string predicate,
        CancellationToken cancellationToken = default)
    {
        var result = _triples.Values
            .Where(t => t.UserId == userId)
            .Where(t => t.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Predicate.Equals(predicate, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.IsActive)
            .Where(t => t.IsCurrentlyValid)
            .Where(t => !IsSuperseded(t))
            .OrderByDescending(t => t.Version)
            .ThenByDescending(t => t.CreatedAt)
            .FirstOrDefault();

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<EntityTriple?> GetValueAsOfAsync(
        string userId,
        string subject,
        string predicate,
        DateTime asOfDate,
        CancellationToken cancellationToken = default)
    {
        var result = _triples.Values
            .Where(t => t.UserId == userId)
            .Where(t => t.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Predicate.Equals(predicate, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.WasValidAt(asOfDate))
            .Where(t => t.TransactionTime <= asOfDate) // Only facts recorded by this date
            .OrderByDescending(t => t.Version)
            .ThenByDescending(t => t.TransactionTime)
            .FirstOrDefault();

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityTriple>> GetVersionHistoryAsync(
        string userId,
        string subject,
        string predicate,
        CancellationToken cancellationToken = default)
    {
        var results = _triples.Values
            .Where(t => t.UserId == userId)
            .Where(t => t.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Predicate.Equals(predicate, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.Version)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<EntityTriple>>(results);
    }

    /// <inheritdoc />
    public async Task<EntityTriple> SupersedeAsync(
        Guid existingTripleId,
        string newObjectValue,
        DateTime? validFrom = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetByIdAsync(existingTripleId, cancellationToken)
            ?? throw new InvalidOperationException($"Triple {existingTripleId} not found");

        // Mark the old triple as superseded by setting ValidTo
        existing.ValidTo = validFrom ?? DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;

        // Create the new version
        var newTriple = existing.CreateSupersedingVersion(newObjectValue, validFrom);
        await StoreAsync(newTriple, cancellationToken);

        logger.LogInformation(
            "Superseded triple {OldId} with {NewId}: {Subject}.{Predicate} changed from '{OldValue}' to '{NewValue}'",
            existingTripleId, newTriple.Id, existing.Subject, existing.Predicate,
            existing.ObjectValue, newObjectValue);

        return newTriple;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityTriple>> FindPotentialContradictionsAsync(
        EntityTriple triple,
        CancellationToken cancellationToken = default)
    {
        // Find triples with same subject and predicate but different object value
        var potentialContradictions = _triples.Values
            .Where(t => t.UserId == triple.UserId)
            .Where(t => t.Id != triple.Id)
            .Where(t => t.Subject.Equals(triple.Subject, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.Predicate.Equals(triple.Predicate, StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.ObjectValue.Equals(triple.ObjectValue, StringComparison.OrdinalIgnoreCase))
            .Where(t => t.IsActive)
            .Where(t => t.IsCurrentlyValid)
            // Check for overlapping valid time periods
            .Where(t => PeriodsOverlap(
                triple.ValidFrom ?? DateTime.MinValue,
                triple.ValidTo ?? DateTime.MaxValue,
                t.ValidFrom ?? DateTime.MinValue,
                t.ValidTo ?? DateTime.MaxValue))
            .ToList();

        return Task.FromResult<IReadOnlyList<EntityTriple>>(potentialContradictions);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityTriple>> GetAllForUserAsync(
        string userId,
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var results = _triples.Values
            .Where(t => t.UserId == userId)
            .Where(t => includeInactive || t.IsActive)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<EntityTriple>>(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityTriple>> GetBySubjectAsync(
        string subject,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var results = _triples.Values
            .Where(t => t.Subject.Equals(subject, StringComparison.OrdinalIgnoreCase))
            .Where(t => string.IsNullOrEmpty(userId) || t.UserId == userId)
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.Confidence)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<EntityTriple>>(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityTriple>> GetByObjectAsync(
        string objectValue,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var results = _triples.Values
            .Where(t => t.ObjectValue.Equals(objectValue, StringComparison.OrdinalIgnoreCase))
            .Where(t => string.IsNullOrEmpty(userId) || t.UserId == userId)
            .Where(t => t.IsActive)
            .OrderByDescending(t => t.Confidence)
            .ThenByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<EntityTriple>>(results);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntityTriple>> GetAllActiveAsync(
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var results = _triples.Values
            .Where(t => t.IsActive)
            .Where(t => t.IsCurrentlyValid)
            .Where(t => !IsSuperseded(t))
            .Where(t => string.IsNullOrEmpty(userId) || t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<EntityTriple>>(results);
    }

    /// <summary>
    /// Checks if a triple has been superseded by a newer version.
    /// </summary>
    private bool IsSuperseded(EntityTriple triple)
    {
        return _triples.Values.Any(t =>
            t.SupersedesId == triple.Id);
    }

    /// <summary>
    /// Checks if two time periods overlap.
    /// </summary>
    private static bool PeriodsOverlap(DateTime start1, DateTime end1, DateTime start2, DateTime end2)
    {
        return start1 < end2 && start2 < end1;
    }
}
