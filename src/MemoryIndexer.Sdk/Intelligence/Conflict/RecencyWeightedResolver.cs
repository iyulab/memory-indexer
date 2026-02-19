using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Conflict;

/// <summary>
/// Recency-weighted memory conflict resolver.
/// Phase 26: Memory Conflict Resolution.
/// </summary>
/// <remarks>
/// Implements exponential decay for recency weighting, prioritizing recent memories
/// while preserving high-confidence historical context.
///
/// Based on Memoria architecture (arXiv 2512.12686v1):
/// - Applies exponential decay: weight = exp(-λ * age_in_days)
/// - Combines recency with confidence for final scoring
/// - Requires 20% advantage for replacement to avoid thrashing
///
/// Resolution Strategy:
/// - Recent + high-confidence wins over old + low-confidence
/// - Close scores trigger conflict marking for review
/// - Temporal conflicts preserve historical context via archiving
/// </remarks>
public sealed partial class RecencyWeightedResolver : IMemoryConflictResolver
{
    private readonly LlmConflictDetector _conflictDetector;
    private readonly ILogger<RecencyWeightedResolver> _logger;

    // Exponential decay rate: λ = 0.1 (10% decay per day)
    // After 7 days: weight ≈ 0.50
    // After 30 days: weight ≈ 0.05
    private const float DECAY_RATE = 0.1f;

    // Require 20% score advantage for replacement (avoid thrashing)
    private const float REPLACEMENT_THRESHOLD = 1.2f;

    public RecencyWeightedResolver(
        LlmConflictDetector conflictDetector,
        ILogger<RecencyWeightedResolver> logger)
    {
        _conflictDetector = conflictDetector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ConflictResolution> ResolveAsync(
        MemoryUnit newMemory,
        IReadOnlyList<MemoryUnit> similarMemories,
        CancellationToken cancellationToken = default)
    {
        if (similarMemories.Count == 0)
        {
            // No conflict, add as new memory
            return new ConflictResolution
            {
                Action = MemoryAction.Add,
                ConflictType = ConflictType.None,
                Confidence = 1.0f,
                Reasoning = "No similar memories found"
            };
        }

        // Find most similar memory for conflict analysis
        var mostSimilar = similarMemories[0];

        // Use LLM to analyze semantic conflict
        var analysis = await _conflictDetector.AnalyzeAsync(
            newMemory,
            mostSimilar,
            cancellationToken);

        // Apply recency-weighted resolution logic
        var resolution = ApplyRecencyWeighting(newMemory, mostSimilar, analysis);

        LogConflictResolvedActionTypeType(_logger, resolution.Action, resolution.ConflictType, resolution.Confidence);

        return resolution;
    }

    private ConflictResolution ApplyRecencyWeighting(
        MemoryUnit newMemory,
        MemoryUnit existingMemory,
        ConflictAnalysis analysis)
    {
        // For duplicates and refinements, use LLM recommendation directly
        if (analysis.ConflictType is ConflictType.Duplicate or ConflictType.Refinement)
        {
            return new ConflictResolution
            {
                Action = analysis.RecommendedAction,
                ConflictType = analysis.ConflictType,
                Confidence = analysis.Confidence,
                TargetMemoryId = analysis.RecommendedAction is MemoryAction.Merge
                    ? existingMemory.Id.ToString()
                    : null,
                Reasoning = analysis.Reasoning
            };
        }

        // For updates, always replace (newer info supersedes)
        if (analysis.ConflictType == ConflictType.Update)
        {
            return new ConflictResolution
            {
                Action = MemoryAction.Replace,
                ConflictType = ConflictType.Update,
                Confidence = analysis.Confidence,
                TargetMemoryId = existingMemory.Id.ToString(),
                Reasoning = analysis.Reasoning
            };
        }

        // For temporal changes, archive old and add new
        if (analysis.ConflictType == ConflictType.Temporal)
        {
            return new ConflictResolution
            {
                Action = MemoryAction.Archive,
                ConflictType = ConflictType.Temporal,
                Confidence = analysis.Confidence,
                TargetMemoryId = existingMemory.Id.ToString(),
                UpdatedContent = null,  // Archive logic in orchestrator
                Reasoning = $"{analysis.Reasoning} (preserving historical context)"
            };
        }

        // For contradictions, apply recency-weighted scoring
        if (analysis.ConflictType == ConflictType.Contradiction)
        {
            var newScore = ComputeMemoryScore(newMemory);
            var existingScore = ComputeMemoryScore(existingMemory);

            LogContradictionDetectedNEWScoreNewScore(_logger, newScore, existingScore);

            if (newScore > existingScore * REPLACEMENT_THRESHOLD)
            {
                // New memory significantly stronger, replace
                return new ConflictResolution
                {
                    Action = MemoryAction.Replace,
                    ConflictType = ConflictType.Contradiction,
                    Confidence = analysis.Confidence,
                    TargetMemoryId = existingMemory.Id.ToString(),
                    Reasoning = $"{analysis.Reasoning} (new memory has {newScore / existingScore:F2}x score advantage)"
                };
            }
            else if (existingScore > newScore * REPLACEMENT_THRESHOLD)
            {
                // Existing memory significantly stronger, keep it
                return new ConflictResolution
                {
                    Action = MemoryAction.NoOp,
                    ConflictType = ConflictType.Contradiction,
                    Confidence = analysis.Confidence,
                    Reasoning = $"{analysis.Reasoning} (existing memory has {existingScore / newScore:F2}x score advantage)"
                };
            }
            else
            {
                // Too close to call, mark for review
                return new ConflictResolution
                {
                    Action = MemoryAction.MarkConflict,
                    ConflictType = ConflictType.Contradiction,
                    Confidence = 0.5f,  // Low confidence when unclear
                    TargetMemoryId = existingMemory.Id.ToString(),
                    Reasoning = $"{analysis.Reasoning} (scores too close: new={newScore:F2}, existing={existingScore:F2})"
                };
            }
        }

        // Fallback: no conflict detected
        return new ConflictResolution
        {
            Action = MemoryAction.Add,
            ConflictType = ConflictType.None,
            Confidence = analysis.Confidence,
            Reasoning = analysis.Reasoning
        };
    }

    /// <summary>
    /// Computes memory score combining recency and confidence.
    /// Score = recency_weight * confidence
    /// </summary>
    private static float ComputeMemoryScore(MemoryUnit memory)
    {
        var recencyWeight = ComputeRecencyWeight(memory.UpdatedAt);
        var confidence = memory.ConfidenceScore ?? 0.7f;  // Default confidence if not set
        return recencyWeight * confidence;
    }

    /// <summary>
    /// Computes exponential decay weight for memory age.
    /// weight = exp(-λ * age_in_days)
    /// where λ = DECAY_RATE (0.1)
    /// </summary>
    private static float ComputeRecencyWeight(DateTime timestamp)
    {
        var ageInDays = (DateTime.UtcNow - timestamp).TotalDays;
        return (float)Math.Exp(-DECAY_RATE * ageInDays);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Conflict resolved: {Action} (type: {Type}, confidence: {Confidence})")]
    private static partial void LogConflictResolvedActionTypeType(ILogger logger, MemoryAction action, ConflictType type, float confidence);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Contradiction detected - NEW score: {NewScore:F3}, EXISTING score: {ExistingScore:F3}")]
    private static partial void LogContradictionDetectedNEWScoreNewScore(ILogger logger, float newScore, float existingScore);
}
