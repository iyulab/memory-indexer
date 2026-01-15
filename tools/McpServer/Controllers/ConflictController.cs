using MemoryIndexer.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace McpServer.Controllers;

/// <summary>
/// REST API controller for fact conflict detection and resolution operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ConflictController : ControllerBase
{
    private readonly IFactValidator _factValidator;
    private readonly IArchiveStore _archiveStore;
    private readonly ILogger<ConflictController> _logger;
    private const string DefaultUserId = "default";

    public ConflictController(
        IFactValidator factValidator,
        IArchiveStore archiveStore,
        ILogger<ConflictController> logger)
    {
        _factValidator = factValidator ?? throw new ArgumentNullException(nameof(factValidator));
        _archiveStore = archiveStore ?? throw new ArgumentNullException(nameof(archiveStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validate a fact against existing facts.
    /// Detects conflicts and recommends resolution action.
    /// </summary>
    /// <param name="request">The fact validation request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with conflicts and recommended action</returns>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(ValidateFactResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateFact(
        [FromBody] ValidateFactRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest(new { error = "Content is required" });
        }

        try
        {
            var userId = request.UserId ?? DefaultUserId;

            // Parse category
            if (!Enum.TryParse<FactCategory>(request.Category, true, out var factCategory))
            {
                factCategory = FactCategory.General;
            }

            // Create UserFact
            var newFact = new UserFact
            {
                Content = request.Content,
                Category = factCategory,
                Confidence = request.Confidence ?? 0.9f,
                Subject = request.Subject,
                Predicate = request.Predicate,
                Object = request.ObjectValue
            };

            // Get existing facts
            var existingFacts = await _archiveStore.GetAllAsync(userId, cancellationToken);

            // Validate
            var analysis = await _factValidator.ValidateAsync(newFact, existingFacts, cancellationToken: cancellationToken);

            _logger.LogInformation("Validated fact for user {UserId}: {Action}, {ConflictCount} conflicts",
                userId, analysis.RecommendedAction, analysis.Conflicts.Count);

            return Ok(new ValidateFactResponse
            {
                IsValid = analysis.IsValid,
                RecommendedAction = analysis.RecommendedAction.ToString(),
                AppliedStrategy = analysis.AppliedStrategy?.ToString(),
                RequiresConfirmation = analysis.RequiresConfirmation,
                ConfirmationQuestion = analysis.ConfirmationQuestion,
                Confidence = analysis.Confidence,
                Reasoning = analysis.Reasoning,
                ConflictCount = analysis.Conflicts.Count,
                Conflicts = analysis.Conflicts.Select(c => new ConflictDetail
                {
                    Type = c.ConflictType.ToString(),
                    ExistingFactKey = c.ExistingFact?.Key,
                    ExistingFactValue = c.ExistingFact?.Value,
                    Confidence = c.Confidence,
                    SimilarityScore = c.SimilarityScore,
                    Description = c.Description
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate fact");
            return StatusCode(500, new { error = "Failed to validate fact", details = ex.Message });
        }
    }

    /// <summary>
    /// Get the version history of a fact.
    /// Returns all versions from newest to oldest.
    /// </summary>
    /// <param name="key">The fact key to get history for</param>
    /// <param name="userId">User ID (default: "default")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of fact versions</returns>
    [HttpGet("history")]
    [ProducesResponseType(typeof(FactHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFactHistory(
        [FromQuery] string key,
        [FromQuery] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return BadRequest(new { error = "Key is required" });
        }

        try
        {
            userId ??= DefaultUserId;
            var history = await _archiveStore.GetHistoryAsync(userId, key, cancellationToken);

            _logger.LogDebug("Retrieved {Count} versions for fact {Key}, user {UserId}",
                history.Count, key, userId);

            return Ok(new FactHistoryResponse
            {
                UserId = userId,
                Key = key,
                VersionCount = history.Count,
                Versions = history.Select(e => new FactVersion
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
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get fact history for key {Key}", key);
            return StatusCode(500, new { error = "Failed to get fact history", details = ex.Message });
        }
    }

    /// <summary>
    /// Get facts that were valid at a specific point in time.
    /// </summary>
    /// <param name="asOfDate">The date to query (ISO 8601 format)</param>
    /// <param name="userId">User ID (default: "default")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of facts valid at the specified date</returns>
    [HttpGet("temporal")]
    [ProducesResponseType(typeof(TemporalFactsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFactsValidAt(
        [FromQuery] string asOfDate,
        [FromQuery] string? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(asOfDate))
        {
            return BadRequest(new { error = "asOfDate is required" });
        }

        if (!DateTime.TryParse(asOfDate, out var date))
        {
            return BadRequest(new { error = "Invalid date format. Use ISO 8601 (e.g., '2024-01-15')" });
        }

        try
        {
            userId ??= DefaultUserId;
            var facts = await _archiveStore.GetValidAtAsync(userId, date, cancellationToken);

            _logger.LogDebug("Retrieved {Count} facts valid at {Date} for user {UserId}",
                facts.Count, date, userId);

            return Ok(new TemporalFactsResponse
            {
                UserId = userId,
                AsOfDate = date,
                FactCount = facts.Count,
                Facts = facts.Select(e => new TemporalFact
                {
                    Key = e.Key,
                    Value = e.Value,
                    Category = e.Category.ToString(),
                    Confidence = e.Confidence,
                    ValidFrom = e.ValidFrom,
                    ValidTo = e.ValidTo,
                    IsActive = e.IsActive
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get temporal facts for date {Date}", asOfDate);
            return StatusCode(500, new { error = "Failed to get temporal facts", details = ex.Message });
        }
    }

    /// <summary>
    /// Archive an existing fact and update with new value.
    /// Creates a versioned history chain.
    /// </summary>
    /// <param name="request">Archive and update request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated fact information</returns>
    [HttpPost("archive-and-update")]
    [ProducesResponseType(typeof(ArchiveAndUpdateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveAndUpdateFact(
        [FromBody] ArchiveAndUpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return BadRequest(new { error = "Key is required" });
        }

        if (string.IsNullOrWhiteSpace(request.NewValue))
        {
            return BadRequest(new { error = "NewValue is required" });
        }

        try
        {
            var userId = request.UserId ?? DefaultUserId;
            var updatedEntry = await _archiveStore.ArchiveAndUpdateAsync(userId, request.Key, request.NewValue, cancellationToken);

            if (updatedEntry == null)
            {
                return NotFound(new { error = $"Fact with key '{request.Key}' not found" });
            }

            _logger.LogInformation("Archived and updated fact {Key} for user {UserId}: v{OldVersion} -> v{NewVersion}",
                request.Key, userId, updatedEntry.Version - 1, updatedEntry.Version);

            return Ok(new ArchiveAndUpdateResponse
            {
                Success = true,
                Key = updatedEntry.Key,
                NewValue = updatedEntry.Value,
                Version = updatedEntry.Version,
                ArchivedKey = updatedEntry.SupersedesKey,
                Message = $"Archived v{updatedEntry.Version - 1} and created v{updatedEntry.Version}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive and update fact {Key}", request.Key);
            return StatusCode(500, new { error = "Failed to archive and update fact", details = ex.Message });
        }
    }

    /// <summary>
    /// Get category-specific resolution rules.
    /// </summary>
    /// <param name="category">Fact category name</param>
    /// <returns>Resolution rules for the category</returns>
    [HttpGet("rules/{category}")]
    [ProducesResponseType(typeof(CategoryRuleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult GetCategoryRule(string category)
    {
        if (!Enum.TryParse<FactCategory>(category, true, out var factCategory))
        {
            return BadRequest(new { error = $"Unknown category: {category}" });
        }

        var rule = _factValidator.GetCategoryRule(factCategory);

        return Ok(new CategoryRuleResponse
        {
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
    /// <returns>List of all category rules</returns>
    [HttpGet("rules")]
    [ProducesResponseType(typeof(AllCategoryRulesResponse), StatusCodes.Status200OK)]
    public IActionResult GetAllCategoryRules()
    {
        var rules = CategoryResolutionRule.Defaults.Values
            .Select(r => new CategoryRuleResponse
            {
                Category = r.Category.ToString(),
                DefaultStrategy = r.DefaultStrategy.ToString(),
                MinConfidenceDifferential = r.MinConfidenceDifferential,
                RequiresExplicitConfirmation = r.RequiresExplicitConfirmation,
                PreserveHistory = r.PreserveHistory,
                Description = r.Description
            })
            .ToList();

        return Ok(new AllCategoryRulesResponse
        {
            RuleCount = rules.Count,
            Rules = rules
        });
    }
}

#region Request/Response Models

public record ValidateFactRequest
{
    public string? UserId { get; init; }
    public required string Content { get; init; }
    public string? Category { get; init; }
    public string? Subject { get; init; }
    public string? Predicate { get; init; }
    public string? ObjectValue { get; init; }
    public float? Confidence { get; init; }
}

public record ValidateFactResponse
{
    public bool IsValid { get; init; }
    public string? RecommendedAction { get; init; }
    public string? AppliedStrategy { get; init; }
    public bool RequiresConfirmation { get; init; }
    public string? ConfirmationQuestion { get; init; }
    public float Confidence { get; init; }
    public string? Reasoning { get; init; }
    public int ConflictCount { get; init; }
    public List<ConflictDetail>? Conflicts { get; init; }
}

public record ConflictDetail
{
    public string? Type { get; init; }
    public string? ExistingFactKey { get; init; }
    public string? ExistingFactValue { get; init; }
    public float Confidence { get; init; }
    public float? SimilarityScore { get; init; }
    public string? Description { get; init; }
}

public record FactHistoryResponse
{
    public string? UserId { get; init; }
    public string? Key { get; init; }
    public int VersionCount { get; init; }
    public List<FactVersion>? Versions { get; init; }
}

public record FactVersion
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

public record TemporalFactsResponse
{
    public string? UserId { get; init; }
    public DateTime? AsOfDate { get; init; }
    public int FactCount { get; init; }
    public List<TemporalFact>? Facts { get; init; }
}

public record TemporalFact
{
    public string? Key { get; init; }
    public string? Value { get; init; }
    public string? Category { get; init; }
    public float Confidence { get; init; }
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }
    public bool IsActive { get; init; }
}

public record ArchiveAndUpdateRequest
{
    public string? UserId { get; init; }
    public required string Key { get; init; }
    public required string NewValue { get; init; }
}

public record ArchiveAndUpdateResponse
{
    public bool Success { get; init; }
    public string? Key { get; init; }
    public string? NewValue { get; init; }
    public int Version { get; init; }
    public string? ArchivedKey { get; init; }
    public string? Message { get; init; }
}

public record CategoryRuleResponse
{
    public string? Category { get; init; }
    public string? DefaultStrategy { get; init; }
    public float MinConfidenceDifferential { get; init; }
    public bool RequiresExplicitConfirmation { get; init; }
    public bool PreserveHistory { get; init; }
    public string? Description { get; init; }
}

public record AllCategoryRulesResponse
{
    public int RuleCount { get; init; }
    public List<CategoryRuleResponse>? Rules { get; init; }
}

#endregion
