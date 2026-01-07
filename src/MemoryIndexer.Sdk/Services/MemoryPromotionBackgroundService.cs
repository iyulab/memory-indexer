using MemoryIndexer.Interfaces;
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
///
/// Triggers checked every 5 seconds (configurable):
/// - Buffer: TTL (60s), Token threshold (500), Turn threshold (3)
/// - Working: Idle timeout (10min), Token threshold (2K), Turn threshold (10), Topic change
/// </remarks>
public sealed class MemoryPromotionBackgroundService : BackgroundService
{
    private readonly ISensoryPromoter _sensoryPromoter;
    private readonly IShortTermMemoryOrchestrator _orchestrator;
    private readonly ILogger<MemoryPromotionBackgroundService> _logger;
    private readonly MemoryPromotionBackgroundOptions _options;

    public MemoryPromotionBackgroundService(
        ISensoryPromoter sensoryPromoter,
        IShortTermMemoryOrchestrator orchestrator,
        IOptions<MemoryPromotionBackgroundOptions> options,
        ILogger<MemoryPromotionBackgroundService> logger)
    {
        _sensoryPromoter = sensoryPromoter;
        _orchestrator = orchestrator;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "[BACKGROUND] Memory promotion worker started. Check interval: {Interval}s",
            _options.CheckIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.CheckIntervalSeconds), stoppingToken);

                // Phase 1: Check Buffer → Working Memory promotions (T0→T1)
                await CheckBufferPromotionsAsync(stoppingToken);

                // Phase 2: Check Working Memory → Session archival (T1→T2)
                await CheckWorkingMemoryArchivalAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[BACKGROUND] Error in promotion cycle");
                // Continue running despite errors
            }
        }

        _logger.LogInformation("[BACKGROUND] Memory promotion worker stopped");
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

            _logger.LogInformation(
                "[BACKGROUND] Found {Count} users with pending buffer promotions",
                pendingPromotions.Count);

            foreach (var check in pendingPromotions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation(
                    "[BACKGROUND] Triggering buffer promotion for user {UserId}: " +
                    "Trigger={Trigger}, Items={Items}, Tokens={Tokens}",
                    check.UserId, check.Trigger, check.PendingItems, check.PendingTokens);

                var result = await _sensoryPromoter.PromoteAsync(
                    check.UserId,
                    check.Trigger,
                    cancellationToken);

                if (result.Success)
                {
                    _logger.LogInformation(
                        "[BACKGROUND] ✅ Buffer promotion succeeded: {Items} items → {Memories} memories, " +
                        "Evicted: {Evicted}",
                        result.ItemsProcessed, result.CreatedMemories.Count, result.EvictedMemories.Count);
                }
                else
                {
                    _logger.LogError(
                        "[BACKGROUND] ❌ Buffer promotion failed: {Error}",
                        result.Error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BACKGROUND] Error checking buffer promotions");
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
                    _logger.LogInformation(
                        "[BACKGROUND] Triggering working memory archival for user {UserId}: Trigger={Trigger}",
                        userId, trigger.Value);

                    var result = await _orchestrator.ArchiveToSessionAsync(
                        userId,
                        trigger.Value,
                        summarize: true,
                        cancellationToken);

                    if (result.Success)
                    {
                        _logger.LogInformation(
                            "[BACKGROUND] ✅ Archival succeeded: {Count} memories archived",
                            result.MemoriesArchived);
                    }
                    else
                    {
                        _logger.LogError(
                            "[BACKGROUND] ❌ Archival failed: {Error}",
                            result.Error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BACKGROUND] Error checking working memory archival");
        }
    }
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
