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
    /// than as judged. Each has since been investigated; 20 across 12 types remain below, and each of
    /// those is a request field read by implementers outside these assemblies
    /// (<c>TextCompletionOptions</c>), waits on a decision about an unreached parent feature, needs a
    /// small implementation that is not written yet, or would change behaviour at its default value
    /// when wired.
    /// </para>
    /// <para>
    /// Removed since: <c>SecurityOptions</c> and <c>MultiTenantOptions</c>. Their switches were read by
    /// nothing (three numeric fields were validated and then used by nothing either), so configuring a
    /// security posture or tenant isolation through them changed no behaviour; the types are gone.
    /// <c>ResourceLimitOptions</c> was on this list when only the core assembly was scanned and is not on it
    /// now: <c>ResourceLimitEnforcer</c> in the SDK reads it.
    /// </para>
    /// <para>
    /// Also removed, because the feature each one promised does not exist in the library (no LLM client,
    /// reranker model, classifier model or HNSW index is built here; no automatic fact extraction,
    /// summarization, compression, consolidation, causal linking, inference chaining, community queries or
    /// abstractive summaries): <c>CompletionOptions.ApiKey</c> and <c>Endpoint</c>,
    /// <c>IntelligenceOptions.Enabled</c>, <c>ClassifierModel</c>, <c>FactExtractionEnabled</c> and
    /// <c>SummarizationEnabled</c>, <c>SearchOptions.RerankerModel</c>, <c>SensoryBufferOptions.Enabled</c>
    /// and <c>TriggerCheckInterval</c>, <c>SqliteOptions.HnswEfConstruction</c> and <c>HnswEfSearch</c>,
    /// <c>ConfidenceDecayOptions.DefaultStrategy</c>, <c>InferenceOptions.MaxDepth</c>,
    /// <c>LinkDiscoveryOptions.FindCausalLinks</c>, <c>OptimizationOptions.EnableCompression</c> and
    /// <c>EnableConsolidation</c>, <c>OutdatedDetectionOptions.FocusEntityTypes</c>,
    /// <c>ProfileExportOptions.IncludeAuditTrail</c>, <c>SubQueryOptions.IncludeCommunityQueries</c>,
    /// <c>ContextOptimizationOptions.MaxTokens</c>, <c>ExpansionOptions.OnlyAmbiguous</c>,
    /// <c>HybridGraphOptions.SemanticWeight</c>, <c>SummarizationOptions.Style</c> and
    /// <c>VCMOptions.ConsolidationInterval</c>. Options that were only validated and then used by nothing
    /// went with them - the scan counts a validator read as a read, so they were never listed here.
    /// </para>
    /// <para>
    /// Merged: <c>WorkingMemoryOrchestratorOptions</c> duplicated <c>WorkingMemoryOptions</c> field for field
    /// and was the one the working memory orchestrator read, so <c>EnableTopicChangeDetection</c> and
    /// <c>SummarizeBeforeArchival</c> set on the documented type did nothing. The duplicate is gone and the
    /// orchestrator reads <c>MemoryIndexerOptions.WorkingMemory</c>.
    /// </para>
    /// <para>
    /// Wired since, each with a test in both directions (the non-default value changes the outcome, the
    /// default keeps it): <c>LatencyOptions.ProfilingEnabled</c>,
    /// <c>SensoryBufferOptions.EnableBackgroundWorker</c>, <c>IntelligenceOptions.ClassificationEnabled</c>,
    /// <c>MemoryPromotionBackgroundOptions.Enabled</c>, <c>FactValidationOptions.SimilarityThreshold</c> and
    /// <c>UseSpoMatching</c>, <c>OptimizationOptions.EnableArchival</c>,
    /// <c>ProfileExportOptions.IncludeHistory</c>, <c>ContradictionDetectionOptions.AsOfDate</c>,
    /// <c>ConsolidationOptions.ForgettingDecayRate</c> and <c>ArchiveThreshold</c>, and
    /// <c>LineageQueryOptions.IncludeRelated</c>.
    /// </para>
    /// <para>
    /// 0.18.0 closed the last four by hand. Three were promises with nothing behind them and no way to
    /// build one without a magnitude nobody had chosen, so they are gone:
    /// <c>ConfidenceUpdateOptions.BoostFrequentlyAccessed</c> and <c>ReduceForContradictions</c> (a boost
    /// factor and a contradiction penalty) and <c>ContextOptimizationOptions.EnableChunkExpansion</c>.
    /// The fourth, <c>LatencyOptions.QueryCacheSize</c>, was different - its siblings
    /// <c>QueryCacheEnabled</c> and <c>QueryCacheTtlMinutes</c> were live in the same service, so the
    /// query cache ran with no bound at all. That one was wired rather than removed.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownUnread = new()
    {
        // Request fields the library populates and the consumer's ITextCompletionService
        // implementation reads - so they are read, just not inside these assemblies. The three that
        // nothing populated (TopP, FrequencyPenalty, PresencePenalty) were removed in 0.18.0.
        ["MemoryIndexer.Interfaces.TextCompletionOptions"] =
        [
            "MaxTokens", "StopSequences", "Temperature",
        ],
    };

    [Fact]
    public void EveryPublicOption_IsRead() =>
        OptionsReachability.Scan(Libraries, OptionsTypes.NamedWith("Options", "Config"))
            .ShouldMatchRoster(KnownUnread);
}
