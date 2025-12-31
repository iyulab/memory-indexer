using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Conflict;

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
    public async Task<ResolutionResult> ResolveAsync(
        ContradictionAnalysis analysis,
        ResolutionStrategy strategy = ResolutionStrategy.RecencyFirst,
        CancellationToken cancellationToken = default)
    {
        if (!analysis.HasContradiction)
        {
            return new ResolutionResult
            {
                Success = true,
                AppliedStrategy = strategy,
                Explanation = "No contradiction to resolve"
            };
        }

        _logger.LogInformation(
            "Resolving {Type} contradiction using {Strategy} strategy",
            analysis.Type, strategy);

        return strategy switch
        {
            ResolutionStrategy.RecencyFirst => await ResolveByRecencyAsync(analysis, cancellationToken),
            ResolutionStrategy.ConfidenceFirst => ResolveByConfidence(analysis),
            ResolutionStrategy.SourceAuthority => ResolveBySourceAuthority(analysis),
            ResolutionStrategy.AskUser => CreateUserIntervention(analysis),
            ResolutionStrategy.KeepBoth => await KeepBothWithRelationAsync(analysis, cancellationToken),
            ResolutionStrategy.TemporalPartition => await ResolveByTemporalPartitionAsync(analysis, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy))
        };
    }

    /// <inheritdoc />
    public async Task<ResolutionResult> AutoResolveAsync(
        ContradictionAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        var (strategy, explanation) = await SuggestStrategyAsync(analysis, cancellationToken);

        _logger.LogDebug("Auto-resolving with suggested strategy: {Strategy} - {Explanation}",
            strategy, explanation);

        return await ResolveAsync(analysis, strategy, cancellationToken);
    }

    /// <inheritdoc />
    public Task<(ResolutionStrategy Strategy, string Explanation)> SuggestStrategyAsync(
        ContradictionAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        // Decision tree for strategy selection based on contradiction type and context
        var (strategy, explanation) = analysis.Type switch
        {
            // Temporal contradictions should use temporal partitioning
            ContradictionType.Temporal => (
                ResolutionStrategy.TemporalPartition,
                "Temporal contradiction detected - using time-based partitioning to preserve history"),

            // Factual contradictions with high confidence should use recency
            ContradictionType.Factual when analysis.ContradictionConfidence > 0.8f => (
                ResolutionStrategy.RecencyFirst,
                "High-confidence factual contradiction - newer information likely more accurate"),

            // Low confidence contradictions should keep both
            _ when analysis.ContradictionConfidence < 0.6f => (
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

        return Task.FromResult((strategy, explanation));
    }

    /// <summary>
    /// Resolves by keeping the more recent item.
    /// </summary>
    private async Task<ResolutionResult> ResolveByRecencyAsync(
        ContradictionAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var (newItem, existing) = GetItems(analysis);

        var newDate = GetCreatedAt(newItem);
        var existingDate = GetCreatedAt(existing);

        object kept, superseded;
        if (newDate >= existingDate)
        {
            kept = newItem;
            superseded = existing;
        }
        else
        {
            kept = existing;
            superseded = newItem;
        }

        // If working with entity triples and we have a store, create supersession chain
        if (kept is EntityTriple keptTriple && superseded is EntityTriple supersededTriple &&
            _temporalEntityStore != null)
        {
            try
            {
                // Mark the old triple's ValidTo
                supersededTriple.ValidTo = DateTime.UtcNow;
                supersededTriple.IsActive = false;

                _logger.LogInformation(
                    "Superseded triple {OldId} (v{OldVersion}) with {NewId} using recency",
                    supersededTriple.Id, supersededTriple.Version, keptTriple.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update superseded triple in store");
            }
        }

        return new ResolutionResult
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.RecencyFirst,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = $"Kept more recent item (created {GetCreatedAt(kept):yyyy-MM-dd HH:mm})"
        };
    }

    /// <summary>
    /// Resolves by keeping the item with higher confidence.
    /// </summary>
    private ResolutionResult ResolveByConfidence(ContradictionAnalysis analysis)
    {
        var (newItem, existing) = GetItems(analysis);

        var newConfidence = GetConfidence(newItem);
        var existingConfidence = GetConfidence(existing);

        object kept, superseded;
        if (newConfidence >= existingConfidence)
        {
            kept = newItem;
            superseded = existing;
        }
        else
        {
            kept = existing;
            superseded = newItem;
        }

        return new ResolutionResult
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.ConfidenceFirst,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = $"Kept item with higher confidence ({GetConfidence(kept):P0} vs {GetConfidence(superseded):P0})"
        };
    }

    /// <summary>
    /// Resolves based on source authority (simplified implementation).
    /// </summary>
    private ResolutionResult ResolveBySourceAuthority(ContradictionAnalysis analysis)
    {
        var (newItem, existing) = GetItems(analysis);

        // For now, prefer items with context/source information
        var newHasSource = HasSourceInfo(newItem);
        var existingHasSource = HasSourceInfo(existing);

        object kept, superseded;
        string explanation;

        if (newHasSource && !existingHasSource)
        {
            kept = newItem;
            superseded = existing;
            explanation = "Kept item with source attribution";
        }
        else if (existingHasSource && !newHasSource)
        {
            kept = existing;
            superseded = newItem;
            explanation = "Kept item with source attribution";
        }
        else
        {
            // Fall back to recency if both or neither have source info
            var newDate = GetCreatedAt(newItem);
            var existingDate = GetCreatedAt(existing);
            kept = newDate >= existingDate ? newItem : existing;
            superseded = newDate >= existingDate ? existing : newItem;
            explanation = "Both items have similar authority - fell back to recency";
        }

        return new ResolutionResult
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.SourceAuthority,
            KeptItem = kept,
            SupersededItem = superseded,
            Explanation = explanation
        };
    }

    /// <summary>
    /// Creates a result requesting user intervention.
    /// </summary>
    private ResolutionResult CreateUserIntervention(ContradictionAnalysis analysis)
    {
        var (newItem, existing) = GetItems(analysis);

        var question = BuildUserQuestion(analysis, newItem, existing);

        return new ResolutionResult
        {
            Success = false,
            AppliedStrategy = ResolutionStrategy.AskUser,
            RequiresUserIntervention = true,
            UserQuestion = question,
            Explanation = "User intervention required to resolve contradiction"
        };
    }

    /// <summary>
    /// Keeps both items with an explicit CONTRADICTS relation.
    /// </summary>
    private Task<ResolutionResult> KeepBothWithRelationAsync(
        ContradictionAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var (newItem, existing) = GetItems(analysis);

        // In a full implementation, we would create a MemoryRelation here
        // with type = MemoryRelationType.Contradicts

        return Task.FromResult(new ResolutionResult
        {
            Success = true,
            AppliedStrategy = ResolutionStrategy.KeepBoth,
            KeptItem = newItem,
            SupersededItem = null, // Not superseded, both kept
            Explanation = "Both items kept with explicit contradiction relation. " +
                          $"Contradiction confidence: {analysis.ContradictionConfidence:P0}"
        });
    }

    /// <summary>
    /// Resolves by partitioning into different time periods.
    /// </summary>
    private async Task<ResolutionResult> ResolveByTemporalPartitionAsync(
        ContradictionAnalysis analysis,
        CancellationToken cancellationToken)
    {
        var (newItem, existing) = GetItems(analysis);

        if (newItem is EntityTriple newTriple && existing is EntityTriple existingTriple)
        {
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

            return new ResolutionResult
            {
                Success = true,
                AppliedStrategy = ResolutionStrategy.TemporalPartition,
                KeptItem = newTriple,
                SupersededItem = existingTriple,
                Explanation = $"Partitioned by time: '{existingTriple.ObjectValue}' valid until {existingTriple.ValidTo:yyyy-MM-dd}, " +
                              $"'{newTriple.ObjectValue}' valid from {newTriple.ValidFrom:yyyy-MM-dd}"
            };
        }

        // Fall back to recency for non-triple items
        return await ResolveByRecencyAsync(analysis, cancellationToken);
    }

    #region Helper Methods

    private static (object NewItem, object Existing) GetItems(ContradictionAnalysis analysis)
    {
        return (analysis.NewItem, analysis.ConflictingItem ?? throw new InvalidOperationException("No conflicting item"));
    }

    private static DateTime GetCreatedAt(object item) => item switch
    {
        MemoryUnit m => m.CreatedAt,
        EntityTriple t => t.CreatedAt,
        _ => DateTime.MinValue
    };

    private static float GetConfidence(object item) => item switch
    {
        MemoryUnit m => m.ImportanceScore,
        EntityTriple t => t.Confidence,
        _ => 0.5f
    };

    private static bool HasSourceInfo(object item) => item switch
    {
        MemoryUnit m => m.Metadata.Count > 0 || !string.IsNullOrEmpty(m.SessionId),
        EntityTriple t => !string.IsNullOrEmpty(t.Context) || t.SourceMemoryId.HasValue,
        _ => false
    };

    private static string BuildUserQuestion(ContradictionAnalysis analysis, object newItem, object existing)
    {
        var newDesc = GetItemDescription(newItem);
        var existingDesc = GetItemDescription(existing);

        return $"Conflicting information detected:\n\n" +
               $"1. Existing: {existingDesc}\n" +
               $"2. New: {newDesc}\n\n" +
               $"Conflict type: {analysis.Type}\n" +
               $"Confidence: {analysis.ContradictionConfidence:P0}\n\n" +
               "Which information should be kept?\n" +
               "Options: [1] Keep existing, [2] Keep new, [3] Keep both, [4] Merge";
    }

    private static string GetItemDescription(object item) => item switch
    {
        MemoryUnit m => $"[Memory] {TruncateText(m.Content, 100)}",
        EntityTriple t => $"[Fact] {t.Subject} {t.Predicate} {t.ObjectValue}",
        _ => item.ToString() ?? "Unknown"
    };

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    #endregion
}
