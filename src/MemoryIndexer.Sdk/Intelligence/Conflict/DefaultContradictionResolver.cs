using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Sdk.Intelligence.Conflict;

/// <summary>
/// Default implementation of contradiction resolution.
/// Implements multiple resolution strategies based on research best practices.
/// </summary>
public sealed class DefaultContradictionResolver : IContradictionResolver
{
    private readonly ITemporalEntityStore? _temporalEntityStore;
    private readonly ILogger<DefaultContradictionResolver> _logger;

    public DefaultContradictionResolver(
        ILogger<DefaultContradictionResolver> logger,
        ITemporalEntityStore? temporalEntityStore = null)
    {
        _logger = logger;
        _temporalEntityStore = temporalEntityStore;
    }

    /// <inheritdoc />
    public async Task<ResolutionResult<MemoryUnit>> ResolveMemoryAsync(
        ContradictionAnalysis<MemoryUnit> analysis,
        ResolutionStrategy strategy = ResolutionStrategy.RecencyFirst,
        CancellationToken cancellationToken = default)
    {
        if (!analysis.HasContradiction)
        {
            return new ResolutionResult<MemoryUnit>
            {
                Success = true,
                AppliedStrategy = strategy,
                Explanation = "No contradiction to resolve"
            };
        }

        _logger.LogInformation(
            "Resolving {Type} memory contradiction using {Strategy} strategy",
            analysis.Type, strategy);

        return strategy switch
        {
            ResolutionStrategy.RecencyFirst => ResolveMemoryByRecency(analysis),
            ResolutionStrategy.ConfidenceFirst => ResolveMemoryByConfidence(analysis),
            ResolutionStrategy.SourceAuthority => ResolveMemoryBySourceAuthority(analysis),
            ResolutionStrategy.AskUser => CreateMemoryUserIntervention(analysis),
            ResolutionStrategy.KeepBoth => KeepBothMemories(analysis),
            ResolutionStrategy.TemporalPartition => ResolveMemoryByRecency(analysis), // Fall back to recency for memories
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }

    /// <inheritdoc />
    public async Task<ResolutionResult<EntityTriple>> ResolveTripleAsync(
        ContradictionAnalysis<EntityTriple> analysis,
        ResolutionStrategy strategy = ResolutionStrategy.RecencyFirst,
        CancellationToken cancellationToken = default)
    {
        if (!analysis.HasContradiction)
        {
            return new ResolutionResult<EntityTriple>
            {
                Success = true,
                AppliedStrategy = strategy,
                Explanation = "No contradiction to resolve"
            };
        }

        _logger.LogInformation(
            "Resolving {Type} triple contradiction using {Strategy} strategy",
            analysis.Type, strategy);

        return strategy switch
        {
            ResolutionStrategy.RecencyFirst => await ResolveTripleByRecencyAsync(analysis, cancellationToken),
            ResolutionStrategy.ConfidenceFirst => ResolveTripleByConfidence(analysis),
            ResolutionStrategy.SourceAuthority => ResolveTripleBySourceAuthority(analysis),
            ResolutionStrategy.AskUser => CreateTripleUserIntervention(analysis),
            ResolutionStrategy.KeepBoth => KeepBothTriples(analysis),
            ResolutionStrategy.TemporalPartition => ResolveTripleByTemporalPartition(analysis),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }

    /// <inheritdoc />
    public async Task<ResolutionResult<MemoryUnit>> AutoResolveMemoryAsync(
        ContradictionAnalysis<MemoryUnit> analysis,
        CancellationToken cancellationToken = default)
    {
        var (strategy, _) = SuggestStrategy(analysis.Type, analysis.ContradictionConfidence);
        return await ResolveMemoryAsync(analysis, strategy, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ResolutionResult<EntityTriple>> AutoResolveTripleAsync(
        ContradictionAnalysis<EntityTriple> analysis,
        CancellationToken cancellationToken = default)
    {
        var (strategy, _) = SuggestStrategy(analysis.Type, analysis.ContradictionConfidence);
        return await ResolveTripleAsync(analysis, strategy, cancellationToken);
    }

    /// <inheritdoc />
    public (ResolutionStrategy Strategy, string Explanation) SuggestStrategy(
        ContradictionType type,
        float confidence)
    {
        return type switch
        {
            // Temporal contradictions should use temporal partitioning
            ContradictionType.Temporal => (
                ResolutionStrategy.TemporalPartition,
                "Temporal contradiction detected - using time-based partitioning to preserve history"),

            // Factual contradictions with high confidence should use recency
            ContradictionType.Factual when confidence > 0.8f => (
                ResolutionStrategy.RecencyFirst,
                "High-confidence factual contradiction - newer information likely more accurate"),

            // Low confidence contradictions should keep both
            _ when confidence < 0.6f => (
                ResolutionStrategy.KeepBoth,
                "Low confidence contradiction - keeping both with explicit relation"),

            // Preference contradictions may need user input
            ContradictionType.Preference => (
                ResolutionStrategy.AskUser,
                "Preference contradiction detected - user clarification recommended"),

            // Semantic contradictions use confidence-based resolution
            ContradictionType.Semantic => (
                ResolutionStrategy.ConfidenceFirst,
                "Semantic contradiction - using confidence scores to determine truth"),

            // Default to recency for other cases
            _ => (
                ResolutionStrategy.RecencyFirst,
                "Default resolution - preferring more recent information")
        };
    }

    #region Memory Resolution Methods

    private ResolutionResult<MemoryUnit> ResolveMemoryByRecency(ContradictionAnalysis<MemoryUnit> analysis)
    {
        var newItem = analysis.NewItem;
        var existing = analysis.ConflictingItem!;

        var (kept, superseded) = newItem.CreatedAt >= existing.CreatedAt
            ? (newItem, existing)
            : (existing, newItem);

        return new ResolutionResult<MemoryUnit>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.RecencyFirst,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = $"Kept more recent memory (created {kept.CreatedAt:yyyy-MM-dd HH:mm})"
        };
    }

    private ResolutionResult<MemoryUnit> ResolveMemoryByConfidence(ContradictionAnalysis<MemoryUnit> analysis)
    {
        var newItem = analysis.NewItem;
        var existing = analysis.ConflictingItem!;

        var (kept, superseded) = newItem.ImportanceScore >= existing.ImportanceScore
            ? (newItem, existing)
            : (existing, newItem);

        return new ResolutionResult<MemoryUnit>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.ConfidenceFirst,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = $"Kept memory with higher importance ({kept.ImportanceScore:P0} vs {superseded.ImportanceScore:P0})"
        };
    }

    private ResolutionResult<MemoryUnit> ResolveMemoryBySourceAuthority(ContradictionAnalysis<MemoryUnit> analysis)
    {
        var newItem = analysis.NewItem;
        var existing = analysis.ConflictingItem!;

        var newHasSource = (newItem.Metadata != null && newItem.Metadata.Count > 0) || !string.IsNullOrEmpty(newItem.SessionId);
        var existingHasSource = (existing.Metadata != null && existing.Metadata.Count > 0) || !string.IsNullOrEmpty(existing.SessionId);

        MemoryUnit kept, superseded;
        string explanation;

        if (newHasSource && !existingHasSource)
        {
            kept = newItem;
            superseded = existing;
            explanation = "Kept memory with source attribution";
        }
        else if (existingHasSource && !newHasSource)
        {
            kept = existing;
            superseded = newItem;
            explanation = "Kept memory with source attribution";
        }
        else
        {
            // Fall back to recency
            (kept, superseded) = newItem.CreatedAt >= existing.CreatedAt
                ? (newItem, existing)
                : (existing, newItem);
            explanation = "Both memories have similar authority - fell back to recency";
        }

        return new ResolutionResult<MemoryUnit>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.SourceAuthority,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = explanation
        };
    }

    private ResolutionResult<MemoryUnit> CreateMemoryUserIntervention(ContradictionAnalysis<MemoryUnit> analysis)
    {
        var question = $"Conflicting information detected:\n\n" +
                       $"1. Existing: [Memory] {TruncateText(analysis.ConflictingItem!.Content, 100)}\n" +
                       $"2. New: [Memory] {TruncateText(analysis.NewItem.Content, 100)}\n\n" +
                       $"Conflict type: {analysis.Type}\n" +
                       $"Confidence: {analysis.ContradictionConfidence:P0}\n\n" +
                       "Which information should be kept?\n" +
                       "Options: [1] Keep existing, [2] Keep new, [3] Keep both, [4] Merge";

        return new ResolutionResult<MemoryUnit>
        {
            Success = false,
            AppliedStrategy = ResolutionStrategy.AskUser,
            RequiresUserIntervention = true,
            UserQuestion = question,
            Explanation = "User intervention required to resolve contradiction"
        };
    }

    private ResolutionResult<MemoryUnit> KeepBothMemories(ContradictionAnalysis<MemoryUnit> analysis)
    {
        return new ResolutionResult<MemoryUnit>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.KeepBoth,
            KeptItem = analysis.NewItem,
            SupersededItem = null, // Not superseded, both kept
            Explanation = $"Both memories kept with explicit contradiction relation. " +
                          $"Contradiction confidence: {analysis.ContradictionConfidence:P0}"
        };
    }

    #endregion

    #region Triple Resolution Methods

    private async Task<ResolutionResult<EntityTriple>> ResolveTripleByRecencyAsync(
        ContradictionAnalysis<EntityTriple> analysis,
        CancellationToken cancellationToken)
    {
        var newItem = analysis.NewItem;
        var existing = analysis.ConflictingItem!;

        var (kept, superseded) = newItem.CreatedAt >= existing.CreatedAt
            ? (newItem, existing)
            : (existing, newItem);

        // If we have a store, create supersession chain
        if (_temporalEntityStore != null)
        {
            try
            {
                superseded.ValidTo = DateTime.UtcNow;
                superseded.IsActive = false;

                _logger.LogInformation(
                    "Superseded triple {OldId} (v{OldVersion}) with {NewId} using recency",
                    superseded.Id, superseded.Version, kept.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update superseded triple in store");
            }
        }

        return new ResolutionResult<EntityTriple>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.RecencyFirst,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = $"Kept more recent triple (created {kept.CreatedAt:yyyy-MM-dd HH:mm})"
        };
    }

    private ResolutionResult<EntityTriple> ResolveTripleByConfidence(ContradictionAnalysis<EntityTriple> analysis)
    {
        var newItem = analysis.NewItem;
        var existing = analysis.ConflictingItem!;

        var (kept, superseded) = newItem.Confidence >= existing.Confidence
            ? (newItem, existing)
            : (existing, newItem);

        return new ResolutionResult<EntityTriple>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.ConfidenceFirst,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = $"Kept triple with higher confidence ({kept.Confidence:P0} vs {superseded.Confidence:P0})"
        };
    }

    private ResolutionResult<EntityTriple> ResolveTripleBySourceAuthority(ContradictionAnalysis<EntityTriple> analysis)
    {
        var newItem = analysis.NewItem;
        var existing = analysis.ConflictingItem!;

        var newHasSource = !string.IsNullOrEmpty(newItem.Context) || newItem.SourceMemoryId.HasValue;
        var existingHasSource = !string.IsNullOrEmpty(existing.Context) || existing.SourceMemoryId.HasValue;

        EntityTriple kept, superseded;
        string explanation;

        if (newHasSource && !existingHasSource)
        {
            kept = newItem;
            superseded = existing;
            explanation = "Kept triple with source attribution";
        }
        else if (existingHasSource && !newHasSource)
        {
            kept = existing;
            superseded = newItem;
            explanation = "Kept triple with source attribution";
        }
        else
        {
            // Fall back to recency
            (kept, superseded) = newItem.CreatedAt >= existing.CreatedAt
                ? (newItem, existing)
                : (existing, newItem);
            explanation = "Both triples have similar authority - fell back to recency";
        }

        return new ResolutionResult<EntityTriple>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.SourceAuthority,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = explanation
        };
    }

    private ResolutionResult<EntityTriple> CreateTripleUserIntervention(ContradictionAnalysis<EntityTriple> analysis)
    {
        var existing = analysis.ConflictingItem!;
        var newItem = analysis.NewItem;

        var question = $"Conflicting information detected:\n\n" +
                       $"1. Existing: [Fact] {existing.Subject} {existing.Predicate} {existing.ObjectValue}\n" +
                       $"2. New: [Fact] {newItem.Subject} {newItem.Predicate} {newItem.ObjectValue}\n\n" +
                       $"Conflict type: {analysis.Type}\n" +
                       $"Confidence: {analysis.ContradictionConfidence:P0}\n\n" +
                       "Which information should be kept?\n" +
                       "Options: [1] Keep existing, [2] Keep new, [3] Keep both, [4] Merge";

        return new ResolutionResult<EntityTriple>
        {
            Success = false,
            AppliedStrategy = ResolutionStrategy.AskUser,
            RequiresUserIntervention = true,
            UserQuestion = question,
            Explanation = "User intervention required to resolve contradiction"
        };
    }

    private ResolutionResult<EntityTriple> KeepBothTriples(ContradictionAnalysis<EntityTriple> analysis)
    {
        return new ResolutionResult<EntityTriple>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.KeepBoth,
            KeptItem = analysis.NewItem,
            SupersededItem = null, // Not superseded, both kept
            Explanation = $"Both triples kept with explicit contradiction relation. " +
                          $"Contradiction confidence: {analysis.ContradictionConfidence:P0}"
        };
    }

    private ResolutionResult<EntityTriple> ResolveTripleByTemporalPartition(ContradictionAnalysis<EntityTriple> analysis)
    {
        var newTriple = analysis.NewItem;
        var existingTriple = analysis.ConflictingItem!;

        // Set ValidTo on existing triple to when the new one starts
        var newValidFrom = newTriple.ValidFrom ?? DateTime.UtcNow;
        existingTriple.ValidTo = newValidFrom;
        existingTriple.UpdatedAt = DateTime.UtcNow;

        // Ensure new triple has proper ValidFrom
        if (!newTriple.ValidFrom.HasValue)
        {
            newTriple.ValidFrom = DateTime.UtcNow;
        }

        // Create supersession link
        newTriple.SupersedesId = existingTriple.Id;
        newTriple.Version = existingTriple.Version + 1;

        _logger.LogInformation(
            "Temporal partition: {Subject}.{Predicate} = '{OldValue}' until {EndDate}, then '{NewValue}'",
            existingTriple.Subject, existingTriple.Predicate, existingTriple.ObjectValue,
            existingTriple.ValidTo, newTriple.ObjectValue);

        return new ResolutionResult<EntityTriple>
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.TemporalPartition,
            KeptItem = newTriple,
            SupersededItem = existingTriple,
            Explanation = $"Partitioned by time: '{existingTriple.ObjectValue}' valid until {existingTriple.ValidTo:yyyy-MM-dd}, " +
                          $"'{newTriple.ObjectValue}' valid from {newTriple.ValidFrom:yyyy-MM-dd}"
        };
    }

    #endregion

    #region Helper Methods

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    #endregion
}
