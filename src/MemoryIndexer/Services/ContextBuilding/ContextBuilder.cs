using System.Text;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services.ContextStrategies;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Services.ContextBuilding;

/// <summary>
/// Default implementation of IContextBuilder that assembles context
/// with token-budget awareness across memory tiers.
/// </summary>
public class ContextBuilder : IContextBuilder
{
    private readonly IBuffer _buffer;
    private readonly IShortTermMemory _shortTermMemory;
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly ITokenCounter _tokenCounter;
    private readonly ILogger<ContextBuilder> _logger;
    private readonly Dictionary<string, IContextStrategy> _strategies;

    // Configurable defaults (can be overridden via constructor or options in future)
    private const float DefaultMinScore = 0.3f;
    private const int DefaultSearchLimit = 20;

    /// <summary>
    /// Creates a new ContextBuilder.
    /// </summary>
    public ContextBuilder(
        IBuffer buffer,
        IShortTermMemory shortTermMemory,
        IMemoryStore memoryStore,
        IEmbeddingService embeddingService,
        ITokenCounter tokenCounter,
        ILogger<ContextBuilder> logger)
    {
        _buffer = buffer;
        _shortTermMemory = shortTermMemory;
        _memoryStore = memoryStore;
        _embeddingService = embeddingService;
        _tokenCounter = tokenCounter;
        _logger = logger;

        // Register built-in strategies
        _strategies = new Dictionary<string, IContextStrategy>(StringComparer.OrdinalIgnoreCase)
        {
            ["Balanced"] = new BalancedStrategy(),
            ["RecentHeavy"] = new RecentHeavyStrategy(),
            ["SemanticHeavy"] = new SemanticHeavyStrategy()
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContextItem>> GetRecentTurnsAsync(
        string userId,
        string sessionId,
        int maxTokens,
        CancellationToken ct = default)
    {
        var items = new List<ContextItem>();
        var usedTokens = 0;

        // Get from Buffer (T0) - most recent first, then reverse for chronological order
        try
        {
            var bufferItems = await _buffer.GetPendingAsync(userId);
            var sessionBufferItems = bufferItems
                .Where(b => b.SessionId == sessionId)
                .OrderByDescending(b => b.Timestamp)
                .ToList();

            foreach (var item in sessionBufferItems)
            {
                var tokens = _tokenCounter.Count(item.Content);
                if (usedTokens + tokens > maxTokens)
                    break;

                items.Add(new ContextItem(
                    Content: item.Content,
                    Tokens: tokens,
                    Source: ContextItemSource.Recent,
                    Score: null,
                    Timestamp: item.Timestamp,
                    Role: item.Role
                ));
                usedTokens += tokens;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get buffer items for context");
        }

        // Get from Short-Term Memory (T1)
        try
        {
            var shortTermItems = await _shortTermMemory.GetAllAsync();
            foreach (var item in shortTermItems.OrderByDescending(m => m.CreatedAt))
            {
                if (usedTokens >= maxTokens)
                    break;

                var tokens = _tokenCounter.Count(item.Content);
                if (usedTokens + tokens > maxTokens)
                    continue; // Skip items that would exceed budget

                items.Add(new ContextItem(
                    Content: item.Content,
                    Tokens: tokens,
                    Source: ContextItemSource.Recent,
                    Score: null,
                    Timestamp: item.CreatedAt,
                    Role: item.Role,
                    MemoryType: item.Type
                ));
                usedTokens += tokens;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get short-term items for context");
        }

        // Reverse to get chronological order (oldest first)
        items.Reverse();

        _logger.LogDebug("GetRecentTurns: {Count} items, {Tokens} tokens", items.Count, usedTokens);
        return items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContextItem>> GetSemanticContextAsync(
        string userId,
        string query,
        int maxTokens,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        var items = new List<ContextItem>();
        var usedTokens = 0;

        try
        {
            // Generate embedding for the query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query, ct);

            // Search for USER-SCOPED memories only (Semantic/Fact types)
            // These are facts that should persist across sessions (e.g., user's name, preferences)
            // Session-specific memories (Episodic) are handled by GetSessionContextAsync
            var searchOptions = new MemorySearchOptions
            {
                UserId = userId,
                SessionId = null, // User-scoped: no session filter (sessionId param ignored by design)
                Types = [MemoryType.Semantic, MemoryType.Fact], // Exclude Episodic (session-specific)
                Limit = DefaultSearchLimit, // Get more than needed, filter by tokens
                MinScore = DefaultMinScore
            };

            var results = await _memoryStore.SearchAsync(queryEmbedding, searchOptions, ct);

            foreach (var result in results.OrderByDescending(r => r.Score))
            {
                if (usedTokens >= maxTokens)
                    break;

                var tokens = _tokenCounter.Count(result.Memory.Content);
                if (usedTokens + tokens > maxTokens)
                    continue;

                items.Add(new ContextItem(
                    Content: result.Memory.Content,
                    Tokens: tokens,
                    Source: ContextItemSource.Semantic,
                    Score: result.Score,
                    Timestamp: result.Memory.CreatedAt,
                    Role: result.Memory.Role,
                    MemoryType: result.Memory.Type
                ));
                usedTokens += tokens;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get semantic context");
        }

        _logger.LogDebug("GetSemanticContext: {Count} items, {Tokens} tokens", items.Count, usedTokens);
        return items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContextItem>> GetSessionContextAsync(
        string userId,
        string sessionId,
        int maxTokens,
        CancellationToken ct = default)
    {
        var items = new List<ContextItem>();
        var usedTokens = 0;

        try
        {
            var options = new MemoryFilterOptions
            {
                SessionId = sessionId,
                Types = [MemoryType.Episodic]
            };

            var memories = await _memoryStore.GetAllAsync(userId, options, ct);

            // Order by time, newest first (for most relevant context)
            foreach (var memory in memories.OrderByDescending(m => m.CreatedAt))
            {
                if (usedTokens >= maxTokens)
                    break;

                var tokens = _tokenCounter.Count(memory.Content);
                if (usedTokens + tokens > maxTokens)
                    continue;

                items.Add(new ContextItem(
                    Content: memory.Content,
                    Tokens: tokens,
                    Source: ContextItemSource.Episodic,
                    Score: null,
                    Timestamp: memory.CreatedAt,
                    Role: memory.Role,
                    MemoryType: memory.Type
                ));
                usedTokens += tokens;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get session context");
        }

        _logger.LogDebug("GetSessionContext: {Count} items, {Tokens} tokens", items.Count, usedTokens);
        return items;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContextItem>> GetUserFactsAsync(
        string userId,
        int maxTokens,
        CancellationToken ct = default)
    {
        var items = new List<ContextItem>();
        var usedTokens = 0;

        try
        {
            var options = new MemoryFilterOptions
            {
                SessionId = null, // User-scoped (no session)
                Types = [MemoryType.Semantic, MemoryType.Fact]
            };

            var memories = await _memoryStore.GetAllAsync(userId, options, ct);

            // Order by importance, then by recency
            foreach (var memory in memories
                .OrderByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.CreatedAt))
            {
                if (usedTokens >= maxTokens)
                    break;

                var tokens = _tokenCounter.Count(memory.Content);
                if (usedTokens + tokens > maxTokens)
                    continue;

                items.Add(new ContextItem(
                    Content: memory.Content,
                    Tokens: tokens,
                    Source: ContextItemSource.Fact,
                    Score: memory.ImportanceScore,
                    Timestamp: memory.CreatedAt,
                    Role: memory.Role,
                    MemoryType: memory.Type
                ));
                usedTokens += tokens;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get user facts");
        }

        _logger.LogDebug("GetUserFacts: {Count} items, {Tokens} tokens", items.Count, usedTokens);
        return items;
    }

    /// <inheritdoc />
    public Task<ContextBundle> BuildAsync(
        ContextRequest request,
        IContextStrategy? strategy = null,
        CancellationToken ct = default)
    {
        strategy ??= _strategies["Balanced"];
        return BuildContextAsync(request, strategy, ct);
    }

    /// <inheritdoc />
    public Task<ContextBundle> BuildAsync(
        ContextRequest request,
        string strategyName,
        CancellationToken ct = default)
    {
        if (!_strategies.TryGetValue(strategyName, out var strategy))
        {
            _logger.LogWarning("Strategy '{Name}' not found, using Balanced", strategyName);
            strategy = _strategies["Balanced"];
        }

        return BuildContextAsync(request, strategy, ct);
    }

    /// <summary>
    /// Register a custom strategy for later use.
    /// </summary>
    public void RegisterStrategy(IContextStrategy strategy)
    {
        _strategies[strategy.Name] = strategy;
        _logger.LogInformation("Registered context strategy: {Name}", strategy.Name);
    }

    private async Task<ContextBundle> BuildContextAsync(
        ContextRequest request,
        IContextStrategy strategy,
        CancellationToken ct)
    {
        // Determine budget allocation
        var allocation = request.Budget.RecentTokens.HasValue
            ? new ContextBreakdown(
                request.Budget.RecentTokens ?? 0,
                request.Budget.SemanticTokens ?? 0,
                request.Budget.EpisodicTokens ?? 0,
                request.Budget.FactTokens ?? 0)
            : strategy.AllocateBudget(request.Budget.TotalTokens);

        _logger.LogDebug(
            "Building context with strategy {Strategy}: Recent={Recent}, Semantic={Semantic}, Episodic={Episodic}, Fact={Fact}",
            strategy.Name, allocation.RecentTokens, allocation.SemanticTokens,
            allocation.EpisodicTokens, allocation.FactTokens);

        var allItems = new List<ContextItem>();
        var actualBreakdown = new int[4]; // Recent, Semantic, Episodic, Fact

        // Gather context from each source
        if (allocation.RecentTokens > 0 && request.SessionId != null)
        {
            var recentItems = await GetRecentTurnsAsync(
                request.UserId, request.SessionId, allocation.RecentTokens, ct);
            allItems.AddRange(recentItems);
            actualBreakdown[0] = recentItems.Sum(i => i.Tokens);
        }

        if (allocation.SemanticTokens > 0)
        {
            var semanticItems = await GetSemanticContextAsync(
                request.UserId, request.Query, allocation.SemanticTokens, request.SessionId, ct);
            allItems.AddRange(semanticItems);
            actualBreakdown[1] = semanticItems.Sum(i => i.Tokens);
        }

        if (allocation.EpisodicTokens > 0 && request.SessionId != null)
        {
            var episodicItems = await GetSessionContextAsync(
                request.UserId, request.SessionId, allocation.EpisodicTokens, ct);
            allItems.AddRange(episodicItems);
            actualBreakdown[2] = episodicItems.Sum(i => i.Tokens);
        }

        if (allocation.FactTokens > 0)
        {
            var factItems = await GetUserFactsAsync(request.UserId, allocation.FactTokens, ct);
            allItems.AddRange(factItems);
            actualBreakdown[3] = factItems.Sum(i => i.Tokens);
        }

        // Format the context string
        var content = FormatContext(allItems);
        var totalTokens = allItems.Sum(i => i.Tokens);

        var bundle = new ContextBundle(
            Content: content,
            TotalTokens: totalTokens,
            Breakdown: new ContextBreakdown(
                actualBreakdown[0], actualBreakdown[1],
                actualBreakdown[2], actualBreakdown[3]),
            Items: allItems
        );

        _logger.LogInformation(
            "Built context: {Items} items, {Tokens} tokens (R:{Recent}/S:{Semantic}/E:{Episodic}/F:{Fact})",
            bundle.ItemCount, bundle.TotalTokens,
            bundle.Breakdown.RecentTokens, bundle.Breakdown.SemanticTokens,
            bundle.Breakdown.EpisodicTokens, bundle.Breakdown.FactTokens);

        return bundle;
    }

    private static string FormatContext(IReadOnlyList<ContextItem> items)
    {
        if (items.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();

        // Group by source for organized output
        var recentItems = items.Where(i => i.Source == ContextItemSource.Recent).ToList();
        var semanticItems = items.Where(i => i.Source == ContextItemSource.Semantic).ToList();
        var episodicItems = items.Where(i => i.Source == ContextItemSource.Episodic).ToList();
        var factItems = items.Where(i => i.Source == ContextItemSource.Fact).ToList();

        if (recentItems.Count > 0)
        {
            sb.AppendLine("## Recent Conversation");
            foreach (var item in recentItems)
            {
                var roleLabel = item.Role switch
                {
                    "user" => "User",
                    "assistant" => "Assistant",
                    "system" => "System",
                    _ => item.Role ?? "Unknown"
                };
                var age = FormatAge(item.Timestamp);
                sb.AppendLine($"[{roleLabel}, {age}] {item.Content}");
            }
            sb.AppendLine();
        }

        if (semanticItems.Count > 0)
        {
            sb.AppendLine("## Relevant Memories");
            foreach (var item in semanticItems)
            {
                var score = item.Score.HasValue ? $", score:{item.Score:F2}" : "";
                var age = FormatAge(item.Timestamp);
                sb.AppendLine($"[{item.MemoryType}, {age}{score}] {item.Content}");
            }
            sb.AppendLine();
        }

        if (episodicItems.Count > 0)
        {
            sb.AppendLine("## Session History");
            foreach (var item in episodicItems)
            {
                var age = FormatAge(item.Timestamp);
                sb.AppendLine($"[{age}] {item.Content}");
            }
            sb.AppendLine();
        }

        if (factItems.Count > 0)
        {
            sb.AppendLine("## Known Facts");
            foreach (var item in factItems)
            {
                sb.AppendLine($"- {item.Content}");
            }
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatAge(DateTime timestamp)
    {
        var age = DateTime.UtcNow - timestamp;
        return age.TotalMinutes < 1 ? "just now" :
               age.TotalMinutes < 60 ? $"{age.TotalMinutes:F0}m ago" :
               age.TotalHours < 24 ? $"{age.TotalHours:F0}h ago" :
               $"{age.TotalDays:F0}d ago";
    }
}
