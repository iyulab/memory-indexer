using System.ComponentModel;
using MemoryIndexer.Interfaces;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for AI-based fact extraction and user profile management.
/// Phase v0.9.1: Intelligent Fact Extraction.
/// </summary>
[McpServerToolType]
public sealed class FactExtractionTools(IFastTrackPromoter fastTrackPromoter)
{
    private const string DefaultUserId = "default";

    /// <summary>
    /// Extract facts from conversational content.
    /// Identifies user facts (name, preferences, etc.) and determines promotion path.
    /// </summary>
    [McpServerTool]
    [Description("Extract facts from conversational content. Identifies user facts like name, preferences, and determines whether they should be fast-tracked to long-term memory.")]
    public async Task<FactExtractionToolResult> ExtractFacts(
        [Description("The content to analyze for facts")] string content,
        [Description("User ID (default: 'default')")] string? userId = null,
        [Description("Session ID for context")] string? sessionId = null,
        [Description("Speaker role: user, assistant, system")] string? role = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;

        var context = new FactExtractionContext
        {
            Content = content,
            UserId = userId,
            SessionId = sessionId,
            Role = role
        };

        var result = await fastTrackPromoter.ProcessAsync(context, cancellationToken);

        return new FactExtractionToolResult
        {
            Success = result.Success,
            ContextType = result.ContextType.ToString(),
            TotalExtracted = result.ExtractedFacts.Count,
            FastTracked = result.FastTrackedFacts.Count,
            StandardPath = result.StandardPathFacts.Count,
            Skipped = result.SkippedFacts.Count,
            Facts = result.ExtractedFacts.Select(f => new ExtractedFactInfo
            {
                Content = f.Content,
                Category = f.Category.ToString(),
                Confidence = f.Confidence,
                Importance = f.Importance,
                PromotionPath = f.PromotionPath.ToString(),
                Subject = f.Subject,
                Predicate = f.Predicate,
                Object = f.Object
            }).ToList(),
            SkippedReasons = result.SkippedFacts.Select(s => new SkippedFactInfo
            {
                Content = s.Fact.Content,
                Reason = s.Reason.ToString(),
                Details = s.Details
            }).ToList(),
            Message = result.Error
        };
    }

    /// <summary>
    /// Get user profile with extracted facts.
    /// Retrieves all facts stored for a user from the Archive.
    /// </summary>
    [McpServerTool]
    [Description("Get user profile with all extracted facts. Retrieves persistent facts about the user from long-term memory.")]
    public async Task<UserProfileToolResult> GetUserProfile(
        [Description("User ID (default: 'default')")] string? userId = null,
        [Description("Filter by category: Identity, Preference, Relationship, Location, Skill, Goal, Health, Professional, General")] string? category = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= DefaultUserId;

        FactCategory? categoryFilter = null;
        if (!string.IsNullOrEmpty(category) &&
            Enum.TryParse<FactCategory>(category, true, out var parsed))
        {
            categoryFilter = parsed;
        }

        var profile = await fastTrackPromoter.GetUserProfileAsync(userId, categoryFilter, cancellationToken);

        return new UserProfileToolResult
        {
            Success = true,
            UserId = profile.UserId,
            TotalFacts = profile.TotalFacts,
            ConfirmedFacts = profile.ConfirmedFacts,
            LastUpdatedAt = profile.LastUpdatedAt,
            Facts = profile.Facts.Select(f => new UserFactInfo
            {
                Key = f.Key,
                Content = f.Content,
                Category = f.Category.ToString(),
                Confidence = f.Confidence,
                ConfirmationCount = f.ConfirmationCount,
                IsConfirmed = f.IsConfirmed,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
                Subject = f.Subject,
                Predicate = f.Predicate,
                Object = f.Object
            }).ToList(),
            FactsByCategory = profile.FactsByCategory.ToDictionary(
                kvp => kvp.Key.ToString(),
                kvp => kvp.Value.Count)
        };
    }

    /// <summary>
    /// Get available fact categories.
    /// </summary>
    [McpServerTool]
    [Description("List available fact categories for filtering and organization.")]
    public static Task<FactCategoriesResult> GetFactCategories()
    {
        var categories = new List<FactCategoryInfo>
        {
            new() { Name = "Identity", Description = "Identity facts (name, age, occupation)" },
            new() { Name = "Preference", Description = "Preference facts (likes, dislikes, favorites)" },
            new() { Name = "Relationship", Description = "Relationship facts (family, friends, colleagues)" },
            new() { Name = "Location", Description = "Location facts (address, workplace, birthplace)" },
            new() { Name = "Temporal", Description = "Temporal facts (birthday, anniversary, schedule)" },
            new() { Name = "Skill", Description = "Skill facts (languages, expertise, abilities)" },
            new() { Name = "Goal", Description = "Goal facts (plans, aspirations, intentions)" },
            new() { Name = "Health", Description = "Health facts (allergies, conditions, medications)" },
            new() { Name = "Professional", Description = "Professional facts (job, company, role)" },
            new() { Name = "General", Description = "General uncategorized facts" }
        };

        return Task.FromResult(new FactCategoriesResult
        {
            Success = true,
            Categories = categories
        });
    }

    /// <summary>
    /// Get promotion path information.
    /// </summary>
    [McpServerTool]
    [Description("List available promotion paths and their criteria.")]
    public static Task<PromotionPathsResult> GetPromotionPaths()
    {
        var paths = new List<PromotionPathInfo>
        {
            new()
            {
                Name = "FastTrack",
                Description = "Direct promotion to Archive (Buffer → Archive)",
                ConfidenceThreshold = 0.9f,
                Criteria = "High-confidence direct statements (e.g., 'My name is John')"
            },
            new()
            {
                Name = "Standard",
                Description = "Standard tier progression (Buffer → Short → Long → Archive)",
                ConfidenceThreshold = 0.5f,
                Criteria = "Moderate confidence facts requiring confirmation"
            },
            new()
            {
                Name = "SessionOnly",
                Description = "Session-scoped, not promoted to user level",
                ConfidenceThreshold = 0f,
                Criteria = "Quoted, hypothetical, or context-dependent statements"
            },
            new()
            {
                Name = "Discard",
                Description = "Not stored as fact",
                ConfidenceThreshold = 0f,
                Criteria = "Questions, fictional content, irrelevant statements"
            }
        };

        return Task.FromResult(new PromotionPathsResult
        {
            Success = true,
            Paths = paths
        });
    }
}

#region Result Models

/// <summary>
/// Result of fact extraction operation.
/// </summary>
public sealed class FactExtractionToolResult
{
    public bool Success { get; init; }
    public string? ContextType { get; init; }
    public int TotalExtracted { get; init; }
    public int FastTracked { get; init; }
    public int StandardPath { get; init; }
    public int Skipped { get; init; }
    public List<ExtractedFactInfo>? Facts { get; init; }
    public List<SkippedFactInfo>? SkippedReasons { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Information about an extracted fact.
/// </summary>
public sealed class ExtractedFactInfo
{
    public string? Content { get; init; }
    public string? Category { get; init; }
    public float Confidence { get; init; }
    public float Importance { get; init; }
    public string? PromotionPath { get; init; }
    public string? Subject { get; init; }
    public string? Predicate { get; init; }
    public string? Object { get; init; }
}

/// <summary>
/// Information about a skipped fact.
/// </summary>
public sealed class SkippedFactInfo
{
    public string? Content { get; init; }
    public string? Reason { get; init; }
    public string? Details { get; init; }
}

/// <summary>
/// Result of user profile retrieval.
/// </summary>
public sealed class UserProfileToolResult
{
    public bool Success { get; init; }
    public string? UserId { get; init; }
    public int TotalFacts { get; init; }
    public int ConfirmedFacts { get; init; }
    public DateTime? LastUpdatedAt { get; init; }
    public List<UserFactInfo>? Facts { get; init; }
    public Dictionary<string, int>? FactsByCategory { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Information about a user fact.
/// </summary>
public sealed class UserFactInfo
{
    public string? Key { get; init; }
    public string? Content { get; init; }
    public string? Category { get; init; }
    public float Confidence { get; init; }
    public int ConfirmationCount { get; init; }
    public bool IsConfirmed { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string? Subject { get; init; }
    public string? Predicate { get; init; }
    public string? Object { get; init; }
}

/// <summary>
/// Result of fact categories listing.
/// </summary>
public sealed class FactCategoriesResult
{
    public bool Success { get; init; }
    public List<FactCategoryInfo>? Categories { get; init; }
}

/// <summary>
/// Information about a fact category.
/// </summary>
public sealed class FactCategoryInfo
{
    public string? Name { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Result of promotion paths listing.
/// </summary>
public sealed class PromotionPathsResult
{
    public bool Success { get; init; }
    public List<PromotionPathInfo>? Paths { get; init; }
}

/// <summary>
/// Information about a promotion path.
/// </summary>
public sealed class PromotionPathInfo
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public float ConfidenceThreshold { get; init; }
    public string? Criteria { get; init; }
}

#endregion
