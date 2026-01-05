using System.ComponentModel;
using System.Text;
using System.Text.Json;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for autonomous memory management.
/// Exposes MemGPT-style self-directed memory operations.
/// </summary>
[McpServerToolType]
public sealed class AutonomousMemoryTools
{
    private readonly IAutonomousMemoryManager _memoryManager;
    private readonly IMemorySelfCorrector _selfCorrector;
    private readonly IReflectionEngine _reflectionEngine;
    private readonly ILogger<AutonomousMemoryTools> _logger;

    public AutonomousMemoryTools(
        IAutonomousMemoryManager memoryManager,
        IMemorySelfCorrector selfCorrector,
        IReflectionEngine reflectionEngine,
        ILogger<AutonomousMemoryTools> logger)
    {
        _memoryManager = memoryManager;
        _selfCorrector = selfCorrector;
        _reflectionEngine = reflectionEngine;
        _logger = logger;
    }

    /// <summary>
    /// Performs an autonomous memory heartbeat check.
    /// Returns memory state and recommendations.
    /// </summary>
    [McpServerTool, Description("Perform autonomous memory heartbeat. Returns memory state, health status, and recommended actions.")]
    public async Task<string> MemoryHeartbeat(
        [Description("Current conversation context")] string context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Memory heartbeat triggered");

        var response = await _memoryManager.HeartbeatAsync(context, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Heartbeat Report");
        sb.AppendLine();
        sb.AppendLine($"**Health Status**: {response.HealthStatus}");
        sb.AppendLine($"**Context Utilization**: {response.State.UtilizationPercent:F1}%");
        sb.AppendLine($"**Working Memory**: {response.State.WorkingMemoryCount} items");
        sb.AppendLine($"**Archival Memory**: {response.State.ArchivalMemoryCount} items");
        sb.AppendLine();

        if (response.Alerts.Count > 0)
        {
            sb.AppendLine("### Alerts");
            foreach (var alert in response.Alerts)
            {
                sb.AppendLine($"- [{alert.Severity}] {alert.Message}");
            }
            sb.AppendLine();
        }

        if (response.RecommendedActions.Count > 0)
        {
            sb.AppendLine("### Recommended Actions");
            foreach (var action in response.RecommendedActions.Take(5))
            {
                sb.AppendLine($"- **{action.OperationType}** ({action.Priority}): {action.Reason}");
            }
            sb.AppendLine();
        }

        if (response.ImmediateActionRequired)
        {
            sb.AppendLine("⚠️ **Immediate action required!**");
        }

        sb.AppendLine($"Next heartbeat in: {response.NextHeartbeatIn.TotalMinutes:F0} minutes");

        return sb.ToString();
    }

    /// <summary>
    /// Pages in relevant memories based on a query.
    /// </summary>
    [McpServerTool, Description("Page relevant memories into working context based on a query. Returns retrieved memories.")]
    public async Task<string> MemoryPageIn(
        [Description("Query to find relevant memories")] string query,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Memory page-in for query: {Query}", query);

        var response = await _memoryManager.AutonomousPageInAsync(query, null, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Page-In Result");
        sb.AppendLine();
        sb.AppendLine($"**Success**: {response.Success}");
        sb.AppendLine($"**Memories Retrieved**: {response.PagedInMemories.Count}");
        sb.AppendLine($"**Tokens Added**: {response.TokensAdded}");

        if (response.EvictionRequired)
        {
            sb.AppendLine($"**Eviction Required**: Yes ({response.EvictedMemories.Count} memories evicted)");
        }

        sb.AppendLine();

        if (response.PagedInMemories.Count > 0)
        {
            sb.AppendLine("### Retrieved Memories");
            foreach (var memory in response.PagedInMemories.Take(10))
            {
                var preview = TruncateContent(memory.Memory.Content, 150);
                sb.AppendLine($"- **{memory.Memory.Type}** (relevance: {memory.RelevanceScore:F2}): {preview}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Pages out memories to free context space.
    /// </summary>
    [McpServerTool, Description("Page out memories from working context to free space. Specify tokens to free.")]
    public async Task<string> MemoryPageOut(
        [Description("Number of tokens to free")] int tokensToFree,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Memory page-out: {Tokens} tokens", tokensToFree);

        var response = await _memoryManager.AutonomousPageOutAsync(tokensToFree, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Page-Out Result");
        sb.AppendLine();
        sb.AppendLine($"**Success**: {response.Success}");
        sb.AppendLine($"**Memories Paged Out**: {response.PagedOutMemories.Count}");
        sb.AppendLine($"**Tokens Freed**: {response.TokensFreed}");

        if (response.ArchivedMemories.Count > 0)
        {
            sb.AppendLine($"**Memories Archived**: {response.ArchivedMemories.Count}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Optimizes memory organization proactively.
    /// </summary>
    [McpServerTool, Description("Optimize memory organization. Compresses, consolidates, and archives memories.")]
    public async Task<string> MemoryOptimize(
        [Description("Target utilization percentage (0-100)")] float targetUtilization = 70f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Memory optimization at target {Target}%", targetUtilization);

        var options = new OptimizationOptions
        {
            TargetUtilization = targetUtilization
        };

        var result = await _memoryManager.OptimizeMemoryAsync(options, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Optimization Result");
        sb.AppendLine();
        sb.AppendLine($"**Success**: {result.Success}");
        sb.AppendLine($"**Tokens Before**: {result.TokensBefore}");
        sb.AppendLine($"**Tokens After**: {result.TokensAfter}");
        sb.AppendLine($"**Tokens Saved**: {result.TokensBefore - result.TokensAfter}");
        sb.AppendLine($"**Duration**: {result.Duration.TotalMilliseconds:F0}ms");
        sb.AppendLine();
        sb.AppendLine($"- Memories Compressed: {result.MemoriesCompressed}");
        sb.AppendLine($"- Memories Consolidated: {result.MemoriesConsolidated}");
        sb.AppendLine($"- Memories Archived: {result.MemoriesArchived}");

        if (result.ActionsTaken.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Actions Taken");
            foreach (var action in result.ActionsTaken.Take(5))
            {
                sb.AppendLine($"- {action.Type}: {action.Description}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Analyzes memories for issues like contradictions and outdated information.
    /// </summary>
    [McpServerTool, Description("Analyze memories for contradictions, outdated info, and duplicates. Returns health assessment.")]
    public async Task<string> MemoryAnalyze(
        [Description("User ID")] string userId,
        [Description("Focus query (optional)")] string? focusQuery = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Memory analysis for user {UserId}", userId);

        var options = new MemoryAnalysisOptions
        {
            FocusQuery = focusQuery,
            MaxMemoriesToAnalyze = 500
        };

        var result = await _selfCorrector.AnalyzeMemoriesAsync(userId, options, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Analysis Report");
        sb.AppendLine();
        sb.AppendLine($"**Memories Analyzed**: {result.MemoriesAnalyzed}");
        sb.AppendLine($"**Health Score**: {result.HealthScore:P0}");
        sb.AppendLine($"**Duration**: {result.Duration.TotalMilliseconds:F0}ms");
        sb.AppendLine();

        sb.AppendLine("### Issues Found");
        sb.AppendLine($"- Contradictions: {result.Contradictions.Count}");
        sb.AppendLine($"- Outdated Memories: {result.OutdatedMemories.Count}");
        sb.AppendLine($"- Duplicate Groups: {result.DuplicateGroups.Count}");
        sb.AppendLine($"- Evidence Gaps: {result.EvidenceGaps.Count}");

        if (result.Contradictions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Top Contradictions");
            foreach (var c in result.Contradictions.Take(3))
            {
                sb.AppendLine($"- {c.Type}: {c.Description} (confidence: {c.Confidence:F2})");
            }
        }

        if (result.SuggestedCorrections.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Suggested Corrections");
            foreach (var correction in result.SuggestedCorrections.Take(5))
            {
                sb.AppendLine($"- {correction.Type} ({correction.Priority}): {correction.Reason}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Applies corrections to memories.
    /// </summary>
    [McpServerTool, Description("Apply corrections to memories. Resolves contradictions and updates outdated info.")]
    public async Task<string> MemoryCorrect(
        [Description("User ID")] string userId,
        [Description("Minimum priority (Low, Normal, High, Critical)")] string minPriority = "Normal",
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Applying memory corrections for user {UserId}", userId);

        // First analyze to get corrections
        var analysis = await _selfCorrector.AnalyzeMemoriesAsync(userId, null, cancellationToken);

        if (analysis.SuggestedCorrections.Count == 0)
        {
            return "No corrections needed. Memory health is good.";
        }

        var priority = Enum.TryParse<CorrectionPriority>(minPriority, true, out var p)
            ? p
            : CorrectionPriority.Normal;

        var options = new CorrectionOptions
        {
            MinPriority = priority,
            CreateBackup = true,
            RecordHistory = true
        };

        var result = await _selfCorrector.ApplyCorrectionsAsync(
            analysis.SuggestedCorrections, options, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Correction Result");
        sb.AppendLine();
        sb.AppendLine($"**Success**: {result.Success}");
        sb.AppendLine($"**Total Processed**: {result.TotalProcessed}");
        sb.AppendLine($"**Applied**: {result.AppliedCorrections.Count}");
        sb.AppendLine($"**Failed**: {result.FailedCorrections.Count}");
        sb.AppendLine($"**Skipped**: {result.SkippedCorrections.Count}");
        sb.AppendLine($"**Duration**: {result.Duration.TotalMilliseconds:F0}ms");

        if (result.AppliedCorrections.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Applied Corrections");
            foreach (var correction in result.AppliedCorrections.Take(5))
            {
                sb.AppendLine($"- {correction.Correction.Type}: {correction.Correction.Reason}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Updates confidence scores with time decay.
    /// </summary>
    [McpServerTool, Description("Update memory confidence scores with time decay and access patterns.")]
    public async Task<string> MemoryUpdateConfidence(
        [Description("User ID")] string userId,
        [Description("Decay half-life in days")] int halfLifeDays = 30,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating confidence scores for user {UserId}", userId);

        var options = new ConfidenceUpdateOptions
        {
            DecayHalfLifeDays = halfLifeDays
        };

        var result = await _selfCorrector.UpdateConfidenceScoresAsync(userId, options, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Confidence Update Result");
        sb.AppendLine();
        sb.AppendLine($"**Memories Updated**: {result.MemoriesUpdated}");
        sb.AppendLine($"**Average Confidence Before**: {result.AverageConfidenceBefore:F2}");
        sb.AppendLine($"**Average Confidence After**: {result.AverageConfidenceAfter:F2}");
        sb.AppendLine($"**Low Confidence Memories**: {result.LowConfidenceMemories.Count}");

        return sb.ToString();
    }

    /// <summary>
    /// Triggers a reflection cycle to generate insights.
    /// </summary>
    [McpServerTool, Description("Trigger reflection to generate insights from recent memories. Creates higher-level understanding.")]
    public async Task<string> MemoryReflect(
        [Description("User ID")] string userId,
        [Description("Focus topic (optional)")] string? focusTopic = null,
        [Description("Time window in hours")] int hoursBack = 24,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reflection triggered for user {UserId}", userId);

        var options = new ReflectionOptions
        {
            TimeWindow = TimeSpan.FromHours(hoursBack),
            FocusTopic = focusTopic,
            GenerateInsights = true,
            DiscoverLinks = true,
            IdentifyPatterns = true
        };

        var result = await _reflectionEngine.ReflectAsync(userId, options, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Reflection Result");
        sb.AppendLine();
        sb.AppendLine($"**Success**: {result.Success}");
        sb.AppendLine($"**Memories Reflected**: {result.ReflectedMemoryIds.Count}");
        sb.AppendLine($"**Duration**: {result.Duration.TotalMilliseconds:F0}ms");
        sb.AppendLine();
        sb.AppendLine(result.Summary);
        sb.AppendLine();

        if (result.Insights.Count > 0)
        {
            sb.AppendLine("### Insights Generated");
            foreach (var insight in result.Insights.Take(5))
            {
                sb.AppendLine($"- **{insight.Type}** (confidence: {insight.Confidence:F2}): {insight.Content}");
            }
            sb.AppendLine();
        }

        if (result.Patterns.Count > 0)
        {
            sb.AppendLine("### Patterns Identified");
            foreach (var pattern in result.Patterns.Take(3))
            {
                sb.AppendLine($"- {pattern.Type}: {pattern.Description}");
            }
            sb.AppendLine();
        }

        if (result.Questions.Count > 0)
        {
            sb.AppendLine("### Questions to Explore");
            foreach (var question in result.Questions.Take(3))
            {
                sb.AppendLine($"- {question.Question}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Checks if reflection should be triggered.
    /// </summary>
    [McpServerTool, Description("Check if reflection should be triggered based on accumulated memories and importance.")]
    public async Task<string> ShouldReflect(
        [Description("User ID")] string userId,
        CancellationToken cancellationToken = default)
    {
        var result = await _reflectionEngine.ShouldReflectAsync(userId, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Reflection Check");
        sb.AppendLine();
        sb.AppendLine($"**Should Reflect**: {(result.ShouldReflect ? "Yes" : "No")}");
        sb.AppendLine($"**Priority**: {result.Priority:F2}");
        sb.AppendLine($"**Accumulated Memories**: {result.AccumulatedMemories}");
        sb.AppendLine($"**Accumulated Importance**: {result.AccumulatedImportance:F0}");

        if (result.LastReflection.HasValue)
        {
            var timeSince = DateTime.UtcNow - result.LastReflection.Value;
            sb.AppendLine($"**Last Reflection**: {timeSince.TotalHours:F1} hours ago");
        }
        else
        {
            sb.AppendLine("**Last Reflection**: Never");
        }

        if (result.Reasons.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Reasons");
            foreach (var reason in result.Reasons)
            {
                sb.AppendLine($"- {reason}");
            }
        }

        if (result.SuggestedTopics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**Suggested Focus Topics**: {string.Join(", ", result.SuggestedTopics)}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets memory activity summary.
    /// </summary>
    [McpServerTool, Description("Get summary of recent memory activity including top topics and entities.")]
    public async Task<string> MemoryActivitySummary(
        [Description("User ID")] string userId,
        [Description("Hours to summarize")] int hours = 24,
        CancellationToken cancellationToken = default)
    {
        var summary = await _reflectionEngine.SummarizeActivityAsync(
            userId, TimeSpan.FromHours(hours), cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Activity Summary");
        sb.AppendLine();
        sb.AppendLine($"**Period**: Last {summary.Period.TotalHours:F0} hours");
        sb.AppendLine($"**Memories Created**: {summary.MemoriesCreated}");
        sb.AppendLine($"**Memories Updated**: {summary.MemoriesUpdated}");
        sb.AppendLine($"**Average Importance**: {summary.AverageImportance:F2}");
        sb.AppendLine();

        if (summary.TypeDistribution.Count > 0)
        {
            sb.AppendLine("### Memory Types");
            foreach (var (type, count) in summary.TypeDistribution.OrderByDescending(kv => kv.Value))
            {
                sb.AppendLine($"- {type}: {count}");
            }
            sb.AppendLine();
        }

        if (summary.TopEntities.Count > 0)
        {
            sb.AppendLine("### Top Entities");
            foreach (var entity in summary.TopEntities.Take(5))
            {
                sb.AppendLine($"- {entity.Entity}: {entity.MentionCount} mentions");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets suggested memory operations.
    /// </summary>
    [McpServerTool, Description("Get suggested memory operations based on current state and patterns.")]
    public async Task<string> MemorySuggestions(
        CancellationToken cancellationToken = default)
    {
        var suggestions = await _memoryManager.GetSuggestedOperationsAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Suggested Memory Operations");
        sb.AppendLine();

        if (suggestions.Count == 0)
        {
            sb.AppendLine("No operations suggested at this time. Memory state is healthy.");
            return sb.ToString();
        }

        foreach (var suggestion in suggestions)
        {
            sb.AppendLine($"### {suggestion.OperationType} ({suggestion.Priority})");
            sb.AppendLine($"**Reason**: {suggestion.Reason}");
            sb.AppendLine($"**Estimated Benefit**: {suggestion.EstimatedBenefit:F2}");
            if (suggestion.TargetMemoryIds?.Count > 0)
            {
                sb.AppendLine($"**Target Memories**: {suggestion.TargetMemoryIds.Count}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        return content.Length <= maxLength
            ? content
            : content[..maxLength] + "...";
    }
}
