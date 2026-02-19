using System.ComponentModel;
using MemoryIndexer.Interfaces;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for fact conflict detection and resolution.
/// Phase v0.9.2: Fact Conflict Resolution.
/// </summary>
[McpServerToolType]
public sealed class FactConflictTools(
    IFactValidator factValidator,
    IArchiveStore archiveStore)
{
    private const string DefaultUserId = "default";

    /// <summary>
    /// Validate a fact against existing facts.
    /// Detects conflicts and recommends resolution action.
    /// </summary>
    [McpServerTool]
    [Description("Validate a fact against existing facts. Detects conflicts and recommends resolution action based on category-specific rules.")]
    public async Task<ValidateFactResult> ValidateFact(
        [Description("The fact content to validate")] string content,
        [Description("Fact category: Identity, Preference, Relationship, Location, Temporal, Skill, Goal, Health, Professional, General")] string category,
        [Description("User ID (default: 'default')")] string? userId = null,
        [Description("Subject of the fact (e.g., 'user')")] string? subject = null,
        [Description("Predicate of the fact (e.g., 'name', 'likes')")] string? predicate = null,
        [Description("Object/value of the fact (e.g., 'John', 'pizza')")] string? objectValue = null,
        [Description("Confidence score (0-1, default: 0.9)")] float confidence = 0.9f,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;

        // Parse category
        if (!Enum.TryParse<FactCategory>(category, true, out var factCategory))
        {
            factCategory = FactCategory.General;
        }

        // Create UserFact
        var newFact = new UserFact
        {
            Content = content,
            Category = factCategory,
            Confidence = confidence,
            Subject = subject,
            Predicate = predicate,
            Object = objectValue
        };

        // Get existing facts
        var existingFacts = await archiveStore.GetAllAsync(userId, cancellationToken);

        // Validate
        var analysis = await factValidator.ValidateAsync(newFact, existingFacts, cancellationToken: cancellationToken);

        return new ValidateFactResult
        {
            Success = true,
            IsValid = analysis.IsValid,
            RecommendedAction = analysis.RecommendedAction.ToString(),
            AppliedStrategy = analysis.AppliedStrategy?.ToString(),
            RequiresConfirmation = analysis.RequiresConfirmation,
            ConfirmationQuestion = analysis.ConfirmationQuestion,
            Confidence = analysis.Confidence,
            Reasoning = analysis.Reasoning,
            ConflictCount = analysis.Conflicts.Count,
            Conflicts = analysis.Conflicts.Select(c => new ConflictInfo
            {
                Type = c.ConflictType.ToString(),
                ExistingFactKey = c.ExistingFact?.Key,
                ExistingFactValue = c.ExistingFact?.Value,
                Confidence = c.Confidence,
                SimilarityScore = c.SimilarityScore,
                Description = c.Description
            }).ToList()
        };
    }

    /// <summary>
    /// Get the version history of a fact.
    /// Returns all versions from newest to oldest.
    /// </summary>
    [McpServerTool]
    [Description("Get the version history of a fact. Returns all versions in the supersession chain from newest to oldest.")]
    public async Task<FactHistoryResult> GetFactHistory(
        [Description("The fact key to get history for")] string key,
        [Description("User ID (default: 'default')")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;

        var history = await archiveStore.GetHistoryAsync(userId, key, cancellationToken);

        return new FactHistoryResult
        {
            Success = true,
            UserId = userId,
            Key = key,
            VersionCount = history.Count,
            Versions = history.Select(e => new FactVersionInfo
            {
                Key = e.Key,
                Value = e.Value,
                Version = e.Version,
                IsActive = e.IsActive,
                Confidence = e.Confidence,
                ConfirmationCount = e.ConfirmationCount,
                ValidFrom = e.ValidFrom,
                ValidTo = e.ValidTo,
                SupersedesKey = e.SupersedesKey,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt
            }).ToList()
        };
    }

    /// <summary>
    /// Get facts that were valid at a specific point in time.
    /// </summary>
    [McpServerTool]
    [Description("Get all facts that were valid at a specific point in time (temporal query).")]
    public async Task<TemporalFactsResult> GetFactsValidAt(
        [Description("The date to query (ISO 8601 format, e.g., '2024-01-15')")] string asOfDate,
        [Description("User ID (default: 'default')")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;

        if (!DateTime.TryParse(asOfDate, out var date))
        {
            return new TemporalFactsResult
            {
                Success = false,
                Message = "Invalid date format. Use ISO 8601 (e.g., '2024-01-15')"
            };
        }

        var facts = await archiveStore.GetValidAtAsync(userId, date, cancellationToken);

        return new TemporalFactsResult
        {
            Success = true,
            UserId = userId,
            AsOfDate = date,
            FactCount = facts.Count,
            Facts = facts.Select(e => new TemporalFactInfo
            {
                Key = e.Key,
                Value = e.Value,
                Category = e.Category.ToString(),
                Confidence = e.Confidence,
                ValidFrom = e.ValidFrom,
                ValidTo = e.ValidTo,
                IsActive = e.IsActive
            }).ToList()
        };
    }

    /// <summary>
    /// Archive an existing fact and update with new value.
    /// Creates a versioned history chain.
    /// </summary>
    [McpServerTool]
    [Description("Archive an existing fact and update with a new value. Preserves the old version in a history chain.")]
    public async Task<ArchiveAndUpdateResult> ArchiveAndUpdateFact(
        [Description("The fact key to update")] string key,
        [Description("The new value")] string newValue,
        [Description("User ID (default: 'default')")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;

        var updatedEntry = await archiveStore.ArchiveAndUpdateAsync(userId, key, newValue, cancellationToken);

        if (updatedEntry == null)
        {
            return new ArchiveAndUpdateResult
            {
                Success = false,
                Message = $"Fact with key '{key}' not found"
            };
        }

        return new ArchiveAndUpdateResult
        {
            Success = true,
            Key = updatedEntry.Key,
            NewValue = updatedEntry.Value,
            Version = updatedEntry.Version,
            ArchivedKey = updatedEntry.SupersedesKey,
            Message = $"Archived v{updatedEntry.Version - 1} and created v{updatedEntry.Version}"
        };
    }

    /// <summary>
    /// Get category-specific resolution rules.
    /// </summary>
    [McpServerTool]
    [Description("Get the resolution rules for a specific fact category.")]
    public Task<CategoryRuleResult> GetCategoryRule(
        [Description("Fact category: Identity, Preference, Relationship, Location, Temporal, Skill, Goal, Health, Professional, General")] string category)
    {
        if (!Enum.TryParse<FactCategory>(category, true, out var factCategory))
        {
            return Task.FromResult(new CategoryRuleResult
            {
                Success = false,
                Message = $"Unknown category: {category}"
            });
        }

        var rule = factValidator.GetCategoryRule(factCategory);

        return Task.FromResult(new CategoryRuleResult
        {
            Success = true,
            Category = rule.Category.ToString(),
            DefaultStrategy = rule.DefaultStrategy.ToString(),
            MinConfidenceDifferential = rule.MinConfidenceDifferential,
            RequiresExplicitConfirmation = rule.RequiresExplicitConfirmation,
            PreserveHistory = rule.PreserveHistory,
            Description = rule.Description
        });
    }

    /// <summary>
    /// List all category resolution rules.
    /// </summary>
    [McpServerTool]
    [Description("List all category-specific resolution rules.")]
    public static Task<AllCategoryRulesResult> GetAllCategoryRules()
    {
        var rules = CategoryResolutionRule.Defaults.Values
            .Select(r => new CategoryRuleInfo
            {
                Category = r.Category.ToString(),
                DefaultStrategy = r.DefaultStrategy.ToString(),
                MinConfidenceDifferential = r.MinConfidenceDifferential,
                RequiresExplicitConfirmation = r.RequiresExplicitConfirmation,
                PreserveHistory = r.PreserveHistory,
                Description = r.Description
            })
            .ToList();

        return Task.FromResult(new AllCategoryRulesResult
        {
            Success = true,
            RuleCount = rules.Count,
            Rules = rules
        });
    }
}

#region Result Models

/// <summary>
/// Result of fact validation.
/// </summary>
public sealed class ValidateFactResult
{
    public bool Success { get; init; }
    public bool IsValid { get; init; }
    public string? RecommendedAction { get; init; }
    public string? AppliedStrategy { get; init; }
    public bool RequiresConfirmation { get; init; }
    public string? ConfirmationQuestion { get; init; }
    public float Confidence { get; init; }
    public string? Reasoning { get; init; }
    public int ConflictCount { get; init; }
    public List<ConflictInfo>? Conflicts { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Information about a detected conflict.
/// </summary>
public sealed class ConflictInfo
{
    public string? Type { get; init; }
    public string? ExistingFactKey { get; init; }
    public string? ExistingFactValue { get; init; }
    public float Confidence { get; init; }
    public float? SimilarityScore { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Result of fact history query.
/// </summary>
public sealed class FactHistoryResult
{
    public bool Success { get; init; }
    public string? UserId { get; init; }
    public string? Key { get; init; }
    public int VersionCount { get; init; }
    public List<FactVersionInfo>? Versions { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Information about a fact version.
/// </summary>
public sealed class FactVersionInfo
{
    public string? Key { get; init; }
    public string? Value { get; init; }
    public int Version { get; init; }
    public bool IsActive { get; init; }
    public float Confidence { get; init; }
    public int ConfirmationCount { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public string? SupersedesKey { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Result of temporal facts query.
/// </summary>
public sealed class TemporalFactsResult
{
    public bool Success { get; init; }
    public string? UserId { get; init; }
    public DateTime? AsOfDate { get; init; }
    public int FactCount { get; init; }
    public List<TemporalFactInfo>? Facts { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Information about a temporal fact.
/// </summary>
public sealed class TemporalFactInfo
{
    public string? Key { get; init; }
    public string? Value { get; init; }
    public string? Category { get; init; }
    public float Confidence { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public bool IsActive { get; init; }
}

/// <summary>
/// Result of archive and update operation.
/// </summary>
public sealed class ArchiveAndUpdateResult
{
    public bool Success { get; init; }
    public string? Key { get; init; }
    public string? NewValue { get; init; }
    public int Version { get; init; }
    public string? ArchivedKey { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Result of category rule query.
/// </summary>
public sealed class CategoryRuleResult
{
    public bool Success { get; init; }
    public string? Category { get; init; }
    public string? DefaultStrategy { get; init; }
    public float MinConfidenceDifferential { get; init; }
    public bool RequiresExplicitConfirmation { get; init; }
    public bool PreserveHistory { get; init; }
    public string? Description { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Result of all category rules query.
/// </summary>
public sealed class AllCategoryRulesResult
{
    public bool Success { get; init; }
    public int RuleCount { get; init; }
    public List<CategoryRuleInfo>? Rules { get; init; }
}

/// <summary>
/// Information about a category rule.
/// </summary>
public sealed class CategoryRuleInfo
{
    public string? Category { get; init; }
    public string? DefaultStrategy { get; init; }
    public float MinConfidenceDifferential { get; init; }
    public bool RequiresExplicitConfirmation { get; init; }
    public bool PreserveHistory { get; init; }
    public string? Description { get; init; }
}

#endregion
