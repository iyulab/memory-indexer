using System.Text.Json;
using Flux.Abstractions;
using System.Text.Json.Serialization;
using MemoryIndexer.Interfaces;
using ITextCompletionService = Flux.Abstractions.ITextCompletionService;
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
                StopSequences = new[] { "###" }
            };

            LogAnalyzingConflictNEWNewCreatedAtVs(_logger, newMemory.CreatedAt, existingMemory.CreatedAt);

            var response = await _completionService.CompleteAsync(prompt, options, cancellationToken);

            var analysis = ParseConflictAnalysis(response);

            LogConflictAnalysisTypeConfidenceConfidence(_logger, analysis.ConflictType, analysis.Confidence);

            return analysis;
        }
        catch (Exception ex)
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

            Respond ONLY with valid JSON:
            {
              "conflictType": "DUPLICATE|REFINEMENT|UPDATE|CONTRADICTION|TEMPORAL|NONE",
              "confidence": 0.0-1.0,
              "reasoning": "brief explanation in 1-2 sentences",
              "recommendedAction": "NO_OP|MERGE|REPLACE|ARCHIVE|MARK_CONFLICT|ADD"
            }

            ###
            """;
    }

    private ConflictAnalysis ParseConflictAnalysis(string response)
    {
        try
        {
            // Extract JSON from response (handle potential markdown code blocks)
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart == -1 || jsonEnd == -1)
            {
                LogJSONFoundConflictAnalysisResponse(_logger);
                return CreateFallbackAnalysis();
            }

            var json = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var result = JsonSerializer.Deserialize<ConflictAnalysisDto>(json);

            if (result == null)
            {
                LogFailedDeserializeConflictAnalysisJSON(_logger);
                return CreateFallbackAnalysis();
            }

            // Map DTO to domain model
            return new ConflictAnalysis
            {
                ConflictType = ParseConflictType(result.ConflictType),
                Confidence = result.Confidence,
                RecommendedAction = ParseMemoryAction(result.RecommendedAction),
                Reasoning = result.Reasoning
            };
        }
        catch (JsonException ex)
        {
            LogFailedParseConflictAnalysisJSON(_logger, ex, response);
            return CreateFallbackAnalysis();
        }
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

    private static ConflictAnalysis CreateFallbackAnalysis()
    {
        return new ConflictAnalysis
        {
            ConflictType = ConflictType.None,
            Confidence = 0.5f,
            RecommendedAction = MemoryAction.Add,
            Reasoning = "Unable to analyze conflict, treating as new memory"
        };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Analyzing conflict: NEW[{NewCreatedAt}] vs EXISTING[{ExistingCreatedAt}]")]
    private static partial void LogAnalyzingConflictNEWNewCreatedAtVs(ILogger logger, DateTime newCreatedAt, DateTime existingCreatedAt);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conflict analysis: {Type} (confidence: {Confidence})")]
    private static partial void LogConflictAnalysisTypeConfidenceConfidence(ILogger logger, ConflictType type, float confidence);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to analyze conflict")]
    private static partial void LogFailedAnalyzeConflict(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No JSON found in conflict analysis response")]
    private static partial void LogJSONFoundConflictAnalysisResponse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to deserialize conflict analysis JSON")]
    private static partial void LogFailedDeserializeConflictAnalysisJSON(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse conflict analysis as JSON: {Response}")]
    private static partial void LogFailedParseConflictAnalysisJSON(ILogger logger, Exception ex, string response);
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
