using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Profile;

/// <summary>
/// Time-based confidence decay strategy.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
/// <remarks>
/// Uses exponential decay with half-life:
/// decayed = original * (0.5 ^ (days_since_update / half_life_days))
///
/// Factors that slow decay:
/// - Confirmation count (more confirmations = slower decay)
/// - Recent access (accessed recently = slower decay)
/// - Category multiplier (identity facts decay slower)
/// </remarks>
public class TimeBasedDecayStrategy : IConfidenceDecayStrategy
{
    private readonly ConfidenceDecayOptions _options;

    /// <inheritdoc />
    public string Name => "TimeBased";

    public TimeBasedDecayStrategy(IOptions<ConfidenceDecayOptions> options)
    {
        _options = options.Value;
    }

    public TimeBasedDecayStrategy() : this(Options.Create(new ConfidenceDecayOptions()))
    {
    }

    /// <inheritdoc />
    public float CalculateDecayedConfidence(SemanticStoreEntry entry, DateTime asOfDate)
    {
        var originalConfidence = entry.Confidence;
        var lastUpdate = entry.UpdatedAt;
        var lastAccess = entry.LastAccessedAt;

        // Calculate days since last update
        var daysSinceUpdate = Math.Max(0, (asOfDate - lastUpdate).TotalDays);
        if (daysSinceUpdate <= 0)
            return originalConfidence;

        // Get category multiplier (higher = slower decay)
        var categoryMultiplier = GetCategoryMultiplier(entry.Category);

        // Effective half-life = base half-life * category multiplier * confirmation bonus
        var confirmationBonus = 1f + (entry.ConfirmationCount - 1) * (_options.ConfirmationBonus - 1f);
        confirmationBonus = Math.Max(1f, confirmationBonus);

        var effectiveHalfLife = _options.HalfLifeDays * categoryMultiplier * confirmationBonus;

        // Calculate base decay using exponential function
        // decayed = original * 0.5^(days/halfLife)
        var decayFactor = Math.Pow(0.5, daysSinceUpdate / effectiveHalfLife);
        var decayedConfidence = originalConfidence * (float)decayFactor;

        // Apply access bonus if recently accessed
        var daysSinceAccess = (asOfDate - lastAccess).TotalDays;
        if (daysSinceAccess <= _options.RecentAccessDays)
        {
            var accessBoost = 1f + (_options.AccessBonus - 1f) * (float)(1 - daysSinceAccess / _options.RecentAccessDays);
            decayedConfidence *= accessBoost;
        }

        // Apply minimum floor
        decayedConfidence = Math.Max(decayedConfidence, _options.MinimumConfidence);

        // Don't exceed original confidence
        return Math.Min(decayedConfidence, originalConfidence);
    }

    /// <inheritdoc />
    public bool NeedsReconfirmation(SemanticStoreEntry entry, float threshold = 0.5f)
    {
        var currentConfidence = CalculateDecayedConfidence(entry, DateTime.UtcNow);
        return currentConfidence < threshold;
    }

    /// <summary>
    /// Gets all facts that need re-confirmation.
    /// </summary>
    /// <param name="facts">Facts to check.</param>
    /// <param name="threshold">Confidence threshold.</param>
    /// <returns>Facts needing re-confirmation.</returns>
    public IReadOnlyList<StaleFactInfo> GetStaleFacts(
        IReadOnlyList<SemanticStoreEntry> facts,
        float threshold = 0.5f)
    {
        var now = DateTime.UtcNow;
        var staleFacts = new List<StaleFactInfo>();

        foreach (var fact in facts.Where(f => f.IsActive))
        {
            var decayedConfidence = CalculateDecayedConfidence(fact, now);
            if (decayedConfidence < threshold)
            {
                staleFacts.Add(new StaleFactInfo
                {
                    Fact = fact,
                    OriginalConfidence = fact.Confidence,
                    DecayedConfidence = decayedConfidence,
                    DaysSinceUpdate = (int)(now - fact.UpdatedAt).TotalDays,
                    RecommendedAction = decayedConfidence < _options.MinimumConfidence * 2
                        ? StaleFactAction.Archive
                        : StaleFactAction.Reconfirm
                });
            }
        }

        return staleFacts.OrderBy(s => s.DecayedConfidence).ToList();
    }

    /// <summary>
    /// Predicts when a fact will become stale.
    /// </summary>
    /// <param name="entry">The fact entry.</param>
    /// <param name="threshold">Staleness threshold.</param>
    /// <returns>Predicted stale date, or null if already stale or won't become stale.</returns>
    public DateTime? PredictStaleDate(SemanticStoreEntry entry, float threshold = 0.5f)
    {
        if (entry.Confidence <= threshold)
            return null; // Already below threshold

        if (entry.Confidence <= _options.MinimumConfidence)
            return null; // At minimum, won't decay further

        var categoryMultiplier = GetCategoryMultiplier(entry.Category);
        var confirmationBonus = 1f + (entry.ConfirmationCount - 1) * (_options.ConfirmationBonus - 1f);
        var effectiveHalfLife = _options.HalfLifeDays * categoryMultiplier * Math.Max(1f, confirmationBonus);

        // Solve: threshold = confidence * 0.5^(days/halfLife)
        // days = halfLife * log2(confidence/threshold)
        var targetRatio = entry.Confidence / threshold;
        var daysUntilStale = effectiveHalfLife * Math.Log2(targetRatio);

        return entry.UpdatedAt.AddDays(daysUntilStale);
    }

    private float GetCategoryMultiplier(SemanticStoreCategory category)
    {
        // Map store category to fact category for multiplier lookup
        var factCategory = category switch
        {
            SemanticStoreCategory.Fact => FactCategory.General,
            SemanticStoreCategory.Preference => FactCategory.Preference,
            SemanticStoreCategory.Skill => FactCategory.Skill,
            SemanticStoreCategory.Interest => FactCategory.Preference,
            SemanticStoreCategory.Relationship => FactCategory.Relationship,
            SemanticStoreCategory.Work => FactCategory.Professional,
            SemanticStoreCategory.Goal => FactCategory.Goal,
            SemanticStoreCategory.Behavior => FactCategory.General,
            SemanticStoreCategory.Communication => FactCategory.General,
            _ => FactCategory.General
        };

        return _options.CategoryMultipliers.TryGetValue(factCategory, out var multiplier)
            ? multiplier
            : 1.0f;
    }
}

/// <summary>
/// Information about a stale fact.
/// </summary>
public class StaleFactInfo
{
    /// <summary>
    /// The fact entry.
    /// </summary>
    public required SemanticStoreEntry Fact { get; set; }

    /// <summary>
    /// Original confidence when stored.
    /// </summary>
    public float OriginalConfidence { get; set; }

    /// <summary>
    /// Current decayed confidence.
    /// </summary>
    public float DecayedConfidence { get; set; }

    /// <summary>
    /// Days since last update.
    /// </summary>
    public int DaysSinceUpdate { get; set; }

    /// <summary>
    /// Recommended action for this fact.
    /// </summary>
    public StaleFactAction RecommendedAction { get; set; }
}

/// <summary>
/// Recommended action for stale facts.
/// </summary>
public enum StaleFactAction
{
    /// <summary>Request re-confirmation from user.</summary>
    Reconfirm,

    /// <summary>Archive the fact (mark as inactive).</summary>
    Archive,

    /// <summary>Delete the fact entirely.</summary>
    Delete
}
