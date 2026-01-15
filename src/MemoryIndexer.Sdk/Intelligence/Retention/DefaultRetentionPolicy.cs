using MemoryIndexer.Interfaces;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Intelligence.Retention;

/// <summary>
/// Default retention policy with sensible category-specific rules.
/// Phase v0.11.0: Retention Policy Engine.
/// </summary>
public class DefaultRetentionPolicy : IRetentionPolicy
{
    private readonly IConfidenceDecayStrategy? _decayStrategy;
    private readonly RetentionPolicyOptions _options;
    private readonly Dictionary<SemanticStoreCategory, RetentionRule> _rules;

    /// <summary>
    /// Default retention rules by category.
    /// </summary>
    private static readonly Dictionary<SemanticStoreCategory, RetentionRule> DefaultRules = new()
    {
        // Facts (identity, personal info) - never expire, but can be archived if low confidence
        [SemanticStoreCategory.Fact] = new RetentionRule
        {
            Category = SemanticStoreCategory.Fact,
            MaxAgeDays = null, // Never expires based on age
            MinConfidence = 0.05f, // Very low threshold - identity facts are important
            ProtectedConfirmationCount = 2,
            StaleAfterDays = null, // Access patterns don't matter
            Action = RetentionAction.Archive,
            Description = "Identity facts are retained indefinitely unless explicitly superseded"
        },

        // Preferences - moderate retention, preferences change over time
        [SemanticStoreCategory.Preference] = new RetentionRule
        {
            Category = SemanticStoreCategory.Preference,
            MaxAgeDays = 365, // 1 year
            MinConfidence = 0.2f,
            ProtectedConfirmationCount = 3,
            StaleAfterDays = 180, // 6 months without access
            Action = RetentionAction.Archive,
            Description = "Preferences expire after 1 year or 6 months without access"
        },

        // Skills - longer retention, skills are durable
        [SemanticStoreCategory.Skill] = new RetentionRule
        {
            Category = SemanticStoreCategory.Skill,
            MaxAgeDays = 730, // 2 years
            MinConfidence = 0.15f,
            ProtectedConfirmationCount = 3,
            StaleAfterDays = 365,
            Action = RetentionAction.Archive,
            Description = "Skills retained for 2 years, stale after 1 year without access"
        },

        // Interests - moderate retention
        [SemanticStoreCategory.Interest] = new RetentionRule
        {
            Category = SemanticStoreCategory.Interest,
            MaxAgeDays = 365,
            MinConfidence = 0.2f,
            ProtectedConfirmationCount = 3,
            StaleAfterDays = 180,
            Action = RetentionAction.Archive,
            Description = "Interests expire after 1 year or 6 months without access"
        },

        // Relationships - long retention
        [SemanticStoreCategory.Relationship] = new RetentionRule
        {
            Category = SemanticStoreCategory.Relationship,
            MaxAgeDays = null, // Never expires based on age
            MinConfidence = 0.1f,
            ProtectedConfirmationCount = 2,
            StaleAfterDays = 365,
            Action = RetentionAction.Archive,
            Description = "Relationships retained indefinitely, stale after 1 year without access"
        },

        // Work - moderate retention, jobs change
        [SemanticStoreCategory.Work] = new RetentionRule
        {
            Category = SemanticStoreCategory.Work,
            MaxAgeDays = 730, // 2 years
            MinConfidence = 0.15f,
            ProtectedConfirmationCount = 3,
            StaleAfterDays = 365,
            Action = RetentionAction.Archive,
            Description = "Work info retained for 2 years, stale after 1 year"
        },

        // Goals - shorter retention, goals are achieved or abandoned
        [SemanticStoreCategory.Goal] = new RetentionRule
        {
            Category = SemanticStoreCategory.Goal,
            MaxAgeDays = 180, // 6 months
            MinConfidence = 0.3f,
            ProtectedConfirmationCount = 2,
            StaleAfterDays = 90, // 3 months
            Action = RetentionAction.Archive,
            Description = "Goals expire after 6 months or 3 months without access"
        },

        // Behavior - moderate retention
        [SemanticStoreCategory.Behavior] = new RetentionRule
        {
            Category = SemanticStoreCategory.Behavior,
            MaxAgeDays = 365,
            MinConfidence = 0.2f,
            ProtectedConfirmationCount = 5, // Need more confirmations for behavioral patterns
            StaleAfterDays = 180,
            Action = RetentionAction.Archive,
            Description = "Behavioral patterns expire after 1 year"
        },

        // Communication - long retention, communication style is persistent
        [SemanticStoreCategory.Communication] = new RetentionRule
        {
            Category = SemanticStoreCategory.Communication,
            MaxAgeDays = null,
            MinConfidence = 0.15f,
            ProtectedConfirmationCount = 3,
            StaleAfterDays = 365,
            Action = RetentionAction.Archive,
            Description = "Communication style retained indefinitely"
        },

        // Other - default moderate retention
        [SemanticStoreCategory.Other] = new RetentionRule
        {
            Category = SemanticStoreCategory.Other,
            MaxAgeDays = 365,
            MinConfidence = 0.2f,
            ProtectedConfirmationCount = 3,
            StaleAfterDays = 180,
            Action = RetentionAction.Archive,
            Description = "Default retention: 1 year or 6 months without access"
        }
    };

    public DefaultRetentionPolicy(
        IOptions<RetentionPolicyOptions>? options = null,
        IConfidenceDecayStrategy? decayStrategy = null)
    {
        _decayStrategy = decayStrategy;
        _options = options?.Value ?? new RetentionPolicyOptions();

        // Merge default rules with custom rules from options
        _rules = new Dictionary<SemanticStoreCategory, RetentionRule>(DefaultRules);

        foreach (var (category, rule) in _options.CategoryRules)
        {
            _rules[category] = rule;
        }
    }

    /// <inheritdoc />
    public RetentionRule GetRule(SemanticStoreCategory category)
    {
        if (_rules.TryGetValue(category, out var rule))
        {
            return rule;
        }

        // Return default rule for unknown categories
        return new RetentionRule
        {
            Category = category,
            MaxAgeDays = _options.DefaultMaxAgeDays,
            MinConfidence = _options.DefaultMinConfidence,
            StaleAfterDays = _options.DefaultStaleAfterDays,
            Action = RetentionAction.Archive,
            Description = "Default retention rule"
        };
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<SemanticStoreCategory, RetentionRule> GetAllRules()
    {
        return _rules;
    }

    /// <inheritdoc />
    public RetentionDecision Evaluate(SemanticStoreEntry entry, DateTime? referenceTime = null)
    {
        var now = referenceTime ?? DateTime.UtcNow;
        var rule = GetRule(entry.Category);

        // Already archived entries
        if (!entry.IsActive)
        {
            return new RetentionDecision
            {
                Entry = entry,
                ShouldRetain = true,
                Action = RetentionAction.Keep,
                Reason = RetentionReason.AlreadyArchived,
                AppliedRule = rule
            };
        }

        // Check if protected by confirmations
        if (entry.ConfirmationCount >= rule.ProtectedConfirmationCount)
        {
            return new RetentionDecision
            {
                Entry = entry,
                ShouldRetain = true,
                Action = RetentionAction.Keep,
                Reason = RetentionReason.ProtectedByConfirmations,
                AppliedRule = rule
            };
        }

        // Check for infinite retention (no max age)
        if (rule.MaxAgeDays == null && rule.StaleAfterDays == null)
        {
            // Only check confidence for infinite retention categories
            var decayedConfidence = GetDecayedConfidence(entry, now);

            if (decayedConfidence >= rule.MinConfidence)
            {
                return new RetentionDecision
                {
                    Entry = entry,
                    ShouldRetain = true,
                    Action = RetentionAction.Keep,
                    Reason = RetentionReason.NeverExpires,
                    AppliedRule = rule,
                    DecayedConfidence = decayedConfidence
                };
            }

            // Low confidence even for infinite retention
            return new RetentionDecision
            {
                Entry = entry,
                ShouldRetain = false,
                Action = rule.Action,
                Reason = RetentionReason.LowConfidence,
                AppliedRule = rule,
                DecayedConfidence = decayedConfidence
            };
        }

        // Check age-based expiration
        if (rule.MaxAgeDays.HasValue)
        {
            var age = (now - entry.CreatedAt).TotalDays;
            if (age > rule.MaxAgeDays.Value)
            {
                return new RetentionDecision
                {
                    Entry = entry,
                    ShouldRetain = false,
                    Action = rule.Action,
                    Reason = RetentionReason.AgeExceeded,
                    AppliedRule = rule
                };
            }
        }

        // Check stale based on last access
        if (rule.StaleAfterDays.HasValue)
        {
            var daysSinceAccess = (now - entry.LastAccessedAt).TotalDays;
            if (daysSinceAccess > rule.StaleAfterDays.Value)
            {
                return new RetentionDecision
                {
                    Entry = entry,
                    ShouldRetain = false,
                    Action = rule.Action,
                    Reason = RetentionReason.Stale,
                    AppliedRule = rule
                };
            }
        }

        // Check confidence threshold
        var confidence = GetDecayedConfidence(entry, now);
        if (confidence < rule.MinConfidence)
        {
            return new RetentionDecision
            {
                Entry = entry,
                ShouldRetain = false,
                Action = rule.Action,
                Reason = RetentionReason.LowConfidence,
                AppliedRule = rule,
                DecayedConfidence = confidence
            };
        }

        // Calculate days until eligible for cleanup
        int? daysUntilEligible = null;
        if (rule.MaxAgeDays.HasValue)
        {
            var age = (now - entry.CreatedAt).TotalDays;
            daysUntilEligible = (int)(rule.MaxAgeDays.Value - age);
        }

        return new RetentionDecision
        {
            Entry = entry,
            ShouldRetain = true,
            Action = RetentionAction.Keep,
            Reason = RetentionReason.WithinPolicy,
            AppliedRule = rule,
            DaysUntilEligible = daysUntilEligible > 0 ? daysUntilEligible : null,
            DecayedConfidence = confidence
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<RetentionDecision> EvaluateAll(
        IEnumerable<SemanticStoreEntry> entries,
        DateTime? referenceTime = null)
    {
        return entries.Select(e => Evaluate(e, referenceTime)).ToList();
    }

    private float GetDecayedConfidence(SemanticStoreEntry entry, DateTime referenceTime)
    {
        if (_options.UseConfidenceDecay && _decayStrategy != null)
        {
            return _decayStrategy.CalculateDecayedConfidence(entry, referenceTime);
        }

        return entry.Confidence;
    }
}
