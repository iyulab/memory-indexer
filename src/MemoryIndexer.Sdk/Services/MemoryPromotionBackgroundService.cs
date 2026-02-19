using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Services;

/// <summary>
/// Background service that automatically promotes memories through the 4-tier hierarchy.
/// Implements proactive consolidation from Atkinson-Shiffrin Multi-Store Model.
/// </summary>
/// <remarks>
/// Promotion Pipeline:
/// - Tier 0→1: Buffer → Working Memory (via ISensoryPromoter)
/// - Tier 1→2: Working Memory → Session Storage (via IShortTermMemoryOrchestrator)
/// - Tier 2→3: Session Storage → Archive (via ILongTermPromoter) - Phase 52
///
/// Triggers checked every 5 seconds (configurable):
/// - Buffer: TTL (60s), Token threshold (500), Turn threshold (3)
/// - Working: Idle timeout (10min), Token threshold (2K), Turn threshold (10), Topic change
/// - Long→Archive: AND logic (confidence ≥ 0.8 AND confirmations ≥ 3)
/// </remarks>
public sealed partial class MemoryPromotionBackgroundService : BackgroundService
{
    private readonly ISensoryPromoter _sensoryPromoter;
    private readonly IShortTermMemoryOrchestrator _orchestrator;
    private readonly ILongTermPromoter _longTermPromoter;
    private readonly ILogger<MemoryPromotionBackgroundService> _logger;
    private readonly MemoryPromotionBackgroundOptions _options;

    public MemoryPromotionBackgroundService(
        ISensoryPromoter sensoryPromoter,
        IShortTermMemoryOrchestrator orchestrator,
        ILongTermPromoter longTermPromoter,
        IOptions<MemoryPromotionBackgroundOptions> options,
        ILogger<MemoryPromotionBackgroundService> logger)
    {
        _sensoryPromoter = sensoryPromoter;
        _orchestrator = orchestrator;
        _longTermPromoter = longTermPromoter;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWorkerStarted(_logger, _options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);

                // Phase 1: Check Buffer → Working Memory promotions (T0→T1)
                await CheckBufferPromotionsAsync(stoppingToken);

                // Phase 2: Check Working Memory → Session archival (T1→T2)
                await CheckWorkingMemoryArchivalAsync(stoppingToken);

                // Phase 3: Check Long → Archive promotion (T2→T3) - Phase 52
                await CheckLongTermArchivalAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                LogPromotionCycleError(_logger, ex);
                // Continue running despite errors
            }
        }

        LogWorkerStopped(_logger);
    }

    /// <summary>
    /// Checks for pending buffer promotions (T0→T1) and executes them.
    /// </summary>
    private async Task CheckBufferPromotionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var pendingPromotions = await _sensoryPromoter.CheckPendingPromotionsAsync(cancellationToken);

            if (pendingPromotions.Count == 0)
            {
                return;
            }

            LogFoundPendingBufferPromotions(_logger, pendingPromotions.Count);

            foreach (var check in pendingPromotions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                LogTriggeringBufferPromotion(_logger, check.UserId, check.Trigger, check.PendingItems, check.PendingTokens);

                var result = await _sensoryPromoter.PromoteAsync(
                    check.UserId,
                    check.Trigger,
                    cancellationToken);

                if (result.Success)
                {
                    LogBufferPromotionSucceeded(_logger, result.ItemsProcessed, result.CreatedMemories.Count, result.EvictedMemories.Count);
                }
                else
                {
                    LogBufferPromotionFailed(_logger, result.Error);
                }
            }
        }
        catch (Exception ex)
        {
            LogErrorCheckingBufferPromotions(_logger, ex);
        }
    }

    /// <summary>
    /// Checks for Working Memory archival triggers (T1→T2) and executes them.
    /// </summary>
    private async Task CheckWorkingMemoryArchivalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activeUsers = _orchestrator.GetActiveUserIds();

            if (activeUsers.Count == 0)
            {
                return;
            }

            foreach (var userId in activeUsers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var trigger = await _orchestrator.CheckArchivalTriggerAsync(userId, cancellationToken);

                if (trigger.HasValue)
                {
                    LogTriggeringWorkingMemoryArchival(_logger, userId, trigger.Value);

                    var result = await _orchestrator.ArchiveToSessionAsync(
                        userId,
                        trigger.Value,
                        summarize: true,
                        cancellationToken);

                    if (result.Success)
                    {
                        LogArchivalSucceeded(_logger, result.MemoriesArchived);
                    }
                    else
                    {
                        LogArchivalFailed(_logger, result.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogErrorCheckingWorkingMemoryArchival(_logger, ex);
        }
    }

    /// <summary>
    /// Checks for Long tier memories eligible for Archive promotion (T2→T3) and executes them.
    /// Implements Tulving's Episodic→Semantic transition with AND logic.
    /// </summary>
    /// <remarks>
    /// Phase 52: AND logic requirements (both must be satisfied):
    /// - Confidence >= 0.8
    /// - ConfirmCount >= 3
    /// </remarks>
    private async Task CheckLongTermArchivalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var usersWithCandidates = await _longTermPromoter.GetUsersWithCandidatesAsync(cancellationToken);

            if (usersWithCandidates.Count == 0)
            {
                return;
            }

            foreach (var userId in usersWithCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidates = await _longTermPromoter.CheckPromotionCandidatesAsync(userId, cancellationToken);
                var eligibleCount = candidates.Count(c => c.IsEligible);

                if (eligibleCount > 0)
                {
                    LogFoundLongTierCandidates(_logger, eligibleCount, candidates.Count, userId);

                    var result = await _longTermPromoter.PromoteToArchiveAsync(userId, cancellationToken);

                    if (result.Success)
                    {
                        LogArchivePromotionSucceeded(_logger, result.MemoriesPromoted, result.MemoriesSkipped);
                    }
                    else
                    {
                        LogArchivePromotionFailed(_logger, result.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogErrorCheckingLongTermArchival(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Memory promotion worker started. Check interval: {Interval}s")]
    private static partial void LogWorkerStarted(ILogger logger, int interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "[BACKGROUND] Error in promotion cycle")]
    private static partial void LogPromotionCycleError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Memory promotion worker stopped")]
    private static partial void LogWorkerStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Found {Count} users with pending buffer promotions")]
    private static partial void LogFoundPendingBufferPromotions(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Triggering buffer promotion for user {UserId}: Trigger={Trigger}, Items={Items}, Tokens={Tokens}")]
    private static partial void LogTriggeringBufferPromotion(ILogger logger, string userId, PromotionTriggerType trigger, int items, int tokens);

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Buffer promotion succeeded: {Items} items -> {Memories} memories, Evicted: {Evicted}")]
    private static partial void LogBufferPromotionSucceeded(ILogger logger, int items, int memories, int evicted);

    [LoggerMessage(Level = LogLevel.Error, Message = "[BACKGROUND] Buffer promotion failed: {Error}")]
    private static partial void LogBufferPromotionFailed(ILogger logger, string? error);

    [LoggerMessage(Level = LogLevel.Error, Message = "[BACKGROUND] Error checking buffer promotions")]
    private static partial void LogErrorCheckingBufferPromotions(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Triggering working memory archival for user {UserId}: Trigger={Trigger}")]
    private static partial void LogTriggeringWorkingMemoryArchival(ILogger logger, string userId, WorkingPromotionTrigger trigger);

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Archival succeeded: {Count} memories archived")]
    private static partial void LogArchivalSucceeded(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "[BACKGROUND] Archival failed: {Error}")]
    private static partial void LogArchivalFailed(ILogger logger, string? error);

    [LoggerMessage(Level = LogLevel.Error, Message = "[BACKGROUND] Error checking working memory archival")]
    private static partial void LogErrorCheckingWorkingMemoryArchival(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Found {Eligible}/{Total} Long tier memories eligible for Archive (user {UserId})")]
    private static partial void LogFoundLongTierCandidates(ILogger logger, int eligible, int total, string userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "[BACKGROUND] Archive promotion succeeded: {Promoted} promoted, {Skipped} skipped (AND logic)")]
    private static partial void LogArchivePromotionSucceeded(ILogger logger, int promoted, int skipped);

    [LoggerMessage(Level = LogLevel.Error, Message = "[BACKGROUND] Archive promotion failed: {Error}")]
    private static partial void LogArchivePromotionFailed(ILogger logger, string? error);

    [LoggerMessage(Level = LogLevel.Error, Message = "[BACKGROUND] Error checking Long->Archive promotion")]
    private static partial void LogErrorCheckingLongTermArchival(ILogger logger, Exception ex);
}

/// <summary>
/// Configuration options for the memory promotion background service.
/// </summary>
public sealed class MemoryPromotionBackgroundOptions
{
    /// <summary>
    /// Interval in seconds between promotion checks.
    /// Default: 5 seconds (responsive to game speed of ~5s/conversation).
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Whether the background service is enabled.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
