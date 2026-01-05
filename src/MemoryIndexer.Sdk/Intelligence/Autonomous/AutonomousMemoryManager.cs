using System.Collections.Concurrent;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Autonomous;

/// <summary>
/// Autonomous Memory Manager implementation.
/// Implements MemGPT-inspired self-directed memory management.
/// </summary>
public sealed class AutonomousMemoryManager : IAutonomousMemoryManager
{
    private readonly IMemoryStore _memoryStore;
    private readonly ITieredMemoryStore _tieredStore;
    private readonly IVirtualContextManager _contextManager;
    private readonly IScoringService _scoringService;
    private readonly ILogger<AutonomousMemoryManager> _logger;

    private readonly ConcurrentDictionary<Guid, MemoryAccessRecord> _accessRecords = new();
    private readonly ConcurrentQueue<AccessEvent> _accessHistory = new();
    private readonly object _stateLock = new();
    private MemoryState _currentState = new();

    private const int MaxAccessHistorySize = 1000;

    public AutonomousMemoryManager(
        IMemoryStore memoryStore,
        ITieredMemoryStore tieredStore,
        IVirtualContextManager contextManager,
        IScoringService scoringService,
        ILogger<AutonomousMemoryManager> logger)
    {
        _memoryStore = memoryStore;
        _tieredStore = tieredStore;
        _contextManager = contextManager;
        _scoringService = scoringService;
        _logger = logger;
    }

    /// <inheritdoc />
    public MemoryState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    /// <inheritdoc />
    public async Task<MemoryOperationResponse> RequestOperationAsync(
        MemoryOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing memory operation: {Operation}", request.OperationType);

        try
        {
            return request.OperationType switch
            {
                MemoryOperationType.Retrieve => await HandleRetrieveAsync(request, cancellationToken),
                MemoryOperationType.PageIn => await HandlePageInAsync(request, cancellationToken),
                MemoryOperationType.PageOut => await HandlePageOutAsync(request, cancellationToken),
                MemoryOperationType.Archive => await HandleArchiveAsync(request, cancellationToken),
                MemoryOperationType.Delete => await HandleDeleteAsync(request, cancellationToken),
                MemoryOperationType.Optimize => await HandleOptimizeAsync(request, cancellationToken),
                _ => CreateFailedResponse($"Unsupported operation: {request.OperationType}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory operation failed: {Operation}", request.OperationType);
            return CreateFailedResponse(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<HeartbeatResponse> HeartbeatAsync(
        string currentContext,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Memory heartbeat with context length: {Length}", currentContext?.Length ?? 0);

        var alerts = new List<MemoryAlert>();
        var recommendations = new List<SuggestedOperation>();

        // Get current context state
        var contextUsage = await _contextManager.GetContextUsageAsync(cancellationToken);
        var state = _contextManager.State;

        // Calculate utilization
        var utilizationPercent = state.MaxTokenCapacity > 0
            ? state.SaturationPercentage
            : 0;

        // Update state
        lock (_stateLock)
        {
            _currentState = new MemoryState
            {
                WorkingMemoryCount = contextUsage.WorkingMemoryCount,
                SessionMemoryCount = contextUsage.SessionMemoryCount,
                ArchivalMemoryCount = contextUsage.UserMemoryCount,
                MainContextTokens = contextUsage.TotalTokens,
                MaxContextTokens = state.MaxTokenCapacity,
                LastHeartbeat = DateTime.UtcNow,
                OptimizationRecommended = utilizationPercent > 80
            };
        }

        // Check for high utilization
        if (utilizationPercent > 85)
        {
            alerts.Add(new MemoryAlert
            {
                Type = AlertType.HighUtilization,
                Severity = AlertSeverity.Warning,
                Message = $"Context utilization at {utilizationPercent:F1}%"
            });

            recommendations.Add(new SuggestedOperation
            {
                OperationType = MemoryOperationType.PageOut,
                Priority = OperationPriority.High,
                Reason = "High context utilization requires memory eviction"
            });
        }

        var healthStatus = DetermineHealthStatus(utilizationPercent, alerts.Count);
        var nextInterval = CalculateNextHeartbeatInterval(healthStatus);

        return new HeartbeatResponse
        {
            State = _currentState,
            HealthStatus = healthStatus,
            Alerts = alerts,
            RecommendedActions = recommendations,
            ImmediateActionRequired = alerts.Any(a => a.Severity >= AlertSeverity.Error),
            NextHeartbeatIn = nextInterval
        };
    }

    /// <inheritdoc />
    public async Task<PageInResponse> AutonomousPageInAsync(
        string query,
        QueryIntent? intent = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Autonomous page-in: query={Query}, intent={Intent}", query, intent);

        var pagedInMemories = new List<MemoryWithScore>();

        var searchOptions = new MemorySearchOptions
        {
            Limit = 5
        };

        var results = await _memoryStore.SearchAsync(
            ReadOnlyMemory<float>.Empty,
            searchOptions,
            cancellationToken);

        foreach (var result in results)
        {
            pagedInMemories.Add(new MemoryWithScore
            {
                Memory = result.Memory,
                RelevanceScore = result.Score,
                SourceTier = result.Memory.Tier
            });
        }

        return new PageInResponse
        {
            Success = true,
            PagedInMemories = pagedInMemories,
            TokensAdded = EstimateTokenCount(pagedInMemories.Select(p => p.Memory.Content)),
            EvictionRequired = false,
            EvictedMemories = []
        };
    }

    /// <inheritdoc />
    public async Task<PageOutResponse> AutonomousPageOutAsync(
        int tokensNeeded,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Autonomous page-out: tokens={Tokens}", tokensNeeded);

        var pagedOut = new List<MemoryReference>();
        var archived = new List<MemoryReference>();
        var tokensFreed = 0;

        // Page out from working memory to get candidates
        var candidates = await _contextManager.PageOutAsync(tokensNeeded / 100, cancellationToken);

        foreach (var memory in candidates)
        {
            var tokens = EstimateTokenCount(memory.Content);
            var reference = new MemoryReference
            {
                Id = memory.Id,
                Preview = memory.Content?.Length > 100 ? memory.Content[..100] + "..." : memory.Content ?? "",
                TokenCount = tokens,
                Tier = memory.Tier
            };

            pagedOut.Add(reference);
            tokensFreed += tokens;
        }

        return new PageOutResponse
        {
            Success = true,
            PagedOutMemories = pagedOut,
            TokensFreed = tokensFreed,
            ArchivedMemories = archived
        };
    }

    /// <inheritdoc />
    public async Task<OptimizationResult> OptimizeMemoryAsync(
        OptimizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new OptimizationOptions();
        _logger.LogDebug("Optimizing memory with target utilization: {Target}", options.TargetUtilization);

        var startTime = DateTimeOffset.UtcNow;
        var actions = new List<OptimizationAction>();

        var memories = await _memoryStore.GetAllAsync("default", new MemoryFilterOptions { Limit = 1000 }, cancellationToken);

        var tokensBefore = EstimateTokenCount(memories.Select(m => m.Content));

        var archived = 0;
        var archivedIds = new List<Guid>();

        foreach (var memory in memories.Where(m => m.Stability <= MemoryStability.Stabilizing))
        {
            var score = _scoringService.CalculateScore(memory);
            if (score < options.MinImportanceToRetain)
            {
                await _tieredStore.DemoteAsync(memory, cancellationToken);
                archived++;
                archivedIds.Add(memory.Id);
            }
        }

        if (archived > 0)
        {
            actions.Add(new OptimizationAction
            {
                Type = OptimizationActionType.Archived,
                AffectedMemoryIds = archivedIds,
                Description = $"Archived {archived} low-priority memories",
                TokensSaved = EstimateTokenCount(memories.Where(m => archivedIds.Contains(m.Id)).Select(m => m.Content))
            });
        }

        var tokensAfter = tokensBefore - actions.Sum(a => a.TokensSaved);

        return new OptimizationResult
        {
            Success = true,
            TokensBefore = tokensBefore,
            TokensAfter = Math.Max(0, tokensAfter),
            MemoriesArchived = archived,
            MemoriesCompressed = 0,
            MemoriesConsolidated = 0,
            ActionsTaken = actions,
            Duration = DateTimeOffset.UtcNow - startTime
        };
    }

    /// <inheritdoc />
    public Task RecordAccessAsync(
        Guid memoryId,
        MemoryAccessType accessType,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        _accessRecords.AddOrUpdate(
            memoryId,
            _ => new MemoryAccessRecord
            {
                MemoryId = memoryId,
                FirstAccessed = now,
                LastAccessed = now,
                AccessCount = 1
            },
            (_, record) =>
            {
                record.LastAccessed = now;
                record.AccessCount++;
                return record;
            });

        _accessHistory.Enqueue(new AccessEvent
        {
            MemoryId = memoryId,
            AccessType = accessType,
            Timestamp = now,
            Context = context
        });

        while (_accessHistory.Count > MaxAccessHistorySize)
        {
            _accessHistory.TryDequeue(out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AccessStatistics> GetAccessStatisticsAsync(
        CancellationToken cancellationToken = default)
    {
        var totalAccesses = _accessRecords.Values.Sum(r => r.AccessCount);
        var topAccessed = _accessRecords.Values
            .OrderByDescending(r => r.AccessCount)
            .Take(10)
            .Select(r => new AccessPattern
            {
                MemoryId = r.MemoryId,
                AccessCount = r.AccessCount,
                LastAccessed = r.LastAccessed.DateTime,
                AverageInterval = r.AccessCount > 1
                    ? TimeSpan.FromTicks((r.LastAccessed - r.FirstAccessed).Ticks / (r.AccessCount - 1))
                    : TimeSpan.Zero
            })
            .ToList();

        var accessesByHour = _accessHistory
            .GroupBy(e => e.Timestamp.Hour)
            .ToDictionary(g => g.Key, g => g.Count());

        return Task.FromResult(new AccessStatistics
        {
            TotalAccesses = totalAccesses,
            HitRate = 0.8f, // Placeholder
            AverageLatency = TimeSpan.FromMilliseconds(50),
            TopAccessedMemories = topAccessed,
            AccessesByHour = accessesByHour,
            CommonSequences = []
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SuggestedOperation>> GetSuggestedOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        var suggestions = new List<SuggestedOperation>();
        var state = CurrentState;

        if (state.UtilizationPercent > 80)
        {
            suggestions.Add(new SuggestedOperation
            {
                OperationType = MemoryOperationType.PageOut,
                Priority = OperationPriority.High,
                Reason = "High memory utilization",
                EstimatedBenefit = 0.8f
            });
        }

        if (state.UtilizationPercent < 50 && state.WorkingMemoryCount > 0)
        {
            suggestions.Add(new SuggestedOperation
            {
                OperationType = MemoryOperationType.Optimize,
                Priority = OperationPriority.Low,
                Reason = "Opportunity for memory consolidation",
                EstimatedBenefit = 0.3f
            });
        }

        return Task.FromResult<IReadOnlyList<SuggestedOperation>>(suggestions);
    }

    private async Task<MemoryOperationResponse> HandleRetrieveAsync(
        MemoryOperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TargetMemoryIds == null || request.TargetMemoryIds.Count == 0)
            return CreateFailedResponse("Target memory IDs required for retrieve");

        var affected = new List<MemoryReference>();
        var tokenDelta = 0;

        foreach (var memoryId in request.TargetMemoryIds)
        {
            var memory = await _memoryStore.GetByIdAsync(memoryId, cancellationToken);
            if (memory != null)
            {
                await RecordAccessAsync(memory.Id, MemoryAccessType.Read, null, cancellationToken);
                var tokens = EstimateTokenCount(memory.Content);
                affected.Add(new MemoryReference
                {
                    Id = memory.Id,
                    Preview = memory.Content?.Length > 100 ? memory.Content[..100] + "..." : memory.Content ?? "",
                    TokenCount = tokens,
                    Tier = memory.Tier
                });
                tokenDelta += tokens;
            }
        }

        return new MemoryOperationResponse
        {
            Success = affected.Count > 0,
            OperationType = MemoryOperationType.Retrieve,
            Message = $"Retrieved {affected.Count} memories",
            AffectedMemories = affected,
            TokenDelta = tokenDelta
        };
    }

    private async Task<MemoryOperationResponse> HandlePageInAsync(
        MemoryOperationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await AutonomousPageInAsync(
            request.Query ?? "",
            null,
            cancellationToken);

        return new MemoryOperationResponse
        {
            Success = response.Success,
            OperationType = MemoryOperationType.PageIn,
            Message = $"Paged in {response.PagedInMemories.Count} memories",
            AffectedMemories = response.PagedInMemories.Select(m => new MemoryReference
            {
                Id = m.Memory.Id,
                Preview = m.Memory.Content?.Length > 100 ? m.Memory.Content[..100] + "..." : m.Memory.Content ?? "",
                TokenCount = EstimateTokenCount(m.Memory.Content),
                Tier = m.Memory.Tier
            }).ToList(),
            TokenDelta = response.TokensAdded
        };
    }

    private async Task<MemoryOperationResponse> HandlePageOutAsync(
        MemoryOperationRequest request,
        CancellationToken cancellationToken)
    {
        var response = await AutonomousPageOutAsync(1000, cancellationToken);

        return new MemoryOperationResponse
        {
            Success = response.Success,
            OperationType = MemoryOperationType.PageOut,
            Message = $"Freed {response.TokensFreed} tokens",
            AffectedMemories = response.PagedOutMemories,
            TokenDelta = -response.TokensFreed
        };
    }

    private async Task<MemoryOperationResponse> HandleArchiveAsync(
        MemoryOperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TargetMemoryIds == null || request.TargetMemoryIds.Count == 0)
            return CreateFailedResponse("Target memory IDs required for archive");

        var affected = new List<MemoryReference>();

        foreach (var memoryId in request.TargetMemoryIds)
        {
            var memory = await _memoryStore.GetByIdAsync(memoryId, cancellationToken);
            if (memory != null)
            {
                await _tieredStore.DemoteAsync(memory, cancellationToken);
                affected.Add(new MemoryReference
                {
                    Id = memory.Id,
                    Preview = memory.Content?.Length > 100 ? memory.Content[..100] + "..." : memory.Content ?? "",
                    TokenCount = EstimateTokenCount(memory.Content),
                    Tier = memory.Tier
                });
            }
        }

        return new MemoryOperationResponse
        {
            Success = affected.Count > 0,
            OperationType = MemoryOperationType.Archive,
            Message = $"Archived {affected.Count} memories",
            AffectedMemories = affected
        };
    }

    private async Task<MemoryOperationResponse> HandleDeleteAsync(
        MemoryOperationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.TargetMemoryIds == null || request.TargetMemoryIds.Count == 0)
            return CreateFailedResponse("Target memory IDs required for delete");

        var deleted = 0;
        var affected = new List<MemoryReference>();

        foreach (var memoryId in request.TargetMemoryIds)
        {
            var success = await _memoryStore.DeleteAsync(memoryId, false, cancellationToken);
            if (success)
            {
                deleted++;
                affected.Add(new MemoryReference { Id = memoryId });
            }
        }

        return new MemoryOperationResponse
        {
            Success = deleted > 0,
            OperationType = MemoryOperationType.Delete,
            Message = $"Deleted {deleted} memories",
            AffectedMemories = affected
        };
    }

    private async Task<MemoryOperationResponse> HandleOptimizeAsync(
        MemoryOperationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await OptimizeMemoryAsync(null, cancellationToken);

        return new MemoryOperationResponse
        {
            Success = result.Success,
            OperationType = MemoryOperationType.Optimize,
            Message = $"Optimized: archived {result.MemoriesArchived}, saved {result.TokensBefore - result.TokensAfter} tokens",
            TokenDelta = -(result.TokensBefore - result.TokensAfter)
        };
    }

    private static MemoryOperationResponse CreateFailedResponse(string message)
    {
        return new MemoryOperationResponse
        {
            Success = false,
            Message = message
        };
    }

    private static MemoryHealthStatus DetermineHealthStatus(float utilization, int alertCount)
    {
        if (alertCount > 0 && utilization > 90) return MemoryHealthStatus.Critical;
        if (utilization > 85) return MemoryHealthStatus.NeedsOptimization;
        if (utilization > 70) return MemoryHealthStatus.Healthy;
        return MemoryHealthStatus.Healthy;
    }

    private static TimeSpan CalculateNextHeartbeatInterval(MemoryHealthStatus status) =>
        status switch
        {
            MemoryHealthStatus.Critical => TimeSpan.FromSeconds(30),
            MemoryHealthStatus.NeedsOptimization => TimeSpan.FromMinutes(1),
            MemoryHealthStatus.HighUtilization => TimeSpan.FromMinutes(2),
            _ => TimeSpan.FromMinutes(5)
        };

    private static int EstimateTokenCount(string? content) =>
        string.IsNullOrEmpty(content) ? 0 : content.Length / 4;

    private static int EstimateTokenCount(IEnumerable<string?> contents) =>
        contents.Sum(EstimateTokenCount);

    private sealed class MemoryAccessRecord
    {
        public Guid MemoryId { get; init; }
        public DateTimeOffset FirstAccessed { get; init; }
        public DateTimeOffset LastAccessed { get; set; }
        public int AccessCount { get; set; }
    }

    private sealed class AccessEvent
    {
        public Guid MemoryId { get; init; }
        public MemoryAccessType AccessType { get; init; }
        public DateTimeOffset Timestamp { get; init; }
        public string? Context { get; init; }
    }
}
