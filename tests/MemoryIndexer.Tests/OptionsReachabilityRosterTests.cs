using System.Reflection;
using Iyu.Conventions.Testing;
using Xunit;

namespace MemoryIndexer.Tests;

/// <summary>
/// Every public option in this library is read by the library. An option nothing reads is a promise it does not keep:
/// a caller sets it, and nothing changes and nothing is reported. The roster fails both ways - a new unread option,
/// and a listed one that has since been wired - so each change is recorded on purpose.
/// </summary>
public class OptionsReachabilityRosterTests
{
    private static readonly Assembly[] Libraries =
    [
        Assembly.Load("MemoryIndexer"),
    ];

    /// <summary>
    /// Options accepted as unread today. Shrink this list; never grow it silently.
    /// <para>
    /// This is the roster's opening baseline (2026-09-20), recorded as found rather than as judged:
    /// the first run reported 221 unread public options across 39 types, and none has been
    /// investigated, so none carries a reason of its own. Recording them is what makes the gate start
    /// green and makes the *next* unread option a failure instead of silently joining a crowd.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownUnread = new()
    {
        ["MemoryIndexer.Configuration.CompletionOptions"] =
        [
            "ApiKey", "Endpoint", "Provider",
        ],
        ["MemoryIndexer.Configuration.DeduplicationOptions"] =
        [
            "ContentTypeRules", "Enabled", "LookbackWindow",
        ],
        ["MemoryIndexer.Configuration.EmbeddingOptions"] = ["CacheTtlMinutes"],
        ["MemoryIndexer.Configuration.IntelligenceOptions"] =
        [
            "ClassificationEnabled", "ClassifierModel", "Enabled", "FactExtractionEnabled", "SummarizationEnabled",
        ],
        ["MemoryIndexer.Configuration.LatencyOptions"] =
        [
            "BatchProcessingEnabled", "EarlyTerminationEnabled", "EarlyTerminationMinResults",
            "EmbeddingCacheEnabled", "EmbeddingCacheTtlMinutes", "MaxBatchSize", "ProfilingEnabled",
            "QueryCacheEnabled", "QueryCacheSize", "QueryCacheTtlMinutes",
        ],
        ["MemoryIndexer.Configuration.MemoryIndexerOptions"] =
        [
            "DefaultUserId", "MultiTenant", "ResourceLimits",
        ],
        ["MemoryIndexer.Configuration.MultiTenantOptions"] =
        [
            "DefaultTenantId", "EnablePerTenantEncryption", "Enabled", "EnforceIsolation", "TenantHeaderName",
        ],
        ["MemoryIndexer.Configuration.ResourceLimitOptions"] =
        [
            "EnforcementEnabled", "MaxMemoriesPerUser", "MaxStorageBytesPerUser", "WarningThresholdPercent",
        ],
        ["MemoryIndexer.Configuration.SearchOptions"] =
        [
            "DuplicateLookbackWindow", "EnableHyde", "HydeDocumentCount", "HydeMinQueryWords", "MinScore",
            "RerankerModel", "RrfK",
        ],
        ["MemoryIndexer.Configuration.SecurityOptions"] =
        [
            "EnableAuditLogging", "EnableInjectionDetection", "EnableLineageTracking", "EnablePiiDetection",
            "EnableRateLimiting", "GlobalPermitsPerMinute", "MaxAllowedRiskLevel",
        ],
        ["MemoryIndexer.Configuration.SensoryBufferOptions"] =
        [
            "EnableBackgroundWorker", "Enabled", "TriggerCheckInterval",
        ],
        ["MemoryIndexer.Configuration.SqliteOptions"] =
        [
            "AutoCleanupOldMemoriesDays", "AutoVacuum", "CheckpointIntervalMinutes", "EnableAutoMaintenance",
            "EnableFullTextSearch", "FtsTokenizer", "HnswEfConstruction", "HnswEfSearch", "IncrementalVacuumPages",
            "MaintenanceIntervalMinutes", "MaxDatabaseSizeMb", "UseWalMode",
        ],
        ["MemoryIndexer.Configuration.StorageOptions"] = ["ConnectionString"],
        ["MemoryIndexer.Configuration.TypeBalancerOptions"] =
        [
            "Enabled", "MinMemoriesForBalancing",
        ],
        ["MemoryIndexer.Configuration.WorkingMemoryOptions"] =
        [
            "EnableTopicChangeDetection", "SummarizeBeforeArchival",
        ],
        ["MemoryIndexer.Interfaces.CommunityDetectionOptions"] =
        [
            "ConvergenceThreshold", "MaxIterations", "MinCommunitySize", "RandomSeed", "UseWeightedEdges",
        ],
        ["MemoryIndexer.Interfaces.ConfidenceDecayOptions"] =
        [
            "AccessBonus", "CategoryMultipliers", "ConfirmationBonus", "DefaultStrategy", "HalfLifeDays",
            "MinimumConfidence", "RecentAccessDays",
        ],
        ["MemoryIndexer.Interfaces.ConfidenceUpdateOptions"] =
        [
            "ApplyTimeDecay", "BoostFrequentlyAccessed", "DecayHalfLifeDays", "MinConfidenceAfterDecay",
            "ReduceForContradictions",
        ],
        ["MemoryIndexer.Interfaces.ContextAssemblyOptions"] =
        [
            "CompressedRatio", "CustomFooter", "CustomHeader", "Format", "FullFidelityRatio",
            "IncludeGraphContext", "IncludeMetadata", "IncludeTierHeaders", "MaxTokens",
        ],
        ["MemoryIndexer.Interfaces.CorrectionOptions"] =
        [
            "CreateBackup", "MaxCorrectionsPerBatch", "MinPriority", "RecordHistory", "ValidateBeforeApply",
        ],
        ["MemoryIndexer.Interfaces.ExportOptions"] =
        [
            "FormatVersion", "IncludeDeleted", "IncludeEmbeddings", "IncludeMetadata", "SessionId", "Since",
            "Tiers", "Types", "Until", "UserId",
        ],
        ["MemoryIndexer.Interfaces.FactValidationOptions"] =
        [
            "AllowAutoResolution", "ConfidenceDifferentialThreshold", "MaxComparisonFacts", "SimilarityThreshold",
            "UseSpoMatching",
        ],
        ["MemoryIndexer.Interfaces.ImportOptions"] =
        [
            "BatchSize", "ConflictResolution", "OverrideSessionId", "OverrideUserId", "PreserveIds",
            "PreserveTimestamps", "RegenerateEmbeddings", "ValidateBeforeImport",
        ],
        ["MemoryIndexer.Interfaces.ImportanceOptions"] =
        [
            "ApplyMemoryBoost", "ConvergenceThreshold", "DampingFactor", "MaxIterations", "MemoryBoostFactor",
            "UseWeightedEdges",
        ],
        ["MemoryIndexer.Interfaces.InferenceOptions"] =
        [
            "AutoStore", "AutoStoreThreshold", "EnabledTypes", "IncludeCategories", "MaxDepth", "MaxResults",
            "MinInferenceConfidence", "MinSourceConfidence",
        ],
        ["MemoryIndexer.Interfaces.LinkDiscoveryOptions"] =
        [
            "FindCausalLinks", "FindEntityLinks", "FindSemanticLinks", "FindTemporalLinks", "MaxLinks",
            "MinSimilarity",
        ],
        ["MemoryIndexer.Interfaces.MemoryAnalysisOptions"] =
        [
            "CheckContradictions", "CheckDuplicates", "CheckOutdated", "FocusQuery", "MaxMemoriesToAnalyze",
            "MinConfidenceThreshold", "TrackGaps",
        ],
        ["MemoryIndexer.Interfaces.OptimizationOptions"] =
        [
            "EnableArchival", "EnableCompression", "EnableConsolidation", "MaxWorkingMemoryAgeHours",
            "MinImportanceToRetain", "TargetUtilization",
        ],
        ["MemoryIndexer.Interfaces.OutdatedDetectionOptions"] =
        [
            "CheckForSuperseding", "FocusEntityTypes", "MaxAgeDays", "MinConfidence",
        ],
        ["MemoryIndexer.Interfaces.ProfileExportOptions"] =
        [
            "ExcludeCategories", "IncludeArchived", "IncludeAuditTrail", "IncludeCategories", "IncludeHistory",
            "IncludeInferred", "IncludeMetadata", "RedactSensitive", "RedactionPatterns", "Since", "Until",
        ],
        ["MemoryIndexer.Interfaces.QueryExpansionOptions"] =
        [
            "ApplyImportanceBoost", "IncludeCommunityContext", "MaxExpansionTokens", "MaxHops",
            "MaxRelatedEntities", "MinImportanceScore",
        ],
        ["MemoryIndexer.Interfaces.ReflectionOptions"] =
        [
            "DiscoverLinks", "FocusTopic", "GenerateInsights", "IdentifyPatterns", "IncludeTypes", "MaxInsights",
            "MaxMemories", "MinImportance", "ReflectionDepth", "TimeWindow",
        ],
        ["MemoryIndexer.Interfaces.RetentionPolicyOptions"] =
        [
            "CategoryRules", "DefaultMaxAgeDays", "DefaultMinConfidence", "DefaultStaleAfterDays",
            "UseConfidenceDecay",
        ],
        ["MemoryIndexer.Interfaces.SemanticStoreOptions"] =
        [
            "ConfidenceBoostPerConfirmation", "EnableSemanticSearch", "MaxEntriesPerUser",
            "MinConfidenceThreshold", "MinConfirmationCount",
        ],
        ["MemoryIndexer.Interfaces.SubQueryOptions"] =
        [
            "IncludeCommunityQueries", "IncludeRelationshipQueries", "MaxSubQueries",
        ],
        ["MemoryIndexer.Interfaces.SubgraphOptions"] =
        [
            "IncludeTemporalInfo", "MaxEntities", "MaxHops", "MaxMemories", "MinConfidence",
        ],
        ["MemoryIndexer.Interfaces.TextCompletionOptions"] =
        [
            "FrequencyPenalty", "MaxTokens", "PresencePenalty", "StopSequences", "Temperature", "TopP",
        ],
        ["MemoryIndexer.Interfaces.WorkingMemoryOrchestratorOptions"] =
        [
            "Capacity", "EnableCapacityEnforcement", "EnableTopicChangeDetection", "IdleTimeout",
            "SummarizeBeforeArchival", "TokenThreshold", "TopicChangeSimilarityThreshold", "TurnThreshold",
        ],
        ["MemoryIndexer.Services.VCMOptions"] =
        [
            "AutoEvictionTrigger", "ConsolidationInterval", "EnableAutoEviction",
        ],
    };

    [Fact]
    public void EveryPublicOption_IsRead() =>
        OptionsReachability.Scan(Libraries, OptionsTypes.NamedWith("Options", "Config"))
            .ShouldMatchRoster(KnownUnread);
}
