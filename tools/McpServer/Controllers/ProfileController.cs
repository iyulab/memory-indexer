using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Profile;
using Microsoft.AspNetCore.Mvc;

namespace McpServer.Controllers;

/// <summary>
/// REST API controller for profile evolution features.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public partial class ProfileController : ControllerBase
{
    private readonly IProfileSnapshotService _snapshotService;
    private readonly IConfidenceDecayStrategy _decayStrategy;
    private readonly IArchiveStore _archiveStore;
    private readonly ILogger<ProfileController> _logger;
    private const string DefaultUserId = "default";

    public ProfileController(
        IProfileSnapshotService snapshotService,
        IConfidenceDecayStrategy decayStrategy,
        IArchiveStore archiveStore,
        ILogger<ProfileController> logger)
    {
        _snapshotService = snapshotService;
        _decayStrategy = decayStrategy;
        _archiveStore = archiveStore;
        _logger = logger;
    }

    /// <summary>
    /// Get profile statistics for a user.
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(ProfileStatsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfileStats(
        [FromQuery] string? userId = null,
        CancellationToken ct = default)
    {
        try
        {
            var uid = userId ?? DefaultUserId;
            var allFacts = await _archiveStore.GetAllAsync(uid, ct);
            var factsList = allFacts.ToList();

            var activeFacts = factsList.Where(f => f.IsActive).ToList();
            var staleFacts = activeFacts.Count(f => _decayStrategy.NeedsReconfirmation(f));

            var byCategory = activeFacts
                .GroupBy(f => f.Category)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var response = new ProfileStatsResponse
            {
                UserId = uid,
                TotalFacts = factsList.Count,
                ActiveFacts = activeFacts.Count,
                ConfirmedFacts = activeFacts.Count(f => f.IsConfirmed),
                StaleFacts = staleFacts,
                AverageConfidence = activeFacts.Count > 0 ? activeFacts.Average(f => f.Confidence) : 0f,
                ByCategory = byCategory,
                CompletenessScore = CalculateCompleteness(byCategory)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            LogFailedToGetProfileStats(_logger, ex, userId);
            return StatusCode(500, new { error = "Failed to get profile stats", details = ex.Message });
        }
    }

    /// <summary>
    /// Create a profile snapshot.
    /// </summary>
    [HttpPost("snapshots")]
    [ProducesResponseType(typeof(SnapshotResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSnapshot(
        [FromBody] CreateSnapshotRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var uid = request.UserId ?? DefaultUserId;
            var snapshot = await _snapshotService.CreateSnapshotAsync(uid, request.Label, ct);

            LogCreatedProfileSnapshot(_logger, snapshot.Id, uid);

            return CreatedAtAction(
                nameof(GetSnapshot),
                new { snapshotId = snapshot.Id, userId = uid },
                new SnapshotResponse
                {
                    Id = snapshot.Id.ToString(),
                    UserId = uid,
                    Label = snapshot.Label,
                    CreatedAt = snapshot.CreatedAt,
                    FactCount = snapshot.Facts.Count,
                    Stats = new ProfileStatsResponse
                    {
                        UserId = uid,
                        TotalFacts = snapshot.Stats.TotalFacts,
                        ActiveFacts = snapshot.Stats.TotalFacts - snapshot.Stats.StaleFactCount,
                        ConfirmedFacts = snapshot.Stats.ConfirmedFacts,
                        StaleFacts = snapshot.Stats.StaleFactCount,
                        AverageConfidence = snapshot.Stats.AverageConfidence,
                        CompletenessScore = snapshot.Stats.CompletenessScore
                    }
                });
        }
        catch (Exception ex)
        {
            LogFailedToCreateSnapshot(_logger, ex);
            return StatusCode(500, new { error = "Failed to create snapshot", details = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific snapshot.
    /// </summary>
    [HttpGet("snapshots/{snapshotId}")]
    [ProducesResponseType(typeof(SnapshotResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSnapshot(
        Guid snapshotId,
        [FromQuery] string? userId = null,
        CancellationToken ct = default)
    {
        try
        {
            var uid = userId ?? DefaultUserId;
            var snapshot = await _snapshotService.GetSnapshotAsync(uid, snapshotId, ct);

            if (snapshot == null)
            {
                return NotFound(new { error = "Snapshot not found", snapshotId });
            }

            return Ok(new SnapshotResponse
            {
                Id = snapshot.Id.ToString(),
                UserId = uid,
                Label = snapshot.Label,
                CreatedAt = snapshot.CreatedAt,
                FactCount = snapshot.Facts.Count,
                Stats = new ProfileStatsResponse
                {
                    UserId = uid,
                    TotalFacts = snapshot.Stats.TotalFacts,
                    ConfirmedFacts = snapshot.Stats.ConfirmedFacts,
                    StaleFacts = snapshot.Stats.StaleFactCount,
                    AverageConfidence = snapshot.Stats.AverageConfidence,
                    CompletenessScore = snapshot.Stats.CompletenessScore
                }
            });
        }
        catch (Exception ex)
        {
            LogFailedToGetSnapshot(_logger, ex, snapshotId);
            return StatusCode(500, new { error = "Failed to get snapshot", details = ex.Message });
        }
    }

    /// <summary>
    /// List all snapshots for a user.
    /// </summary>
    [HttpGet("snapshots")]
    [ProducesResponseType(typeof(SnapshotListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSnapshots(
        [FromQuery] string? userId = null,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        try
        {
            var uid = userId ?? DefaultUserId;
            var snapshots = await _snapshotService.ListSnapshotsAsync(uid, limit, ct);

            return Ok(new SnapshotListResponse
            {
                UserId = uid,
                Snapshots = snapshots.Select(s => new SnapshotSummary
                {
                    Id = s.Id.ToString(),
                    Label = s.Label,
                    CreatedAt = s.CreatedAt,
                    FactCount = s.FactCount
                }).ToList(),
                TotalCount = snapshots.Count
            });
        }
        catch (Exception ex)
        {
            LogFailedToListSnapshots(_logger, ex);
            return StatusCode(500, new { error = "Failed to list snapshots", details = ex.Message });
        }
    }

    /// <summary>
    /// Compare two snapshots.
    /// </summary>
    [HttpPost("snapshots/compare")]
    [ProducesResponseType(typeof(SnapshotDiffResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompareSnapshots(
        [FromBody] CompareSnapshotsRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var uid = request.UserId ?? DefaultUserId;
            var diff = await _snapshotService.CompareSnapshotsAsync(
                uid,
                request.OlderSnapshotId,
                request.NewerSnapshotId,
                ct);

            return Ok(new SnapshotDiffResponse
            {
                UserId = uid,
                HasChanges = diff.HasChanges,
                OlderTimestamp = diff.OlderTimestamp,
                NewerTimestamp = diff.NewerTimestamp,
                AddedCount = diff.Summary.AddedCount,
                RemovedCount = diff.Summary.RemovedCount,
                ModifiedCount = diff.Summary.ModifiedCount,
                TotalChanges = diff.Summary.TotalChanges,
                AddedFacts = diff.AddedFacts.Take(20).Select(f => f.Value).ToList(),
                RemovedFacts = diff.RemovedFacts.Take(20).Select(f => f.Value).ToList(),
                ModifiedFacts = diff.ModifiedFacts.Take(20).Select(m => new FactChangeInfo
                {
                    Key = m.Key,
                    OldValue = m.OldValue,
                    NewValue = m.NewValue,
                    ChangeType = m.ChangeType.ToString()
                }).ToList()
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            LogFailedToCompareSnapshots(_logger, ex);
            return StatusCode(500, new { error = "Failed to compare snapshots", details = ex.Message });
        }
    }

    /// <summary>
    /// Get stale facts that need re-confirmation.
    /// </summary>
    [HttpGet("stale-facts")]
    [ProducesResponseType(typeof(StaleFactsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStaleFacts(
        [FromQuery] string? userId = null,
        [FromQuery] float threshold = 0.5f,
        CancellationToken ct = default)
    {
        try
        {
            var uid = userId ?? DefaultUserId;
            var allFacts = await _archiveStore.GetAllAsync(uid, ct);
            var factsList = allFacts.Where(f => f.IsActive).ToList();

            var staleFacts = factsList
                .Where(f => _decayStrategy.NeedsReconfirmation(f, threshold))
                .Select(f => new StaleFactInfo
                {
                    Key = f.Key,
                    Value = f.Value,
                    OriginalConfidence = f.Confidence,
                    DecayedConfidence = _decayStrategy.CalculateDecayedConfidence(f, DateTime.UtcNow),
                    DaysSinceUpdate = (int)(DateTime.UtcNow - f.UpdatedAt).TotalDays,
                    Category = f.Category.ToString()
                })
                .OrderBy(f => f.DecayedConfidence)
                .Take(50)
                .ToList();

            return Ok(new StaleFactsResponse
            {
                UserId = uid,
                Threshold = threshold,
                StaleCount = staleFacts.Count,
                TotalActiveFacts = factsList.Count,
                StaleFacts = staleFacts
            });
        }
        catch (Exception ex)
        {
            LogFailedToGetStaleFacts(_logger, ex);
            return StatusCode(500, new { error = "Failed to get stale facts", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete a snapshot.
    /// </summary>
    [HttpDelete("snapshots/{snapshotId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteSnapshot(
        Guid snapshotId,
        [FromQuery] string? userId = null,
        CancellationToken ct = default)
    {
        try
        {
            var uid = userId ?? DefaultUserId;
            var deleted = await _snapshotService.DeleteSnapshotAsync(uid, snapshotId, ct);

            if (!deleted)
            {
                return NotFound(new { error = "Snapshot not found", snapshotId });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            LogFailedToDeleteSnapshot(_logger, ex, snapshotId);
            return StatusCode(500, new { error = "Failed to delete snapshot", details = ex.Message });
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get profile stats for user {UserId}")]
    private static partial void LogFailedToGetProfileStats(ILogger logger, Exception ex, string? userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created profile snapshot {SnapshotId} for user {UserId}")]
    private static partial void LogCreatedProfileSnapshot(ILogger logger, Guid snapshotId, string userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create snapshot")]
    private static partial void LogFailedToCreateSnapshot(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get snapshot {SnapshotId}")]
    private static partial void LogFailedToGetSnapshot(ILogger logger, Exception ex, Guid snapshotId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to list snapshots")]
    private static partial void LogFailedToListSnapshots(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to compare snapshots")]
    private static partial void LogFailedToCompareSnapshots(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get stale facts")]
    private static partial void LogFailedToGetStaleFacts(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete snapshot {SnapshotId}")]
    private static partial void LogFailedToDeleteSnapshot(ILogger logger, Exception ex, Guid snapshotId);

    private static float CalculateCompleteness(Dictionary<string, int> byCategory)
    {
        var totalCategories = 10; // Approximate category count
        var coveredCategories = byCategory.Count;
        var completeness = (float)coveredCategories / totalCategories;

        // Bonus for key categories
        var keyCategories = new[] { "Fact", "Preference", "Skill", "Work" };
        var keyFactCount = keyCategories.Sum(c => byCategory.GetValueOrDefault(c, 0));
        var keyCategoryBonus = Math.Min(0.3f, keyFactCount * 0.05f);

        return Math.Min(1f, completeness + keyCategoryBonus);
    }
}

#region Request/Response Models

public record CreateSnapshotRequest
{
    public string? UserId { get; init; }
    public string? Label { get; init; }
}

public record CompareSnapshotsRequest
{
    public string? UserId { get; init; }
    public Guid OlderSnapshotId { get; init; }
    public Guid? NewerSnapshotId { get; init; }
}

public record ProfileStatsResponse
{
    public string UserId { get; init; } = string.Empty;
    public int TotalFacts { get; init; }
    public int ActiveFacts { get; init; }
    public int ConfirmedFacts { get; init; }
    public int StaleFacts { get; init; }
    public float AverageConfidence { get; init; }
    public Dictionary<string, int>? ByCategory { get; init; }
    public float CompletenessScore { get; init; }
}

public record SnapshotResponse
{
    public string Id { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string? Label { get; init; }
    public DateTime CreatedAt { get; init; }
    public int FactCount { get; init; }
    public ProfileStatsResponse? Stats { get; init; }
}

public record SnapshotSummary
{
    public string Id { get; init; } = string.Empty;
    public string? Label { get; init; }
    public DateTime CreatedAt { get; init; }
    public int FactCount { get; init; }
}

public record SnapshotListResponse
{
    public string UserId { get; init; } = string.Empty;
    public List<SnapshotSummary> Snapshots { get; init; } = new();
    public int TotalCount { get; init; }
}

public record SnapshotDiffResponse
{
    public string UserId { get; init; } = string.Empty;
    public bool HasChanges { get; init; }
    public DateTime OlderTimestamp { get; init; }
    public DateTime NewerTimestamp { get; init; }
    public int AddedCount { get; init; }
    public int RemovedCount { get; init; }
    public int ModifiedCount { get; init; }
    public int TotalChanges { get; init; }
    public List<string> AddedFacts { get; init; } = new();
    public List<string> RemovedFacts { get; init; } = new();
    public List<FactChangeInfo> ModifiedFacts { get; init; } = new();
}

public record FactChangeInfo
{
    public string Key { get; init; } = string.Empty;
    public string OldValue { get; init; } = string.Empty;
    public string NewValue { get; init; } = string.Empty;
    public string ChangeType { get; init; } = string.Empty;
}

public record StaleFactsResponse
{
    public string UserId { get; init; } = string.Empty;
    public float Threshold { get; init; }
    public int StaleCount { get; init; }
    public int TotalActiveFacts { get; init; }
    public List<StaleFactInfo> StaleFacts { get; init; } = new();
}

public record StaleFactInfo
{
    public string Key { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public float OriginalConfidence { get; init; }
    public float DecayedConfidence { get; init; }
    public int DaysSinceUpdate { get; init; }
    public string Category { get; init; } = string.Empty;
}

#endregion
