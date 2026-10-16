using System.Text.Json;

using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Conflict;

/// <summary>
/// LLM-powered semantic conflict detector.
/// Phase 26: Memory Conflict Resolution.
/// </summary>
/// <remarks>
/// Uses language models to detect and classify semantic conflicts between memories.
/// Provides natural language reasoning for conflict resolution decisions.
///
/// Based on research from AgentCore (AWS) and Memoria architectures.
/// Handles:
/// - Duplicate detection (paraphrase identification)
/// - Refinement detection (detail addition)
/// - Update detection (value changes)
/// - Contradiction detection (opposing facts)
/// - Temporal detection (time-based evolution)
/// </remarks>
public sealed partial class LlmConflictDetector
{
    private readonly ITextCompletionService _completionService;
    private readonly ILogger<LlmConflictDetector> _logger;

    public LlmConflictDetector(
        ITextCompletionService completionService,
        ILogger<LlmConflictDetector> logger)
    {
        _completionService = completionService;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes semantic relationship between two memories.
    /// </summary>
    public async Task<ConflictAnalysis> AnalyzeAsync(
        MemoryUnit newMemory,
        MemoryUnit existingMemory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = BuildConflictAnalysisPrompt(newMemory, existingMemory);

            var options = new TextCompletionOptions
            {
                Temperature = 0.1f,  // Low temperature for deterministic analysis
                MaxTokens = 300,
                StopSequences = new[] { "###" },
                ResponseFormat = CompletionResponseFormat.Json,
            };

            LogAnalyzingConflictNEWNewCreatedAtVs(_logger, newMemory.CreatedAt, existingMemory.CreatedAt);

            var (dto, error) = await LlmJson.CompleteAsync<ConflictAnalysisDto>(_completionService, prompt, options, cancellationToken);
            if (dto is null)
            {
                LogUnreadableConflictAnalysis(_logger, error);
                return CreateFallbackAnalysis(error);
            }

            var analysis = new ConflictAnalysis
            {
                ConflictType = ParseConflictType(dto.ConflictType),
                Confidence = dto.Confidence,
                RecommendedAction = ParseMemoryAction(dto.RecommendedAction),
                Reasoning = dto.Reasoning
            };

            LogConflictAnalysisTypeConfidenceConfidence(_logger, analysis.ConflictType, analysis.Confidence);

            return analysis;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailedAnalyzeConflict(_logger, ex);

            // Fallback: treat as unrelated if analysis fails
            return new ConflictAnalysis
            {
                ConflictType = ConflictType.None,
                Confidence = 0.5f,
                RecommendedAction = MemoryAction.Add,
                Reasoning = "Analysis failed, treating as new memory"
            };
        }
    }

    private static string BuildConflictAnalysisPrompt(
        MemoryUnit newMemory,
        MemoryUnit existingMemory)
    {
        return $$$"""
            Analyze the relationship between these two memories:

            Memory A (existing):
            Content: {{{existingMemory.Content}}}
            Created: {{{existingMemory.CreatedAt:yyyy-MM-dd HH:mm:ss}}}
            Confidence: {{{existingMemory.ConfidenceScore:F2}}}

            Memory B (new):
            Content: {{{newMemory.Content}}}
            Created: {{{newMemory.CreatedAt:yyyy-MM-dd HH:mm:ss}}}
            Confidence: {{{newMemory.ConfidenceScore:F2}}}

            Determine the relationship type:

            1. DUPLICATE - Identical semantic meaning (paraphrase)
               Example: "likes pizza" vs "enjoys pizza"

            2. REFINEMENT - B adds detail to A (not contradictory)
               Example: "likes pizza" → "loves margherita pizza"

            3. UPDATE - Same fact, changed value (factual update)
               Example: "age 25" → "age 26"
               Example: "lives in Seoul" → "moved to Busan"

            4. CONTRADICTION - Direct conflict between facts
               Example: "likes apples" vs "dislikes apples"

            5. TEMPORAL - Time-based evolution (preferences/situations change)
               Example: "used to smoke" vs "quit smoking in 2023"
               Example: "liked apples" vs "doesn't eat apples after getting sick"

            6. NONE - Unrelated topics or facts
               Example: "likes pizza" vs "age 25"

            Recommended actions:
            - DUPLICATE → NO_OP (keep existing, skip new)
            - REFINEMENT → MERGE (combine both)
            - UPDATE → REPLACE (new supersedes old)
            - CONTRADICTION → REPLACE or MARK_CONFLICT (depends on confidence/recency)
            - TEMPORAL → ARCHIVE (preserve old as historical, add new as current)
            - NONE → ADD (store as separate memory)

            Respond ONLY with one JSON object, no explanation and no code fence. conflictType is one of DUPLICATE, REFINEMENT,
            UPDATE, CONTRADICTION, TEMPORAL, NONE; recommendedAction is one of NO_OP, MERGE, REPLACE, ARCHIVE, MARK_CONFLICT, ADD;
            confidence is a number from 0.0 to 1.0; reasoning is one or two sentences. Example:
            {
              "conflictType": "UPDATE",
              "confidence": 0.85,
              "reasoning": "Both state where the user lives; B gives a newer value.",
              "recommendedAction": "REPLACE"
            }

            ###
            """;
    }

    private static ConflictType ParseConflictType(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "DUPLICATE" => ConflictType.Duplicate,
            "REFINEMENT" => ConflictType.Refinement,
            "UPDATE" => ConflictType.Update,
            "CONTRADICTION" => ConflictType.Contradiction,
            "TEMPORAL" => ConflictType.Temporal,
            "NONE" => ConflictType.None,
            _ => ConflictType.None
        };
    }

    private static MemoryAction ParseMemoryAction(string value)
    {
        return value.ToUpperInvariant().Replace("_", "") switch
        {
            "NOOP" or "NO-OP" => MemoryAction.NoOp,
            "MERGE" => MemoryAction.Merge,
            "REPLACE" => MemoryAction.Replace,
            "ARCHIVE" => MemoryAction.Archive,
            "MARKCONFLICT" or "MARK-CONFLICT" => MemoryAction.MarkConflict,
            "ADD" => MemoryAction.Add,
            _ => MemoryAction.Add
        };
    }

    private static ConflictAnalysis CreateFallbackAnalysis(string? error)
    {
        return new ConflictAnalysis
        {
            ConflictType = ConflictType.None,
            Confidence = 0.5f,
            RecommendedAction = MemoryAction.Add,
            Reasoning = $"Unable to analyze conflict, treating as new memory (parse error: {error})"
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Analyzing conflict: NEW[{NewCreatedAt}] vs EXISTING[{ExistingCreatedAt}]")]
    private static partial void LogAnalyzingConflictNEWNewCreatedAtVs(ILogger logger, DateTime newCreatedAt, DateTime existingCreatedAt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conflict analysis: {Type} (confidence: {Confidence})")]
    private static partial void LogConflictAnalysisTypeConfidenceConfidence(ILogger logger, ConflictType type, float confidence);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to analyze conflict")]
    private static partial void LogFailedAnalyzeConflict(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conflict analysis reply could not be read as JSON, also after one retry ({Reason}); treating the memory as new")]
    private static partial void LogUnreadableConflictAnalysis(ILogger logger, string? reason);
}

#region DTOs

internal sealed class ConflictAnalysisDto
{
    [JsonPropertyName("conflictType")]
    public required string ConflictType { get; init; }

    [JsonPropertyName("confidence")]
    public required float Confidence { get; init; }

    [JsonPropertyName("reasoning")]
    public required string Reasoning { get; init; }

    [JsonPropertyName("recommendedAction")]
    public required string RecommendedAction { get; init; }
}

#endregion

/// <summary>
/// Detailed analysis of conflict between two memories.
/// </summary>
public sealed class ConflictAnalysis
{
    /// <summary>
    /// Type of conflict detected.
    /// </summary>
    public required ConflictType ConflictType { get; init; }

    /// <summary>
    /// Confidence in the analysis (0-1).
    /// </summary>
    public required float Confidence { get; init; }

    /// <summary>
    /// Recommended action based on conflict type.
    /// </summary>
    public required MemoryAction RecommendedAction { get; init; }

    /// <summary>
    /// Human-readable reasoning.
    /// </summary>
    public required string Reasoning { get; init; }
}
