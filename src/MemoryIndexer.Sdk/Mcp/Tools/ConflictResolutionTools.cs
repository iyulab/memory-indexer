using System.ComponentModel;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using ModelContextProtocol.Server;

// Use SDK types explicitly to avoid conflicts with core abstractions
using ContradictionType = MemoryIndexer.Sdk.Intelligence.Conflict.ContradictionType;
using ResolutionStrategy = MemoryIndexer.Sdk.Intelligence.Conflict.ResolutionStrategy;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for detecting and resolving contradictions in memory content.
/// Enables LLMs to identify conflicting information and apply resolution strategies.
/// </summary>
[McpServerToolType]
public sealed class ConflictResolutionTools
{
    private readonly MemoryService _memoryService;
    private readonly IContradictionDetector _contradictionDetector;
    private readonly IContradictionResolver _contradictionResolver;

    private const string DefaultUserId = "default";

    public ConflictResolutionTools(
        MemoryService memoryService,
        IContradictionDetector contradictionDetector,
        IContradictionResolver contradictionResolver)
    {
        _memoryService = memoryService;
        _contradictionDetector = contradictionDetector;
        _contradictionResolver = contradictionResolver;
    }

    /// <summary>
    /// Detect if new content contradicts existing memories.
    /// Use this before storing information that might conflict with what's already known.
    /// </summary>
    /// <param name="content">The new content to check for contradictions.</param>
    /// <param name="similarityThreshold">Minimum similarity to consider items related (0.0-1.0).</param>
    /// <param name="minConfidence">Minimum confidence to report a contradiction (0.0-1.0).</param>
    /// <param name="limit">Maximum existing memories to compare against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Contradiction detection result.</returns>
    [McpServerTool]
    [Description("Detect if new content contradicts existing memories. Use before storing potentially conflicting information.")]
    public async Task<DetectContradictionResult> DetectContradiction(
        [Description("Content to check for contradictions")] string content,
        [Description("Similarity threshold for relatedness (0.0-1.0)")] float similarityThreshold = 0.7f,
        [Description("Minimum confidence to report (0.0-1.0)")] float minConfidence = 0.6f,
        [Description("Max memories to compare (1-100)")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        // Find related memories first
        var existingMemories = await _memoryService.RecallAsync(
            DefaultUserId,
            content,
            Math.Clamp(limit, 1, 100),
            cancellationToken: cancellationToken);

        if (existingMemories.Count == 0)
        {
            return new DetectContradictionResult
            {
                Success = true,
                HasContradiction = false,
                Message = "No related memories found to check for contradictions."
            };
        }

        // Create a temporary memory unit for comparison
        var newMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            Content = content,
            Type = MemoryType.Episodic,
            CreatedAt = DateTime.UtcNow
        };

        var options = new ContradictionDetectionOptions
        {
            SimilarityThreshold = Math.Clamp(similarityThreshold, 0f, 1f),
            MinContradictionConfidence = Math.Clamp(minConfidence, 0f, 1f),
            MaxComparisonItems = Math.Clamp(limit, 1, 100)
        };

        var existingList = existingMemories.Select(r => r.Memory).ToList();
        var analysis = await _contradictionDetector.DetectMemoryContradictionAsync(
            newMemory, existingList, options, cancellationToken);

        return new DetectContradictionResult
        {
            Success = true,
            HasContradiction = analysis.HasContradiction,
            ContradictionType = analysis.Type.ToString(),
            Confidence = analysis.ContradictionConfidence,
            Description = analysis.ConflictDescription,
            ConflictingMemoryId = analysis.ConflictingItem?.Id.ToString(),
            ConflictingContent = analysis.ConflictingItem?.Content,
            SuggestedStrategy = analysis.HasContradiction
                ? _contradictionResolver.SuggestStrategy(analysis.Type, analysis.ContradictionConfidence).Strategy.ToString()
                : null,
            Message = analysis.HasContradiction
                ? $"Contradiction detected ({analysis.Type}) with {analysis.ContradictionConfidence:P0} confidence."
                : "No contradictions detected."
        };
    }

    /// <summary>
    /// Resolve a contradiction between new content and an existing memory.
    /// Use after detecting a contradiction to determine which information to keep.
    /// </summary>
    /// <param name="newContent">The new content that conflicts.</param>
    /// <param name="existingMemoryId">ID of the conflicting memory.</param>
    /// <param name="strategy">Resolution strategy to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolution result with recommended action.</returns>
    [McpServerTool]
    [Description("Resolve a contradiction between new content and existing memory. Apply a resolution strategy.")]
    public async Task<ResolveContradictionResult> ResolveContradiction(
        [Description("New content that conflicts")] string newContent,
        [Description("ID of the conflicting memory")] string existingMemoryId,
        [Description("Strategy: RecencyFirst, ConfidenceFirst, SourceAuthority, AskUser, KeepBoth, TemporalPartition")] string strategy = "RecencyFirst",
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(existingMemoryId, out var memoryId))
        {
            return new ResolveContradictionResult
            {
                Success = false,
                Message = "Invalid memory ID format."
            };
        }

        var existingMemory = await _memoryService.GetByIdAsync(memoryId, cancellationToken);
        if (existingMemory == null)
        {
            return new ResolveContradictionResult
            {
                Success = false,
                Message = $"Memory {existingMemoryId} not found."
            };
        }

        var newMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            Content = newContent,
            Type = MemoryType.Episodic,
            CreatedAt = DateTime.UtcNow
        };

        // First detect the contradiction to get analysis
        var analysis = await _contradictionDetector.DetectMemoryContradictionAsync(
            newMemory, [existingMemory], null, cancellationToken);

        if (!analysis.HasContradiction)
        {
            return new ResolveContradictionResult
            {
                Success = true,
                Message = "No contradiction detected between the content.",
                Action = "none"
            };
        }

        // Parse and apply strategy
        if (!Enum.TryParse<ResolutionStrategy>(strategy, true, out var resolutionStrategy))
        {
            resolutionStrategy = ResolutionStrategy.RecencyFirst;
        }

        var result = await _contradictionResolver.ResolveMemoryAsync(
            analysis, resolutionStrategy, cancellationToken);

        return new ResolveContradictionResult
        {
            Success = result.Success,
            AppliedStrategy = result.AppliedStrategy.ToString(),
            Action = DetermineAction(result),
            KeptContent = result.KeptItem?.Content,
            SupersededContent = result.SupersededItem?.Content,
            MergedContent = result.MergedItem?.Content,
            Explanation = result.Explanation,
            RequiresUserIntervention = result.RequiresUserIntervention,
            UserQuestion = result.UserQuestion,
            Message = result.Success
                ? $"Contradiction resolved using {result.AppliedStrategy} strategy."
                : "Resolution failed."
        };
    }

    /// <summary>
    /// Automatically detect and resolve contradictions for new content.
    /// Combines detection and resolution in a single call with auto-selected strategy.
    /// </summary>
    /// <param name="content">New content to check and potentially store.</param>
    /// <param name="applyResolution">Whether to apply the resolution (update/delete memories).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Auto-resolution result with action taken.</returns>
    [McpServerTool]
    [Description("Automatically detect and resolve contradictions for new content. Combines detection and resolution.")]
    public async Task<AutoResolveResult> AutoResolveContradiction(
        [Description("New content to check")] string content,
        [Description("Apply resolution changes to storage")] bool applyResolution = false,
        CancellationToken cancellationToken = default)
    {
        // Find related memories
        var existingMemories = await _memoryService.RecallAsync(
            DefaultUserId, content, 50, cancellationToken: cancellationToken);

        if (existingMemories.Count == 0)
        {
            return new AutoResolveResult
            {
                Success = true,
                HasContradiction = false,
                Message = "No related memories found. Safe to store.",
                RecommendedAction = "store"
            };
        }

        var newMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = DefaultUserId,
            Content = content,
            Type = MemoryType.Episodic,
            CreatedAt = DateTime.UtcNow
        };

        var existingList = existingMemories.Select(r => r.Memory).ToList();
        var analysis = await _contradictionDetector.DetectMemoryContradictionAsync(
            newMemory, existingList, null, cancellationToken);

        if (!analysis.HasContradiction)
        {
            return new AutoResolveResult
            {
                Success = true,
                HasContradiction = false,
                Message = "No contradictions detected. Safe to store.",
                RecommendedAction = "store"
            };
        }

        // Auto-resolve using the resolver's intelligence
        var resolution = await _contradictionResolver.AutoResolveMemoryAsync(analysis, cancellationToken);

        var result = new AutoResolveResult
        {
            Success = resolution.Success,
            HasContradiction = true,
            ContradictionType = analysis.Type.ToString(),
            Confidence = analysis.ContradictionConfidence,
            ConflictingMemoryId = analysis.ConflictingItem?.Id.ToString(),
            AppliedStrategy = resolution.AppliedStrategy.ToString(),
            RecommendedAction = DetermineAction(resolution),
            Explanation = resolution.Explanation,
            RequiresUserIntervention = resolution.RequiresUserIntervention,
            UserQuestion = resolution.UserQuestion,
            Message = resolution.RequiresUserIntervention
                ? "User intervention required to resolve this contradiction."
                : $"Resolved using {resolution.AppliedStrategy}: {resolution.Explanation}"
        };

        // Apply resolution if requested
        if (applyResolution && resolution.Success && !resolution.RequiresUserIntervention)
        {
            if (resolution.SupersededItem != null)
            {
                await _memoryService.DeleteAsync(resolution.SupersededItem.Id, false, cancellationToken);
                result.ActionsApplied = [$"Deleted superseded memory: {resolution.SupersededItem.Id}"];
            }

            if (resolution.MergedItem != null)
            {
                var stored = await _memoryService.StoreAsync(
                    DefaultUserId,
                    resolution.MergedItem.Content,
                    resolution.MergedItem.Type,
                    null, // sessionId
                    resolution.MergedItem.ImportanceScore,
                    null, // metadata
                    @namespace: null,
                    cancellationToken);
                result.ActionsApplied = (result.ActionsApplied ?? []).Concat(
                    [$"Created merged memory: {stored.Id}"]).ToList();
            }
        }

        return result;
    }

    /// <summary>
    /// Get a recommendation on how to handle conflicting information.
    /// Use this for guidance without making any changes.
    /// </summary>
    /// <param name="contradictionType">Type of contradiction (Factual, Temporal, Preference, Semantic, Logical).</param>
    /// <param name="confidence">Confidence level of the contradiction (0.0-1.0).</param>
    /// <returns>Strategy recommendation with explanation.</returns>
    [McpServerTool]
    [Description("Get a recommendation on how to handle a specific type of contradiction.")]
    public Task<StrategyRecommendationResult> GetResolutionStrategy(
        [Description("Contradiction type: Factual, Temporal, Preference, Semantic, Logical")] string contradictionType,
        [Description("Confidence level (0.0-1.0)")] float confidence)
    {
        if (!Enum.TryParse<ContradictionType>(contradictionType, true, out var type))
        {
            return Task.FromResult(new StrategyRecommendationResult
            {
                Success = false,
                Message = $"Unknown contradiction type: {contradictionType}. Valid types: Factual, Temporal, Preference, Semantic, Logical"
            });
        }

        var (strategy, explanation) = _contradictionResolver.SuggestStrategy(type, Math.Clamp(confidence, 0f, 1f));

        return Task.FromResult(new StrategyRecommendationResult
        {
            Success = true,
            ContradictionType = type.ToString(),
            RecommendedStrategy = strategy.ToString(),
            Explanation = explanation,
            AvailableStrategies = Enum.GetNames<ResolutionStrategy>().ToList(),
            Message = $"For {type} contradictions with {confidence:P0} confidence, recommend: {strategy}"
        });
    }

    private static string DetermineAction(ResolutionResult<MemoryUnit> result)
    {
        if (result.RequiresUserIntervention) return "ask_user";
        if (result.MergedItem != null) return "merge";
        if (result.SupersededItem != null) return "replace";
        if (result.KeptItem != null) return "keep_existing";
        return "none";
    }
}

#region Result Types

/// <summary>
/// Result of contradiction detection.
/// </summary>
public sealed class DetectContradictionResult
{
    public bool Success { get; init; }
    public bool HasContradiction { get; init; }
    public string? ContradictionType { get; init; }
    public float Confidence { get; init; }
    public string? Description { get; init; }
    public string? ConflictingMemoryId { get; init; }
    public string? ConflictingContent { get; init; }
    public string? SuggestedStrategy { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of contradiction resolution.
/// </summary>
public sealed class ResolveContradictionResult
{
    public bool Success { get; init; }
    public string? AppliedStrategy { get; init; }
    public string? Action { get; init; }
    public string? KeptContent { get; init; }
    public string? SupersededContent { get; init; }
    public string? MergedContent { get; init; }
    public string? Explanation { get; init; }
    public bool RequiresUserIntervention { get; init; }
    public string? UserQuestion { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of automatic contradiction resolution.
/// </summary>
public sealed class AutoResolveResult
{
    public bool Success { get; init; }
    public bool HasContradiction { get; init; }
    public string? ContradictionType { get; init; }
    public float Confidence { get; init; }
    public string? ConflictingMemoryId { get; init; }
    public string? AppliedStrategy { get; init; }
    public string? RecommendedAction { get; init; }
    public string? Explanation { get; init; }
    public bool RequiresUserIntervention { get; init; }
    public string? UserQuestion { get; init; }
    public List<string>? ActionsApplied { get; set; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// Result of strategy recommendation.
/// </summary>
public sealed class StrategyRecommendationResult
{
    public bool Success { get; init; }
    public string? ContradictionType { get; init; }
    public string? RecommendedStrategy { get; init; }
    public string? Explanation { get; init; }
    public List<string>? AvailableStrategies { get; init; }
    public string Message { get; init; } = string.Empty;
}

#endregion
