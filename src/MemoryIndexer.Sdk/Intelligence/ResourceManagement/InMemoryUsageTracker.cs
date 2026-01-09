using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.ResourceManagement;

/// <summary>
/// In-memory implementation of usage tracking.
/// Maintains real-time counters for memory operations.
/// </summary>
/// <remarks>
/// Phase v0.6.0-γ: Resource Management
/// Thread-safe with concurrent dictionaries and interlocked operations.
/// </remarks>
public sealed class InMemoryUsageTracker : IUsageTracker
{
    private readonly IMemoryStore _memoryStore;
    private readonly ILogger<InMemoryUsageTracker> _logger;
    private readonly ConcurrentDictionary<string, UserUsageData> _userUsage = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _tenantUsers = new();

    public InMemoryUsageTracker(
        IMemoryStore memoryStore,
        ILogger<InMemoryUsageTracker> logger)
    {
        _memoryStore = memoryStore;
        _logger = logger;
    }

    public void RecordStore(string userId, long sizeBytes, Tier tier, MemoryType type, string? tenantId = null)
    {
        var userData = _userUsage.GetOrAdd(userId, _ => new UserUsageData(userId, tenantId));

        Interlocked.Increment(ref userData.MemoryCount);
        Interlocked.Add(ref userData.StorageSizeBytes, sizeBytes);
        userData.IncrementTier(tier);
        userData.IncrementType(type);
        userData.LastUpdated = DateTime.UtcNow;

        if (tenantId != null)
        {
            var users = _tenantUsers.GetOrAdd(tenantId, _ => []);
            lock (users)
            {
                users.Add(userId);
            }
        }

        _logger.LogTrace(
            "Recorded store for user {UserId}: +1 memory, +{Bytes} bytes, tier={Tier}, type={Type}",
            userId, sizeBytes, tier, type);
    }

    public void RecordDelete(string userId, long sizeBytes, Tier tier, MemoryType type, string? tenantId = null)
    {
        if (!_userUsage.TryGetValue(userId, out var userData))
        {
            _logger.LogWarning("RecordDelete called for unknown user {UserId}", userId);
            return;
        }

        Interlocked.Decrement(ref userData.MemoryCount);
        Interlocked.Add(ref userData.StorageSizeBytes, -sizeBytes);
        userData.DecrementTier(tier);
        userData.DecrementType(type);
        userData.LastUpdated = DateTime.UtcNow;

        // Ensure we don't go negative
        if (Interlocked.Read(ref userData.MemoryCount) < 0)
            Interlocked.Exchange(ref userData.MemoryCount, 0);
        if (Interlocked.Read(ref userData.StorageSizeBytes) < 0)
            Interlocked.Exchange(ref userData.StorageSizeBytes, 0);

        _logger.LogTrace(
            "Recorded delete for user {UserId}: -1 memory, -{Bytes} bytes, tier={Tier}, type={Type}",
            userId, sizeBytes, tier, type);
    }

    public void RecordTierPromotion(string userId, Tier fromTier, Tier toTier, string? tenantId = null)
    {
        if (!_userUsage.TryGetValue(userId, out var userData))
        {
            _logger.LogWarning("RecordTierPromotion called for unknown user {UserId}", userId);
            return;
        }

        userData.DecrementTier(fromTier);
        userData.IncrementTier(toTier);
        userData.LastUpdated = DateTime.UtcNow;

        _logger.LogTrace(
            "Recorded tier promotion for user {UserId}: {FromTier} -> {ToTier}",
            userId, fromTier, toTier);
    }

    public ResourceUsage GetUsage(string userId, string? tenantId = null)
    {
        if (!_userUsage.TryGetValue(userId, out var userData))
        {
            return new ResourceUsage
            {
                UserId = userId,
                TenantId = tenantId,
                MemoryCount = 0,
                StorageSizeBytes = 0,
                ByTier = new Dictionary<Tier, long>(),
                ByType = new Dictionary<MemoryType, long>()
            };
        }

        return new ResourceUsage
        {
            UserId = userId,
            TenantId = userData.TenantId ?? tenantId,
            MemoryCount = Interlocked.Read(ref userData.MemoryCount),
            StorageSizeBytes = Interlocked.Read(ref userData.StorageSizeBytes),
            ByTier = userData.GetTierSnapshot(),
            ByType = userData.GetTypeSnapshot(),
            CalculatedAt = DateTime.UtcNow
        };
    }

    public TenantUsage GetTenantUsage(string tenantId)
    {
        if (!_tenantUsers.TryGetValue(tenantId, out var users))
        {
            return new TenantUsage
            {
                TenantId = tenantId,
                ActiveUsers = 0,
                TotalMemories = 0,
                TotalStorageBytes = 0
            };
        }

        var userBreakdown = new Dictionary<string, ResourceUsage>();
        var byTier = new Dictionary<Tier, long>();
        var byType = new Dictionary<MemoryType, long>();
        long totalMemories = 0;
        long totalStorage = 0;

        lock (users)
        {
            foreach (var userId in users)
            {
                var usage = GetUsage(userId, tenantId);
                userBreakdown[userId] = usage;
                totalMemories += usage.MemoryCount;
                totalStorage += usage.StorageSizeBytes;

                if (usage.ByTier != null)
                {
                    foreach (var (tier, count) in usage.ByTier)
                    {
                        byTier.TryGetValue(tier, out var existing);
                        byTier[tier] = existing + count;
                    }
                }

                if (usage.ByType != null)
                {
                    foreach (var (type, count) in usage.ByType)
                    {
                        byType.TryGetValue(type, out var existing);
                        byType[type] = existing + count;
                    }
                }
            }

            return new TenantUsage
            {
                TenantId = tenantId,
                ActiveUsers = users.Count,
                TotalMemories = totalMemories,
                TotalStorageBytes = totalStorage,
                UserBreakdown = userBreakdown,
                ByTier = byTier,
                ByType = byType
            };
        }
    }

    public IReadOnlyList<string> GetTrackedUsers() => [.. _userUsage.Keys];

    public GlobalUsageSummary GetGlobalSummary()
    {
        var byTier = new Dictionary<Tier, long>();
        var byType = new Dictionary<MemoryType, long>();
        long totalMemories = 0;
        long totalStorage = 0;
        var userStats = new List<(string UserId, long Count, long Bytes)>();

        foreach (var (userId, userData) in _userUsage)
        {
            var count = Interlocked.Read(ref userData.MemoryCount);
            var bytes = Interlocked.Read(ref userData.StorageSizeBytes);
            totalMemories += count;
            totalStorage += bytes;
            userStats.Add((userId, count, bytes));

            var tierSnapshot = userData.GetTierSnapshot();
            foreach (var (tier, tierCount) in tierSnapshot)
            {
                byTier.TryGetValue(tier, out var existing);
                byTier[tier] = existing + tierCount;
            }

            var typeSnapshot = userData.GetTypeSnapshot();
            foreach (var (type, typeCount) in typeSnapshot)
            {
                byType.TryGetValue(type, out var existing);
                byType[type] = existing + typeCount;
            }
        }

        return new GlobalUsageSummary
        {
            TotalUsers = _userUsage.Count,
            TotalTenants = _tenantUsers.Count,
            TotalMemories = totalMemories,
            TotalStorageBytes = totalStorage,
            ByTier = byTier,
            ByType = byType,
            TopUsersByCount = userStats
                .OrderByDescending(x => x.Count)
                .Take(10)
                .Select(x => (x.UserId, x.Count))
                .ToList(),
            TopUsersByStorage = userStats
                .OrderByDescending(x => x.Bytes)
                .Take(10)
                .Select(x => (x.UserId, x.Bytes))
                .ToList()
        };
    }

    public async Task RefreshFromStoreAsync(string userId, CancellationToken cancellationToken = default)
    {
        var memories = await _memoryStore.GetAllAsync(userId, cancellationToken: cancellationToken);

        var userData = _userUsage.GetOrAdd(userId, _ => new UserUsageData(userId, null));

        // Reset counters
        Interlocked.Exchange(ref userData.MemoryCount, memories.Count);

        long totalSize = 0;
        userData.ResetTiers();
        userData.ResetTypes();

        foreach (var memory in memories)
        {
            totalSize += EstimateSize(memory);
            userData.IncrementTier(memory.Tier);
            userData.IncrementType(memory.Type);
        }

        Interlocked.Exchange(ref userData.StorageSizeBytes, totalSize);
        userData.LastUpdated = DateTime.UtcNow;

        _logger.LogInformation(
            "Refreshed usage for user {UserId}: {Count} memories, {Bytes} bytes",
            userId, memories.Count, totalSize);
    }

    public void ClearUser(string userId)
    {
        _userUsage.TryRemove(userId, out _);

        // Remove from tenant tracking
        foreach (var (_, users) in _tenantUsers)
        {
            lock (users)
            {
                users.Remove(userId);
            }
        }

        _logger.LogInformation("Cleared usage tracking for user {UserId}", userId);
    }

    private static long EstimateSize(MemoryUnit memory)
    {
        long size = 0;
        size += memory.Content?.Length * 2 ?? 0; // UTF-16
        size += memory.Embedding?.Length * 4 ?? 0; // float32
        size += 200; // Overhead for metadata, IDs, etc.
        return size;
    }

    /// <summary>
    /// Internal thread-safe user usage data.
    /// </summary>
    private sealed class UserUsageData
    {
        public readonly string UserId;
        public readonly string? TenantId;
        public long MemoryCount;
        public long StorageSizeBytes;
        public DateTime LastUpdated = DateTime.UtcNow;

        private readonly ConcurrentDictionary<Tier, long> _byTier = new();
        private readonly ConcurrentDictionary<MemoryType, long> _byType = new();

        public UserUsageData(string userId, string? tenantId)
        {
            UserId = userId;
            TenantId = tenantId;
        }

        public void IncrementTier(Tier tier) =>
            _byTier.AddOrUpdate(tier, 1, (_, count) => count + 1);

        public void DecrementTier(Tier tier) =>
            _byTier.AddOrUpdate(tier, 0, (_, count) => Math.Max(0, count - 1));

        public void IncrementType(MemoryType type) =>
            _byType.AddOrUpdate(type, 1, (_, count) => count + 1);

        public void DecrementType(MemoryType type) =>
            _byType.AddOrUpdate(type, 0, (_, count) => Math.Max(0, count - 1));

        public Dictionary<Tier, long> GetTierSnapshot() => new(_byTier);
        public Dictionary<MemoryType, long> GetTypeSnapshot() => new(_byType);

        public void ResetTiers() => _byTier.Clear();
        public void ResetTypes() => _byType.Clear();
    }
}
