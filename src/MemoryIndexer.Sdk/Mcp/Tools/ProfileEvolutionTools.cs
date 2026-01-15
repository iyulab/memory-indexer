using System.ComponentModel;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Profile;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Sdk.Mcp.Tools;

/// <summary>
/// MCP tools for profile evolution features.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public class ProfileEvolutionTools
{
    private readonly IFactInferenceEngine _inferenceEngine;
    private readonly IProfileSnapshotService _snapshotService;
    private readonly IConfidenceDecayStrategy _decayStrategy;
    private readonly IProfileExporter _profileExporter;
    private readonly IArchiveStore _archiveStore;

    public ProfileEvolutionTools(
        IFactInferenceEngine inferenceEngine,
        IProfileSnapshotService snapshotService,
        IConfidenceDecayStrategy decayStrategy,
        IProfileExporter profileExporter,
        IArchiveStore archiveStore)
    {
        _inferenceEngine = inferenceEngine;
        _snapshotService = snapshotService;
        _decayStrategy = decayStrategy;
        _profileExporter = profileExporter;
        _archiveStore = archiveStore;
    }

    #region Inference Tools

    /// <summary>
    /// Runs inference on user's facts to derive new insights.
    /// </summary>
    [McpServerTool(Name = "infer_facts")]
    [Description("Run inference on user's profile facts to derive new insights and relationships.")]
    public async Task<InferFactsResult> InferFacts(
        [Description("User ID (default: 'default')")] string userId = "default",
        [Description("Minimum source fact confidence (0-1)")] float minConfidence = 0.7f,
        [Description("Maximum results to return")] int maxResults = 20,
        [Description("Auto-store high-confidence inferences")] bool autoStore = false,
        CancellationToken cancellationToken = default)
    {
        var options = new InferenceOptions
        {
            MinSourceConfidence = minConfidence,
            MaxResults = maxResults,
            AutoStore = autoStore
        };

        var result = await _inferenceEngine.InferForUserAsync(userId, options, cancellationToken);

        return new InferFactsResult
        {
            Success = true,
            UserId = userId,
            SourceFactCount = result.SourceFactCount,
            InferredCount = result.InferredFacts.Count,
            StoredCount = result.StoredCount,
            ProcessingTimeMs = result.ProcessingTimeMs,
            Inferences = result.InferredFacts.Select(f => new InferenceDto
            {
                Content = f.Content,
                Category = f.Category.ToString(),
                Type = f.Type.ToString(),
                Confidence = f.Confidence,
                RuleName = f.RuleName,
                Explanation = f.Explanation,
                SourceKeys = f.SourceFactKeys.ToList()
            }).ToList()
        };
    }

    /// <summary>
    /// Gets registered inference rules.
    /// </summary>
    [McpServerTool(Name = "get_inference_rules")]
    [Description("Get list of registered inference rules.")]
    public Task<GetInferenceRulesResult> GetInferenceRules()
    {
        var rules = _inferenceEngine.GetRegisteredRules();

        return Task.FromResult(new GetInferenceRulesResult
        {
            Success = true,
            RuleCount = rules.Count,
            Rules = rules.Select(r => new InferenceRuleDto
            {
                Name = r.Name,
                Description = r.Description,
                Type = r.Type.ToString()
            }).ToList()
        });
    }

    #endregion

    #region Snapshot Tools

    /// <summary>
    /// Creates a snapshot of user's current profile.
    /// </summary>
    [McpServerTool(Name = "create_profile_snapshot")]
    [Description("Create a point-in-time snapshot of user's profile for tracking changes.")]
    public async Task<CreateSnapshotResult> CreateProfileSnapshot(
        [Description("User ID (default: 'default')")] string userId = "default",
        [Description("Optional label for this snapshot")] string? label = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotService.CreateSnapshotAsync(userId, label, cancellationToken);

        return new CreateSnapshotResult
        {
            Success = true,
            SnapshotId = snapshot.Id.ToString(),
            UserId = userId,
            Label = label,
            CreatedAt = snapshot.CreatedAt,
            FactCount = snapshot.Facts.Count,
            Stats = new ProfileStatsDto
            {
                TotalFacts = snapshot.Stats.TotalFacts,
                ConfirmedFacts = snapshot.Stats.ConfirmedFacts,
                StaleFacts = snapshot.Stats.StaleFactCount,
                AverageConfidence = snapshot.Stats.AverageConfidence,
                CompletenessScore = snapshot.Stats.CompletenessScore
            }
        };
    }

    /// <summary>
    /// Lists profile snapshots.
    /// </summary>
    [McpServerTool(Name = "list_profile_snapshots")]
    [Description("List available profile snapshots for a user.")]
    public async Task<ListSnapshotsResult> ListProfileSnapshots(
        [Description("User ID (default: 'default')")] string userId = "default",
        [Description("Maximum snapshots to return")] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await _snapshotService.ListSnapshotsAsync(userId, limit, cancellationToken);

        return new ListSnapshotsResult
        {
            Success = true,
            UserId = userId,
            SnapshotCount = snapshots.Count,
            Snapshots = snapshots.Select(s => new SnapshotSummaryDto
            {
                Id = s.Id.ToString(),
                Label = s.Label,
                CreatedAt = s.CreatedAt,
                FactCount = s.FactCount
            }).ToList()
        };
    }

    /// <summary>
    /// Compares two profile snapshots.
    /// </summary>
    [McpServerTool(Name = "compare_snapshots")]
    [Description("Compare two profile snapshots to see what changed.")]
    public async Task<CompareSnapshotsResult> CompareSnapshots(
        [Description("Older snapshot ID")] string olderSnapshotId,
        [Description("User ID (default: 'default')")] string userId = "default",
        [Description("Newer snapshot ID (null for current profile)")] string? newerSnapshotId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var olderId = Guid.Parse(olderSnapshotId);
            var newerId = newerSnapshotId != null ? Guid.Parse(newerSnapshotId) : (Guid?)null;

            var diff = await _snapshotService.CompareSnapshotsAsync(userId, olderId, newerId, cancellationToken);

            return new CompareSnapshotsResult
            {
                Success = true,
                UserId = userId,
                HasChanges = diff.HasChanges,
                OlderTimestamp = diff.OlderTimestamp,
                NewerTimestamp = diff.NewerTimestamp,
                Summary = new DiffSummaryDto
                {
                    AddedCount = diff.Summary.AddedCount,
                    RemovedCount = diff.Summary.RemovedCount,
                    ModifiedCount = diff.Summary.ModifiedCount,
                    TotalChanges = diff.Summary.TotalChanges
                },
                AddedFacts = diff.AddedFacts.Select(f => f.Value).Take(10).ToList(),
                RemovedFacts = diff.RemovedFacts.Select(f => f.Value).Take(10).ToList(),
                ModifiedFacts = diff.ModifiedFacts.Select(m => new FactChangeDto
                {
                    Key = m.Key,
                    OldValue = m.OldValue,
                    NewValue = m.NewValue,
                    ChangeType = m.ChangeType.ToString()
                }).Take(10).ToList()
            };
        }
        catch (Exception ex)
        {
            return new CompareSnapshotsResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    #endregion

    #region Decay Tools

    /// <summary>
    /// Gets stale facts that may need re-confirmation.
    /// </summary>
    [McpServerTool(Name = "get_stale_facts")]
    [Description("Get facts with decayed confidence that may need re-confirmation.")]
    public async Task<GetStaleFactsResult> GetStaleFacts(
        [Description("User ID (default: 'default')")] string userId = "default",
        [Description("Confidence threshold below which facts are stale")] float threshold = 0.5f,
        CancellationToken cancellationToken = default)
    {
        var allFacts = await _archiveStore.GetAllAsync(userId, cancellationToken);

        if (_decayStrategy is TimeBasedDecayStrategy timeBasedStrategy)
        {
            var staleFacts = timeBasedStrategy.GetStaleFacts(allFacts.ToList(), threshold);

            return new GetStaleFactsResult
            {
                Success = true,
                UserId = userId,
                Threshold = threshold,
                StaleCount = staleFacts.Count,
                TotalFacts = allFacts.Count,
                StaleFacts = staleFacts.Select(s => new StaleFactDto
                {
                    Key = s.Fact.Key,
                    Value = s.Fact.Value,
                    OriginalConfidence = s.OriginalConfidence,
                    DecayedConfidence = s.DecayedConfidence,
                    DaysSinceUpdate = s.DaysSinceUpdate,
                    RecommendedAction = s.RecommendedAction.ToString()
                }).ToList()
            };
        }

        return new GetStaleFactsResult
        {
            Success = true,
            UserId = userId,
            StaleCount = 0,
            TotalFacts = allFacts.Count,
            StaleFacts = []
        };
    }

    /// <summary>
    /// Calculates decayed confidence for a specific fact.
    /// </summary>
    [McpServerTool(Name = "calculate_decay")]
    [Description("Calculate the current decayed confidence for a fact.")]
    public async Task<CalculateDecayResult> CalculateDecay(
        [Description("Fact key")] string factKey,
        [Description("User ID (default: 'default')")] string userId = "default",
        CancellationToken cancellationToken = default)
    {
        var fact = await _archiveStore.GetAsync(userId, factKey, cancellationToken);

        if (fact == null)
        {
            return new CalculateDecayResult
            {
                Success = false,
                Message = $"Fact not found: {factKey}"
            };
        }

        var decayedConfidence = _decayStrategy.CalculateDecayedConfidence(fact, DateTime.UtcNow);
        var needsReconfirmation = _decayStrategy.NeedsReconfirmation(fact);

        DateTime? predictedStaleDate = null;
        if (_decayStrategy is TimeBasedDecayStrategy timeBasedStrategy)
        {
            predictedStaleDate = timeBasedStrategy.PredictStaleDate(fact);
        }

        return new CalculateDecayResult
        {
            Success = true,
            Key = factKey,
            Value = fact.Value,
            OriginalConfidence = fact.Confidence,
            DecayedConfidence = decayedConfidence,
            DecayAmount = fact.Confidence - decayedConfidence,
            NeedsReconfirmation = needsReconfirmation,
            LastUpdated = fact.UpdatedAt,
            PredictedStaleDate = predictedStaleDate
        };
    }

    #endregion

    #region Export Tools

    /// <summary>
    /// Exports user profile data (GDPR compliance).
    /// </summary>
    [McpServerTool(Name = "export_profile")]
    [Description("Export user profile data in JSON format for GDPR compliance.")]
    public async Task<ExportProfileResult> ExportProfile(
        [Description("User ID (default: 'default')")] string userId = "default",
        [Description("Include archived facts")] bool includeArchived = true,
        [Description("Include version history")] bool includeHistory = true,
        CancellationToken cancellationToken = default)
    {
        var options = new ProfileExportOptions
        {
            IncludeArchived = includeArchived,
            IncludeHistory = includeHistory
        };

        var result = await _profileExporter.ExportAsync(userId, options, cancellationToken);

        if (!result.Success)
        {
            return new ExportProfileResult
            {
                Success = false,
                Message = result.ErrorMessage
            };
        }

        return new ExportProfileResult
        {
            Success = true,
            UserId = userId,
            Format = _profileExporter.Format,
            FactCount = result.Metadata!.FactCount,
            ActiveFactCount = result.Metadata.ActiveFactCount,
            ArchivedFactCount = result.Metadata.ArchivedFactCount,
            SizeBytes = result.Metadata.SizeBytes,
            Checksum = result.Metadata.Checksum,
            Data = result.Data
        };
    }

    #endregion

    #region Result DTOs

    public class InferFactsResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? UserId { get; set; }
        public int SourceFactCount { get; set; }
        public int InferredCount { get; set; }
        public int StoredCount { get; set; }
        public long ProcessingTimeMs { get; set; }
        public List<InferenceDto>? Inferences { get; set; }
    }

    public class InferenceDto
    {
        public required string Content { get; set; }
        public required string Category { get; set; }
        public required string Type { get; set; }
        public float Confidence { get; set; }
        public string? RuleName { get; set; }
        public string? Explanation { get; set; }
        public List<string>? SourceKeys { get; set; }
    }

    public class GetInferenceRulesResult
    {
        public bool Success { get; set; }
        public int RuleCount { get; set; }
        public List<InferenceRuleDto>? Rules { get; set; }
    }

    public class InferenceRuleDto
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required string Type { get; set; }
    }

    public class CreateSnapshotResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? SnapshotId { get; set; }
        public string? UserId { get; set; }
        public string? Label { get; set; }
        public DateTime CreatedAt { get; set; }
        public int FactCount { get; set; }
        public ProfileStatsDto? Stats { get; set; }
    }

    public class ProfileStatsDto
    {
        public int TotalFacts { get; set; }
        public int ConfirmedFacts { get; set; }
        public int StaleFacts { get; set; }
        public float AverageConfidence { get; set; }
        public float CompletenessScore { get; set; }
    }

    public class ListSnapshotsResult
    {
        public bool Success { get; set; }
        public string? UserId { get; set; }
        public int SnapshotCount { get; set; }
        public List<SnapshotSummaryDto>? Snapshots { get; set; }
    }

    public class SnapshotSummaryDto
    {
        public required string Id { get; set; }
        public string? Label { get; set; }
        public DateTime CreatedAt { get; set; }
        public int FactCount { get; set; }
    }

    public class CompareSnapshotsResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? UserId { get; set; }
        public bool HasChanges { get; set; }
        public DateTime OlderTimestamp { get; set; }
        public DateTime NewerTimestamp { get; set; }
        public DiffSummaryDto? Summary { get; set; }
        public List<string>? AddedFacts { get; set; }
        public List<string>? RemovedFacts { get; set; }
        public List<FactChangeDto>? ModifiedFacts { get; set; }
    }

    public class DiffSummaryDto
    {
        public int AddedCount { get; set; }
        public int RemovedCount { get; set; }
        public int ModifiedCount { get; set; }
        public int TotalChanges { get; set; }
    }

    public class FactChangeDto
    {
        public required string Key { get; set; }
        public required string OldValue { get; set; }
        public required string NewValue { get; set; }
        public required string ChangeType { get; set; }
    }

    public class GetStaleFactsResult
    {
        public bool Success { get; set; }
        public string? UserId { get; set; }
        public float Threshold { get; set; }
        public int StaleCount { get; set; }
        public int TotalFacts { get; set; }
        public List<StaleFactDto>? StaleFacts { get; set; }
    }

    public class StaleFactDto
    {
        public required string Key { get; set; }
        public required string Value { get; set; }
        public float OriginalConfidence { get; set; }
        public float DecayedConfidence { get; set; }
        public int DaysSinceUpdate { get; set; }
        public required string RecommendedAction { get; set; }
    }

    public class CalculateDecayResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Key { get; set; }
        public string? Value { get; set; }
        public float OriginalConfidence { get; set; }
        public float DecayedConfidence { get; set; }
        public float DecayAmount { get; set; }
        public bool NeedsReconfirmation { get; set; }
        public DateTime LastUpdated { get; set; }
        public DateTime? PredictedStaleDate { get; set; }
    }

    public class ExportProfileResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? UserId { get; set; }
        public string? Format { get; set; }
        public int FactCount { get; set; }
        public int ActiveFactCount { get; set; }
        public int ArchivedFactCount { get; set; }
        public long SizeBytes { get; set; }
        public string? Checksum { get; set; }
        public string? Data { get; set; }
    }

    #endregion
}
