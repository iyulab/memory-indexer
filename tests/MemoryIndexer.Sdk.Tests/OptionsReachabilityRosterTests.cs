using System.Reflection;
using Iyu.Conventions.Testing;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Every public option in this library is read by the library. An option nothing reads is a promise it does not keep:
/// a caller sets it, and nothing changes and nothing is reported. The roster fails both ways - a new unread option,
/// and a listed one that has since been wired - so each change is recorded on purpose.
/// </summary>
/// <remarks>
/// This lives in the SDK test project rather than the core one because the library ships two assemblies and the
/// options declared in <c>MemoryIndexer</c> are largely read by <c>MemoryIndexer.Sdk</c>. Scanning only the core
/// assembly reports those as unread - it reported 221 that way, and the real number is smaller.
/// </remarks>
public class OptionsReachabilityRosterTests
{
    private static readonly Assembly[] Libraries =
    [
        Assembly.Load("MemoryIndexer"),
        Assembly.Load("MemoryIndexer.Sdk"),
    ];

    /// <summary>
    /// Options accepted as unread today. Shrink this list; never grow it silently.
    /// <para>
    /// Opening baseline (2026-09-20): 73 unread public options across 33 types, recorded as found rather
    /// than as judged. None has been investigated except the three operating-contract types below.
    /// </para>
    /// <para>
    /// Checked: <c>SecurityOptions</c> (all seven) and <c>MultiTenantOptions</c> (all five) are declaration
    /// only across both assemblies - grep over src/ finds one reference each, the declaration itself.
    /// <c>ResourceLimitOptions</c> was on this list when only the core assembly was scanned and is not on it
    /// now: <c>ResourceLimitEnforcer</c> in the SDK reads it.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownUnread = new()
    {
        ["MemoryIndexer.Configuration.CompletionOptions"] =
        [
            "ApiKey", "Endpoint",
        ],
        ["MemoryIndexer.Configuration.IntelligenceOptions"] =
        [
            "ClassificationEnabled", "ClassifierModel", "Enabled", "FactExtractionEnabled", "SummarizationEnabled",
        ],
        ["MemoryIndexer.Configuration.LatencyOptions"] =
        [
            "ProfilingEnabled", "QueryCacheSize",
        ],
        ["MemoryIndexer.Configuration.MemoryIndexerOptions"] = ["MultiTenant"],
        ["MemoryIndexer.Configuration.MultiTenantOptions"] =
        [
            "DefaultTenantId", "EnablePerTenantEncryption", "Enabled", "EnforceIsolation", "TenantHeaderName",
        ],
        ["MemoryIndexer.Configuration.SearchOptions"] = ["RerankerModel"],
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
            "HnswEfConstruction", "HnswEfSearch",
        ],
        ["MemoryIndexer.Configuration.WorkingMemoryOptions"] =
        [
            "EnableTopicChangeDetection", "SummarizeBeforeArchival",
        ],
        ["MemoryIndexer.Interfaces.ConfidenceDecayOptions"] = ["DefaultStrategy"],
        ["MemoryIndexer.Interfaces.ConfidenceUpdateOptions"] =
        [
            "BoostFrequentlyAccessed", "ReduceForContradictions",
        ],
        ["MemoryIndexer.Interfaces.FactValidationOptions"] =
        [
            "MaxComparisonFacts", "SimilarityThreshold", "UseSpoMatching",
        ],
        ["MemoryIndexer.Interfaces.InferenceOptions"] = ["MaxDepth"],
        ["MemoryIndexer.Interfaces.LinkDiscoveryOptions"] = ["FindCausalLinks"],
        ["MemoryIndexer.Interfaces.MemoryAnalysisOptions"] = ["MinConfidenceThreshold"],
        ["MemoryIndexer.Interfaces.OptimizationOptions"] =
        [
            "EnableArchival", "EnableCompression", "EnableConsolidation", "MaxWorkingMemoryAgeHours",
        ],
        ["MemoryIndexer.Interfaces.OutdatedDetectionOptions"] = ["FocusEntityTypes"],
        ["MemoryIndexer.Interfaces.ProfileExportOptions"] =
        [
            "IncludeAuditTrail", "IncludeHistory", "IncludeInferred",
        ],
        ["MemoryIndexer.Interfaces.ReflectionOptions"] =
        [
            "MaxInsights", "MinImportance",
        ],
        ["MemoryIndexer.Interfaces.SubQueryOptions"] = ["IncludeCommunityQueries"],
        ["MemoryIndexer.Interfaces.SubgraphOptions"] = ["IncludeTemporalInfo"],
        ["MemoryIndexer.Interfaces.TextCompletionOptions"] =
        [
            "FrequencyPenalty", "MaxTokens", "PresencePenalty", "StopSequences", "Temperature", "TopP",
        ],
        ["MemoryIndexer.Interfaces.WorkingMemoryOrchestratorOptions"] =
        [
            "Capacity", "EnableCapacityEnforcement",
        ],
        ["MemoryIndexer.Sdk.Intelligence.Conflict.ContradictionDetectionOptions"] = ["AsOfDate"],
        ["MemoryIndexer.Sdk.Intelligence.Consolidation.ConsolidationOptions"] =
        [
            "ArchiveThreshold", "ForgettingDecayRate",
        ],
        ["MemoryIndexer.Sdk.Intelligence.ContextOptimization.ContextOptimizationOptions"] =
        [
            "EnableChunkExpansion", "MaxTokens",
        ],
        ["MemoryIndexer.Sdk.Intelligence.EntityResolution.ExpansionOptions"] = ["OnlyAmbiguous"],
        ["MemoryIndexer.Sdk.Intelligence.Graph.HybridGraphOptions"] = ["SemanticWeight"],
        ["MemoryIndexer.Sdk.Intelligence.Security.LineageQueryOptions"] = ["IncludeRelated"],
        ["MemoryIndexer.Sdk.Intelligence.Summarization.SummarizationOptions"] =
        [
            "FocusTopics", "Style",
        ],
        ["MemoryIndexer.Sdk.Services.MemoryPromotionBackgroundOptions"] = ["Enabled"],
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
