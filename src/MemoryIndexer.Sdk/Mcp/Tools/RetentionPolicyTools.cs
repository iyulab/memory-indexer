using System.ComponentModel;
using MemoryIndexer.Interfaces;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for retention policy management.
/// Phase v0.11.0: Retention Policy Engine.
/// </summary>
[McpServerToolType]
public class RetentionPolicyTools(IRetentionPolicyService retentionService)
{
    private const string DefaultUserId = "mcp-user";

    /// <summary>
    /// Preview what would be cleaned up by the retention policy.
    /// </summary>
    [McpServerTool]
    [Description("Preview what would be cleaned up by the retention policy without actually deleting anything.")]
    public async Task<RetentionPreviewResult> PreviewCleanup(
        [Description("User ID to preview cleanup for (default: mcp-user)")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? DefaultUserId;

        try
        {
            var preview = await retentionService.PreviewCleanupAsync(targetUserId, cancellationToken);

            return new RetentionPreviewResult
            {
                Success = true,
                UserId = preview.UserId,
                TotalEntries = preview.TotalEntries,
                RetainCount = preview.RetainCount,
                ArchiveCount = preview.ArchiveCount,
                DeleteCount = preview.DeleteCount,
                ByCategory = preview.ByCategory
                    .Select(kv => new CategoryPreviewResult
                    {
                        Category = kv.Key.ToString(),
                        TotalCount = kv.Value.TotalCount,
                        RetainCount = kv.Value.RetainCount,
                        CleanupCount = kv.Value.CleanupCount
                    })
                    .ToList(),
                CleanupItems = preview.CleanupDecisions
                    .Take(50) // Limit to avoid too large responses
                    .Select(d => new CleanupItemResult
                    {
                        Key = d.Entry.Key,
                        Category = d.Entry.Category.ToString(),
                        Action = d.Action.ToString(),
                        Reason = d.Reason.ToString(),
                        Confidence = d.DecayedConfidence ?? d.Entry.Confidence
                    })
                    .ToList(),
                Message = $"Preview complete: {preview.RetainCount} to retain, {preview.ArchiveCount} to archive, {preview.DeleteCount} to delete"
            };
        }
        catch (Exception ex)
        {
            return new RetentionPreviewResult
            {
                Success = false,
                Message = $"Preview failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Apply retention policy to clean up old or low-confidence entries.
    /// </summary>
    [McpServerTool]
    [Description("Apply retention policy to archive or delete old/low-confidence entries. Use dryRun=true to preview without changes.")]
    public async Task<RetentionApplyResult> ApplyRetentionPolicy(
        [Description("User ID to apply policy to (default: mcp-user)")] string? userId = null,
        [Description("If true, only report what would be done without making changes")] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? DefaultUserId;

        try
        {
            var result = await retentionService.ApplyAsync(targetUserId, dryRun, cancellationToken);

            return new RetentionApplyResult
            {
                Success = result.Success,
                UserId = targetUserId,
                DryRun = dryRun,
                TotalProcessed = result.TotalProcessed,
                RetainedCount = result.RetainedCount,
                ArchivedCount = result.ArchivedCount,
                DeletedCount = result.DeletedCount,
                ProcessingTimeMs = result.ProcessingTimeMs,
                Message = dryRun
                    ? $"Dry run complete: would retain {result.RetainedCount}, archive {result.ArchivedCount}, delete {result.DeletedCount}"
                    : $"Policy applied: retained {result.RetainedCount}, archived {result.ArchivedCount}, deleted {result.DeletedCount}",
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new RetentionApplyResult
            {
                Success = false,
                UserId = targetUserId,
                DryRun = dryRun,
                Message = $"Failed to apply retention policy: {ex.Message}",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Get retention rules for all categories.
    /// </summary>
    [McpServerTool]
    [Description("Get retention rules showing how long each category of memory is kept.")]
    public Task<RetentionRulesResult> GetRetentionRules()
    {
        var rules = retentionService.Policy.GetAllRules();

        return Task.FromResult(new RetentionRulesResult
        {
            Success = true,
            RuleCount = rules.Count,
            Rules = rules.Values
                .Select(r => new RetentionRuleResult
                {
                    Category = r.Category.ToString(),
                    MaxAgeDays = r.MaxAgeDays,
                    MinConfidence = r.MinConfidence,
                    ProtectedConfirmationCount = r.ProtectedConfirmationCount,
                    StaleAfterDays = r.StaleAfterDays,
                    Action = r.Action.ToString(),
                    Description = r.Description
                })
                .ToList(),
            Message = $"Retrieved {rules.Count} retention rules"
        });
    }

    /// <summary>
    /// Get retention rule for a specific category.
    /// </summary>
    [McpServerTool]
    [Description("Get the retention rule for a specific memory category.")]
    public Task<RetentionRuleDetailResult> GetRetentionRule(
        [Description("Category: Fact, Preference, Skill, Interest, Relationship, Work, Goal, Behavior, Communication, Other")] string category)
    {
        if (!Enum.TryParse<SemanticStoreCategory>(category, ignoreCase: true, out var categoryEnum))
        {
            return Task.FromResult(new RetentionRuleDetailResult
            {
                Success = false,
                Message = $"Invalid category: {category}. Valid values: Fact, Preference, Skill, Interest, Relationship, Work, Goal, Behavior, Communication, Other"
            });
        }

        var rule = retentionService.Policy.GetRule(categoryEnum);

        return Task.FromResult(new RetentionRuleDetailResult
        {
            Success = true,
            Category = rule.Category.ToString(),
            MaxAgeDays = rule.MaxAgeDays,
            MaxAgeDaysDescription = rule.MaxAgeDays.HasValue ? $"{rule.MaxAgeDays} days" : "Never expires",
            MinConfidence = rule.MinConfidence,
            ProtectedConfirmationCount = rule.ProtectedConfirmationCount,
            StaleAfterDays = rule.StaleAfterDays,
            StaleAfterDaysDescription = rule.StaleAfterDays.HasValue ? $"{rule.StaleAfterDays} days without access" : "Access pattern not considered",
            Action = rule.Action.ToString(),
            Description = rule.Description,
            Message = $"Retrieved rule for {category}"
        });
    }

    /// <summary>
    /// Evaluate retention decision for a specific entry.
    /// </summary>
    [McpServerTool]
    [Description("Evaluate the retention decision for a specific entry by key.")]
    public async Task<RetentionEvaluateResult> EvaluateRetention(
        [Description("The key of the entry to evaluate")] string key,
        [Description("User ID (default: mcp-user)")] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? DefaultUserId;

        // Get the archive store to retrieve the entry
        // Note: This is a workaround since we only have IRetentionPolicyService
        // In a real implementation, we'd inject IArchiveStore directly
        var preview = await retentionService.PreviewCleanupAsync(targetUserId, cancellationToken);

        // Find the decision for this key
        var decision = preview.CleanupDecisions
            .FirstOrDefault(d => d.Entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (decision == null)
        {
            // Entry might be retained, check all entries would require full access
            return new RetentionEvaluateResult
            {
                Success = true,
                Key = key,
                ShouldRetain = true,
                Reason = "WithinPolicy",
                Message = $"Entry '{key}' is within retention policy and will be retained (or not found)"
            };
        }

        return new RetentionEvaluateResult
        {
            Success = true,
            Key = key,
            Category = decision.Entry.Category.ToString(),
            ShouldRetain = decision.ShouldRetain,
            Action = decision.Action.ToString(),
            Reason = decision.Reason.ToString(),
            OriginalConfidence = decision.Entry.Confidence,
            DecayedConfidence = decision.DecayedConfidence,
            DaysUntilEligible = decision.DaysUntilEligible,
            CreatedAt = decision.Entry.CreatedAt.ToString("O"),
            LastAccessedAt = decision.Entry.LastAccessedAt.ToString("O"),
            Message = decision.ShouldRetain
                ? $"Entry '{key}' will be retained: {decision.Reason}"
                : $"Entry '{key}' is eligible for {decision.Action}: {decision.Reason}"
        };
    }
}

#region Result Types

/// <summary>
/// Result of retention preview operation.
/// </summary>
public class RetentionPreviewResult
{
    public bool Success { get; init; }
    public string? UserId { get; init; }
    public int TotalEntries { get; init; }
    public int RetainCount { get; init; }
    public int ArchiveCount { get; init; }
    public int DeleteCount { get; init; }
    public List<CategoryPreviewResult>? ByCategory { get; init; }
    public List<CleanupItemResult>? CleanupItems { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Category preview in retention result.
/// </summary>
public class CategoryPreviewResult
{
    public string? Category { get; init; }
    public int TotalCount { get; init; }
    public int RetainCount { get; init; }
    public int CleanupCount { get; init; }
}

/// <summary>
/// Individual cleanup item result.
/// </summary>
public class CleanupItemResult
{
    public string? Key { get; init; }
    public string? Category { get; init; }
    public string? Action { get; init; }
    public string? Reason { get; init; }
    public float Confidence { get; init; }
}

/// <summary>
/// Result of applying retention policy.
/// </summary>
public class RetentionApplyResult
{
    public bool Success { get; init; }
    public string? UserId { get; init; }
    public bool DryRun { get; init; }
    public int TotalProcessed { get; init; }
    public int RetainedCount { get; init; }
    public int ArchivedCount { get; init; }
    public int DeletedCount { get; init; }
    public long ProcessingTimeMs { get; init; }
    public string? Message { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of getting all retention rules.
/// </summary>
public class RetentionRulesResult
{
    public bool Success { get; init; }
    public int RuleCount { get; init; }
    public List<RetentionRuleResult>? Rules { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Single retention rule in list result.
/// </summary>
public class RetentionRuleResult
{
    public string? Category { get; init; }
    public int? MaxAgeDays { get; init; }
    public float MinConfidence { get; init; }
    public int ProtectedConfirmationCount { get; init; }
    public int? StaleAfterDays { get; init; }
    public string? Action { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Detailed result for single retention rule.
/// </summary>
public class RetentionRuleDetailResult
{
    public bool Success { get; init; }
    public string? Category { get; init; }
    public int? MaxAgeDays { get; init; }
    public string? MaxAgeDaysDescription { get; init; }
    public float MinConfidence { get; init; }
    public int ProtectedConfirmationCount { get; init; }
    public int? StaleAfterDays { get; init; }
    public string? StaleAfterDaysDescription { get; init; }
    public string? Action { get; init; }
    public string? Description { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Result of evaluating retention for a single entry.
/// </summary>
public class RetentionEvaluateResult
{
    public bool Success { get; init; }
    public string? Key { get; init; }
    public string? Category { get; init; }
    public bool ShouldRetain { get; init; }
    public string? Action { get; init; }
    public string? Reason { get; init; }
    public float? OriginalConfidence { get; init; }
    public float? DecayedConfidence { get; init; }
    public int? DaysUntilEligible { get; init; }
    public string? CreatedAt { get; init; }
    public string? LastAccessedAt { get; init; }
    public string? Message { get; init; }
}

#endregion
