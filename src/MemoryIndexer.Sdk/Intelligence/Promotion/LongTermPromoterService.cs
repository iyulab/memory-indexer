using System.Diagnostics;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Promotion;

/// <summary>
/// Service for promoting Long tier (episodic) memories to Archive tier (semantic).
/// Implements Tulving's Episodic→Semantic memory transition with AND logic.
/// </summary>
/// <remarks>
/// Phase 52: Long → Archive Promotion Pipeline
///
/// AND logic requirements (both must be satisfied):
/// - Confidence >= 0.8
/// - ConfirmCount >= 3
///
/// This represents the cognitive process where repeated, consistent
/// episodic experiences become abstracted into semantic knowledge.
/// </remarks>
public sealed partial class LongTermPromoterService : ILongTermPromoter
{
    private readonly IMemoryStore _memoryStore;
    private readonly ITierManager _tierManager;
    private readonly SemanticStoreOptions _archiveOptions;
    private readonly ILogger<LongTermPromoterService> _logger;

    public LongTermPromoterService(
        IMemoryStore memoryStore,
        ITierManager tierManager,
        IOptions<SemanticStoreOptions> archiveOptions,
        ILogger<LongTermPromoterService> logger)
    {
        _memoryStore = memoryStore;
        _tierManager = tierManager;
        _archiveOptions = archiveOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ArchivePromotionCandidate>> CheckPromotionCandidatesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var longTierMemories = await _memoryStore.GetAllAsync(
            userId,
            new MemoryFilterOptions
            {
                Tiers = [Tier.Long],
                OrderBy = MemoryOrderBy.CreatedAtAsc
            },
            cancellationToken);

        if (longTierMemories.Count == 0)
        {
            return [];
        }

        var candidates = new List<ArchivePromotionCandidate>();
        var minConfidence = _archiveOptions.MinConfidenceThreshold;
        var minConfirmCount = _archiveOptions.MinConfirmationCount;

        foreach (var memory in longTierMemories)
        {
            var meetsConfidence = memory.Confidence >= minConfidence;
            var meetsConfirmCount = memory.ConfirmCount >= minConfirmCount;
            var isEligible = meetsConfidence && meetsConfirmCount;

            var explanation = (meetsConfidence, meetsConfirmCount) switch
            {
                (true, true) => "Meets AND logic: confidence AND confirmations satisfied",
                (true, false) => $"Needs {minConfirmCount - memory.ConfirmCount} more confirmations",
                (false, true) => $"Needs {minConfidence - memory.Confidence:F2} more confidence",
                (false, false) => $"Needs both: confidence {memory.Confidence:F2}/{minConfidence:F2}, confirms {memory.ConfirmCount}/{minConfirmCount}"
            };

            candidates.Add(new ArchivePromotionCandidate
            {
                Memory = memory,
                IsEligible = isEligible,
                Confidence = memory.Confidence,
                ConfirmCount = memory.ConfirmCount,
                RequiredConfidence = minConfidence,
                RequiredConfirmCount = minConfirmCount,
                Explanation = explanation
            });
        }

        var eligibleValue = candidates.Count(c => c.IsEligible);
        LogARCHIVEPROMOTIONUserUserIdTotal(_logger, userId, candidates.Count, eligibleValue);

        return candidates;
    }

    /// <inheritdoc />
    public async Task<ArchivePromotionResult> PromoteToArchiveAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var sw = Stopwatch.StartNew();

        try
        {
            var candidates = await CheckPromotionCandidatesAsync(userId, cancellationToken);

            if (candidates.Count == 0)
            {
                return ArchivePromotionResult.Empty with { Duration = sw.Elapsed };
            }

            var eligibleCandidates = candidates.Where(c => c.IsEligible).ToList();

            if (eligibleCandidates.Count == 0)
            {
                LogARCHIVEPROMOTIONUserUserIdMemories(_logger, userId);

                return new ArchivePromotionResult
                {
                    Success = true,
                    MemoriesPromoted = 0,
                    MemoriesSkipped = candidates.Count,
                    Duration = sw.Elapsed
                };
            }

            var promotedMemories = new List<PromotedMemoryInfo>();
            var promotedCount = 0;

            foreach (var candidate in eligibleCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await PromoteMemoryAsync(candidate.Memory, cancellationToken);

                if (result.Success)
                {
                    promotedCount++;
                    promotedMemories.AddRange(result.PromotedMemories);
                }
            }

            LogARCHIVEPROMOTIONUserUserIdPromoted(_logger, userId, promotedCount, _archiveOptions.MinConfidenceThreshold, _archiveOptions.MinConfirmationCount);

            return new ArchivePromotionResult
            {
                Success = true,
                MemoriesPromoted = promotedCount,
                MemoriesSkipped = candidates.Count - eligibleCandidates.Count,
                PromotedMemories = promotedMemories,
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            LogARCHIVEPROMOTIONErrorPromotingMemories(_logger, ex, userId);
            return ArchivePromotionResult.Failure(ex.Message) with { Duration = sw.Elapsed };
        }
    }

    /// <inheritdoc />
    public async Task<ArchivePromotionResult> PromoteMemoryAsync(
        MemoryUnit memory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var sw = Stopwatch.StartNew();

        // Verify AND logic requirements
        var minConfidence = _archiveOptions.MinConfidenceThreshold;
        var minConfirmCount = _archiveOptions.MinConfirmationCount;

        if (memory.Confidence < minConfidence || memory.ConfirmCount < minConfirmCount)
        {
            return new ArchivePromotionResult
            {
                Success = false,
                MemoriesSkipped = 1,
                Error = $"Memory does not meet AND logic: confidence {memory.Confidence:F2}/{minConfidence:F2}, confirms {memory.ConfirmCount}/{minConfirmCount}",
                Duration = sw.Elapsed
            };
        }

        // Perform tier promotion via TierManager
        var promotionResult = await _tierManager.PromoteAsync(
            memory,
            Tier.Archive,
            PromotionReason.ThresholdMet,
            cancellationToken);

        if (!promotionResult.Success)
        {
            return new ArchivePromotionResult
            {
                Success = false,
                Error = promotionResult.Error,
                Duration = sw.Elapsed
            };
        }

        // Update in store
        await _memoryStore.UpdateAsync(memory, cancellationToken);

        var promotedInfo = new PromotedMemoryInfo
        {
            MemoryId = memory.Id,
            ContentSummary = memory.Content.Length > 100
                ? memory.Content[..100] + "..."
                : memory.Content,
            Confidence = memory.Confidence,
            ConfirmCount = memory.ConfirmCount,
            FinalType = memory.Type
        };

        LogARCHIVEPROMOTIONMemoryMemoryIdPromoted(_logger, memory.Id, memory.Confidence, memory.ConfirmCount);

        return new ArchivePromotionResult
        {
            Success = true,
            MemoriesPromoted = 1,
            PromotedMemories = [promotedInfo],
            Duration = sw.Elapsed
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUsersWithCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Get all users with Long tier memories
        // This is a simplified implementation - in production, you might want
        // to track active users more efficiently
        var allMemories = await _memoryStore.GetAllAsync(
            userId: null!, // Will need to iterate over known users
            new MemoryFilterOptions { Tiers = [Tier.Long] },
            cancellationToken);

        var userIds = allMemories
            .Select(m => m.UserId)
            .Distinct()
            .ToList();

        return userIds;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "[ARCHIVE_PROMOTION] User {UserId}: {Total} Long tier memories, {Eligible} eligible for Archive")]
    private static partial void LogARCHIVEPROMOTIONUserUserIdTotal(ILogger logger, string userId, int total, int eligible);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[ARCHIVE_PROMOTION] User {UserId}: No memories meet AND logic requirements")]
    private static partial void LogARCHIVEPROMOTIONUserUserIdMemories(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[ARCHIVE_PROMOTION] ✅ User {UserId}: Promoted {Count} memories to Archive tier (AND logic: confidence≥{Confidence}, confirms≥{Confirms})")]
    private static partial void LogARCHIVEPROMOTIONUserUserIdPromoted(ILogger logger, string userId, int count, float confidence, double confirms);

    [LoggerMessage(Level = LogLevel.Error, Message = "[ARCHIVE_PROMOTION] Error promoting memories for user {UserId}")]
    private static partial void LogARCHIVEPROMOTIONErrorPromotingMemories(ILogger logger, Exception ex, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[ARCHIVE_PROMOTION] Memory {MemoryId} promoted: Long→Archive (confidence={Confidence:F2}, confirms={Confirms})")]
    private static partial void LogARCHIVEPROMOTIONMemoryMemoryIdPromoted(ILogger logger, Guid memoryId, float confidence, double confirms);
}
