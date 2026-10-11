using System.ComponentModel;
using System.Text;
using System.Text.Json;
using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.Globalization;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for memory reflection and self-correction: analyze a user's memories for contradictions, outdated and
/// duplicate entries, apply corrections, decay confidence, and synthesize higher-level insights.
/// </summary>
[McpServerToolType]
public sealed partial class MemoryReflectionTools
{
    private readonly IMemorySelfCorrector _selfCorrector;
    private readonly IReflectionEngine _reflectionEngine;
    private readonly ILogger<MemoryReflectionTools> _logger;

    public MemoryReflectionTools(
        IMemorySelfCorrector selfCorrector,
        IReflectionEngine reflectionEngine,
        ILogger<MemoryReflectionTools> logger)
    {
        _selfCorrector = selfCorrector;
        _reflectionEngine = reflectionEngine;
        _logger = logger;
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
        LogMemoryAnalysisUserUserId(_logger, userId);

        var options = new MemoryAnalysisOptions
        {
            FocusQuery = focusQuery,
            MaxMemoriesToAnalyze = 500
        };

        var result = await _selfCorrector.AnalyzeMemoriesAsync(userId, options, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Analysis Report");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Memories Analyzed**: {result.MemoriesAnalyzed}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Health Score**: {result.HealthScore:P0}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Duration**: {result.Duration.TotalMilliseconds:F0}ms");
        sb.AppendLine();

        sb.AppendLine("### Issues Found");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Contradictions: {result.Contradictions.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Outdated Memories: {result.OutdatedMemories.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Duplicate Groups: {result.DuplicateGroups.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Evidence Gaps: {result.EvidenceGaps.Count}");

        if (result.Contradictions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Top Contradictions");
            foreach (var c in result.Contradictions.Take(3))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {c.Type}: {c.Description} (confidence: {c.Confidence:F2})");
            }
        }

        if (result.SuggestedCorrections.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Suggested Corrections");
            foreach (var correction in result.SuggestedCorrections.Take(5))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {correction.Type} ({correction.Priority}): {correction.Reason}");
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
        LogApplyingMemoryCorrectionsUserUserId(_logger, userId);

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
            userId, analysis.SuggestedCorrections, options, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Memory Correction Result");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Success**: {result.Success}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Total Processed**: {result.TotalProcessed}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Applied**: {result.AppliedCorrections.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Failed**: {result.FailedCorrections.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Skipped**: {result.SkippedCorrections.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Duration**: {result.Duration.TotalMilliseconds:F0}ms");

        if (result.AppliedCorrections.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Applied Corrections");
            foreach (var correction in result.AppliedCorrections.Take(5))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {correction.Correction.Type}: {correction.Correction.Reason}");
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
        LogUpdatingConfidenceScoresUserUserId(_logger, userId);

        var options = new ConfidenceUpdateOptions
        {
            DecayHalfLifeDays = halfLifeDays
        };

        var result = await _selfCorrector.UpdateConfidenceScoresAsync(userId, options, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine("## Confidence Update Result");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Memories Updated**: {result.MemoriesUpdated}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Average Confidence Before**: {result.AverageConfidenceBefore:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Average Confidence After**: {result.AverageConfidenceAfter:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Low Confidence Memories**: {result.LowConfidenceMemories.Count}");

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
        LogReflectionTriggeredUserUserId(_logger, userId);

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
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Success**: {result.Success}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Memories Reflected**: {result.ReflectedMemoryIds.Count}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Duration**: {result.Duration.TotalMilliseconds:F0}ms");
        sb.AppendLine();
        sb.AppendLine(result.Summary);
        sb.AppendLine();

        if (result.Insights.Count > 0)
        {
            sb.AppendLine("### Insights Generated");
            foreach (var insight in result.Insights.Take(5))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- **{insight.Type}** (confidence: {insight.Confidence:F2}): {insight.Content}");
            }
            sb.AppendLine();
        }

        if (result.Patterns.Count > 0)
        {
            sb.AppendLine("### Patterns Identified");
            foreach (var pattern in result.Patterns.Take(3))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {pattern.Type}: {pattern.Description}");
            }
            sb.AppendLine();
        }

        if (result.Questions.Count > 0)
        {
            sb.AppendLine("### Questions to Explore");
            foreach (var question in result.Questions.Take(3))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {question.Question}");
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
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Should Reflect**: {(result.ShouldReflect ? "Yes" : "No")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Priority**: {result.Priority:F2}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Accumulated Memories**: {result.AccumulatedMemories}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Accumulated Importance**: {result.AccumulatedImportance:F0}");

        if (result.LastReflection.HasValue)
        {
            var timeSince = DateTime.UtcNow - result.LastReflection.Value;
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Last Reflection**: {timeSince.TotalHours:F1} hours ago");
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
                sb.AppendLine(CultureInfo.InvariantCulture, $"- {reason}");
            }
        }

        if (result.SuggestedTopics.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Suggested Focus Topics**: {string.Join(", ", result.SuggestedTopics)}");
        }

        return sb.ToString();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Memory analysis for user {UserId}")]
    private static partial void LogMemoryAnalysisUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Applying memory corrections for user {UserId}")]
    private static partial void LogApplyingMemoryCorrectionsUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Updating confidence scores for user {UserId}")]
    private static partial void LogUpdatingConfidenceScoresUserUserId(ILogger logger, string userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Reflection triggered for user {UserId}")]
    private static partial void LogReflectionTriggeredUserUserId(ILogger logger, string userId);
}
