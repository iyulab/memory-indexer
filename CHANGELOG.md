# Changelog

All notable changes to Memory Indexer are documented here.

## [v0.19.1] - 2026-09-23

### Fixed
- **The integration guides call the API that exists.** `docs/INTEGRATIONS.md` and `docs/GUIDES.md` were written against
  a tier API the library does not have (`IVirtualContextManager.AddToRecentlyAsync` / `RetrieveHybridAsync`, graph
  `AddRelationshipAsync` / `GetRelatedEntitiesAsync`, a profile `RecallFactsAsync`) and against `StoreAsync` / `RecallAsync`
  signatures `IMemoryService` never had. The samples now use `IMemoryService.RememberAsync` / `RecallAsync`,
  `IMemoryPrimitives.EncodeAsync` / `RetrieveAsync` (type, importance and metadata live on `EncodeRequest`),
  `IMemoryGraphService.LinkMemoryToGraphAsync` / `FindRelatedMemoriesAsync` and `IMemoryStore.StoreBatchAsync` (which
  exists — the guide called batching "not yet supported"). `docs/INTELLIGENCE.md` shows the real OpenTelemetry meter
  name (`AddMeter("MemoryIndexer")`) and metric name (`memory_indexer.intelligence.graph_queries`).
- **The README quick start calls the API that exists.** It stored with `memoryService.StoreAsync(userId, text,
  importance:)` and recalled with `RecallAsync(userId, query, limit:)` — neither signature is on `IMemoryService`.
  It now shows `RememberAsync(userId, content)` and `RecallAsync(userId, sessionId: null, query, limit:)`.
- A docs snippet roster test checks that the names the README and `docs/*.md` call exist; the guides' remaining
  phantoms (a tier API `AddToRecentlyAsync` / `RetrieveHybridAsync` the library does not have) are pinned in it.

## [v0.19.0] - 2026-09-23

### Removed
- **Breaking: seven `TenantConfiguration` switches nothing read.** `RequirePiiDetection` and `EnableAuditLogging`
  (both defaulting to `true`), `EncryptionKeyId`, `DataRetentionDays`, `AllowedMemoryTypes`, `AllowedMetadataFields` and
  `RateLimitOverrides` were declared per tenant and applied by no code path — a tenant configured with them was not
  protected, restricted or retained by them. The same switches at the global level were removed in 0.18.0; these were
  their per-tenant copies. `MaxMemories` and `MaxStorageBytes` stay: `ResourceLimitEnforcer` applies them. Migration:
  delete the assignments; compose `IPiiDetector`, `IAuditLogger` and `IRateLimiter` explicitly for those behaviours.

## [v0.18.5] - 2026-09-23

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.72.0 -> 0.72.1, `LMSupply.Generator` 0.72.0 -> 0.72.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.18.4] - 2026-09-23

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.71.0 -> 0.72.0, `LMSupply.Generator` 0.71.0 -> 0.72.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.18.3] - 2026-09-22

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.70.0 -> 0.71.0, `LMSupply.Generator` 0.70.0 -> 0.71.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.18.2] - 2026-09-21

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.69.0 -> 0.70.0, `LMSupply.Generator` 0.69.0 -> 0.70.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.18.1] - 2026-09-21

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.68.3 -> 0.69.0, `LMSupply.Generator` 0.68.3 -> 0.69.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.18.0] - 2026-09-20

### Changed
- **Seven options that were declared and never read now take effect — and their defaults were moved
  to what the code actually did**, so nobody's behaviour changes on the upgrade. The declarations
  carried numbers that had never applied; adopting them as-is would have silently narrowed what
  existing consumers get. Setting any of these now does what it always said it would:
  - `ReflectionOptions.MinImportance` filters the memories a reflection covers. **Default 0.3 → 0**
    (no filter).
  - `ReflectionOptions.MaxInsights` caps the insights returned. **Type `int` → `int?`, default 10 →
    `null`** (no cap).
  - `FactValidationOptions.MaxComparisonFacts` caps how many active facts a new one is compared
    against. **Type `int` → `int?`, default 50 → `null`** (no cap).
  - `MemoryAnalysisOptions.MinConfidenceThreshold` drops contradictions and outdated-memory findings
    below it, before the health score and suggested corrections are derived from them.
    **Default 0.5 → 0** (no filter).
  - `VCMOptions.EnableAutoEviction` / `AutoEvictionTrigger` now evict after a page-in that pushes
    saturation to the trigger level. `DefensiveEvictAsync` existed and nothing called it.
    **`EnableAutoEviction` default `true` → `false`**: no page-in has ever evicted, so leaving the
    declared default would make every existing consumer start losing paged-in memories.
  - `ProfileExportOptions.IncludeInferred` excludes inferred facts when set to `false`.
    **Default `false` → `true`** — this is the one default that moved *away* from the declaration on
    purpose: an export of what the system holds about a person should carry what was inferred about
    them, and it is also what the exporter did before.

### Added
- **Three declared options now have the small implementation they described.**
  - `SubgraphOptions.IncludeTemporalInfo` appends a triple's validity window to the formatted
    subgraph context (`(valid 2026-01-01–2026-06-30)`, or `from`/`until` when it is open-ended).
  - `SummarizationOptions.FocusTopics` gives a sentence naming one of the requested topics the same
    scoring bonus the extractive summarizer already gave entities and timestamps.
  - `OptimizationOptions.MaxWorkingMemoryAgeHours` demotes working memory that has sat past the
    limit, as a second and independent reason alongside the importance score. **Type `int` → `int?`,
    default 24 → `null`** (disabled) — applying an age rule by default would start demoting
    memories today's consumers keep. Note it applies to the same set the importance rule does
    (stability at or below `Stabilizing`), not to a tier.

### Removed
- **Breaking: `TextCompletionOptions.TopP`, `.FrequencyPenalty` and `.PresencePenalty`.** Nothing in
  this library ever populated them, so every `ITextCompletionService` implementation received them
  as `null` — a completion service that dutifully mapped all three (as the `IronHive.Agent` adapter
  and the sample services here did) was mapping values that could not arrive. `Temperature`,
  `MaxTokens` and `StopSequences` are populated and stay. If you need sampling knobs on this port,
  say so and they come back as fields the library actually sets.
- **Breaking: `SecurityOptions` and `MultiTenantOptions`** (and `MemoryIndexerOptions.Security` /
  `.MultiTenant`). **None of their switches did anything.** `EnablePiiDetection`,
  `EnableInjectionDetection`, `EnableRateLimiting`, `EnableAuditLogging`, `MaxAllowedRiskLevel`,
  `EnforceIsolation`, `EnablePerTenantEncryption` and the rest were read by nothing, so a
  configuration that set a security posture or tenant isolation through them was not protected by
  it. Three numeric fields were range-checked by the configuration validator and then used by
  nothing either; those checks are removed with them.
  Migration: delete the `Security` and `MultiTenant` sections from your configuration. The
  components themselves are unchanged and remain available to compose explicitly: `IPiiDetector`
  and `IPromptInjectionDetector` (also exposed as MCP security tools), `IRateLimiter` with its own
  `RateLimitOptions`, `IAuditLogger`, and `ITenantContext`.
- **Breaking: options that promised a feature this library does not have are removed.** Each was
  declared and documented, several defaulted to `true`, and nothing read any of them: setting one
  changed nothing and reported nothing. Migration for every item below unless it says otherwise:
  delete the assignment or the configuration key; it never had an effect.
  - **`CompletionOptions` keeps only `Provider`.** `ApiKey`, `Endpoint`, `Model`, `TimeoutSeconds`,
    `DefaultTemperature` and `DefaultMaxTokens` configured an LLM client the library never builds
    (the last four were range-checked by the configuration validator and then used by nothing;
    those checks are removed too). For a real model, register your own `ITextCompletionService`
    before `AddMemoryIndexer()` and configure it there; the only built-in provider is `Mock`.
  - **`IntelligenceOptions` keeps only `ClassificationEnabled`.** `Enabled` switched nothing off,
    `ClassifierModel` named a model that is never loaded (the classifier is rule-based), and
    `FactExtractionEnabled` / `SummarizationEnabled` described automatic extraction and
    summarization that do not run. `MaxGeneratorTokens` and `GeneratorTemperature` were validated
    and then used by nothing. `LocalMemoryClassifier`'s constructor no longer takes
    `IOptions<MemoryIndexerOptions>`; it only stored the value.
  - **`SearchOptions.RerankerModel`.** No reranker model is loaded; the registered
    `IRerankerService` (`MockRerankerService`) keeps the original scores. Register your own
    `IRerankerService` to rerank.
  - **`SensoryBufferOptions.Enabled` and `TriggerCheckInterval`.** The library has no ingest path
    that chooses between the buffer and working memory, so `Enabled = false` bypassed nothing; the
    buffer is used only by code that enqueues into `IBuffer` itself. The promotion loop runs on
    `MemoryPromotionBackgroundOptions.CheckIntervalSeconds`; use that instead of
    `TriggerCheckInterval`.
  - **`SqliteOptions.HnswM`, `HnswEfConstruction` and `HnswEfSearch`.** The SQLite store has no
    HNSW index to tune (`HnswM` was validated and then used by nothing). The keys are also gone
    from the MCP server's `appsettings.json` and the Qdrant sample configuration.
  - **Per-call options:** `ConfidenceDecayOptions.DefaultStrategy` (only the time-based strategy
    exists), `InferenceOptions.MaxDepth` (inference is single-pass, nothing is chained),
    `LinkDiscoveryOptions.FindCausalLinks` (no causal link discovery),
    `OptimizationOptions.EnableCompression` and `EnableConsolidation` (optimization always reported
    0 compressed and 0 consolidated), `OutdatedDetectionOptions.FocusEntityTypes` (memories carry
    no typed entities), `ProfileExportOptions.IncludeAuditTrail` (no access history is stored),
    `ContextOptimizationOptions.MaxTokens` (`TargetTokens` is the enforced ceiling; use it),
    `ExpansionOptions.OnlyAmbiguous` (coreferences carry no ambiguity information),
    `HybridGraphOptions.SemanticWeight` (semantic and graph results are returned side by side and
    never fused into one score) and `VCMOptions.ConsolidationInterval` (nothing schedules
    consolidation; call `ConsolidateAsync` yourself).
  - **`SubQueryOptions.IncludeCommunityQueries`, with `SubQueryType.PatternMatch` and
    `SubQueryType.CommunitySearch`.** Sub-query generation only ever produced `EntityFacts` and
    `EntityRelationship`; those two keep their numeric values (0 and 1).
- **Breaking: three more options that were declared and read by nothing** —
  `ConfidenceUpdateOptions.BoostFrequentlyAccessed` and `.ReduceForContradictions` (both defaulted
  to `true`, so a caller had every reason to believe a frequently-read memory gained confidence and
  a contradicted one lost it; the sibling `ApplyTimeDecay`, `DecayHalfLifeDays` and
  `MinConfidenceAfterDecay` are read, which is what made these two credible) and
  `ContextOptimizationOptions.EnableChunkExpansion` (its siblings `EnableMMR` and `EnableHyDE` are
  read by the optimizer; nothing expands chunk context). Implementing the first two needs a boost
  factor and a contradiction penalty that nobody has chosen, and there is no chunk context to
  expand, so they go rather than ship as promises. Migration: delete the assignment; it never had
  an effect. Ask and they come back as fields with a stated magnitude.
- **Breaking: `SummarizationOptions.Style`, the `SummaryStyle` enum and the `style` parameter of the
  `SummarizeMemories` MCP tool are removed.** Every summary is extractive: `Abstractive` and the
  default `Hybrid` produced exactly the same text as `Extractive`. Migration: delete the
  assignment; MCP clients stop sending `style`.
- **Breaking: `WorkingMemoryOrchestratorOptions` and its `MemoryIndexer:VCM:WorkingOrchestrator`
  configuration section are removed; `MemoryIndexer:WorkingMemory` is the one section that
  works.** The type duplicated `WorkingMemoryOptions` field for field with identical defaults, and
  it was the one the working memory orchestrator read - so the documented
  `MemoryIndexerOptions.WorkingMemory` reached the tier manager and capacity enforcement but never
  the orchestrator that archives working memory. `ShortTermMemoryOrchestratorService` now takes
  `IOptions<MemoryIndexerOptions>` and reads `.WorkingMemory`. Migration: move any
  `MemoryIndexer:VCM:WorkingOrchestrator` keys to `MemoryIndexer:WorkingMemory` (same names).
  Defaults are unchanged. If you already set `IdleTimeout`, `TokenThreshold`, `TurnThreshold`,
  `TopicChangeSimilarityThreshold` or `SummarizeBeforeArchival` under `WorkingMemory`, the
  orchestrator now honours those values where it used its own defaults before.

### Fixed
Options that were declared and documented but read by nothing now do what they say. Every default
keeps the previous behaviour; only a caller who sets a non-default value sees a change.

- **`LatencyOptions.ProfilingEnabled = false` now turns profiling off.** Nothing read it:
  `InMemoryLatencyProfiler` recorded every latency and cache access regardless. With `false` it
  records nothing and its metrics stay empty.
- **`MemoryPromotionBackgroundOptions.Enabled = false` now stops the background promoter, and
  `SensoryBufferOptions.EnableBackgroundWorker = false` now skips its buffer promotion phase.**
  The hosted service checked neither switch and always ran all three phases.
  **Breaking:** its constructor now also takes `IOptions<MemoryIndexerOptions>`. The container
  supplies it; only code that constructs the service by hand has to pass it.
- **`IntelligenceOptions.ClassificationEnabled = false` now stops `MemoryPrimitivesService.EncodeAsync`
  from calling the classifier.** A memory stored without a type or importance then gets the
  episodic type and an importance of 0.5. `SimpleMemoryService` still classifies: it needs the
  result to decide whether and where to store.
- **`FactValidationOptions.SimilarityThreshold` and `UseSpoMatching` now reach `ValidateAsync`.**
  The contradiction band started at a hard-coded 0.8 and subject-predicate matching always ran.
  `DetectConflictsAsync` takes no options and keeps the defaults.
- **`OptimizationOptions.EnableArchival = false` now makes `OptimizeMemoryAsync` demote nothing.**
  The demotion loop ran unconditionally.
- **`ProfileExportOptions.IncludeHistory = false` now exports current facts only.** Superseded
  versions are left out and `supersedesKey` is omitted; before, the export was identical either
  way. This also makes the `includeHistory` parameter of the `export_profile` MCP tool effective.
- **`ContradictionDetectionOptions.AsOfDate` now restricts triple contradiction detection to
  triples valid at that date.** It was ignored. Unset (the default) still compares every triple;
  memory contradiction detection has no validity period and does not use it.
- **`ConsolidationOptions.ForgettingDecayRate` and `ArchiveThreshold` now shape a consolidation
  cycle.** The curve used a fixed time constant and a fixed 0.2 archive cut-off; the defaults
  (0.1 and 0.2) reproduce them exactly. `ApplyForgettingCurveAsync` takes no options and keeps
  the defaults.
- **`LineageQueryOptions.IncludeRelated = true` now returns the events of related memories**
  (for example the sources of a merge) together with the memory's own, under the same filters
  and limit. It returned the memory's own events only.
- **`WorkingMemoryOptions.SummarizeBeforeArchival = false` now stops the session summary on
  archival, and `EnableTopicChangeDetection = false` now suppresses the topic change trigger.**
  The orchestrator read a duplicate options type (see Removed), and the tier manager raised the
  topic change trigger regardless of the switch. With detection off the trigger is still listed in
  `TierTriggerStatus.AllTriggers`, never satisfied.
- **`LatencyOptions.QueryCacheSize` now bounds the recall query cache, which ran with no bound at
  all.** The option documents "maximum number of cached query results (LRU eviction)" and its
  siblings `QueryCacheEnabled` and `QueryCacheTtlMinutes` were both honoured, so the cache was
  live - it just grew until entries expired. `OptimizedRecallService` now trims to the configured
  size on write, evicting least-recently-used first, the same way the embedding cache next to it
  already did.
- **`OptimizedRecallService` no longer writes into the container's shared `IMemoryCache`.**
  Breaking: its constructor no longer takes one, and the type is now `IDisposable`. It owns a cache
  instead, because a size bound belongs to a cache instance and cannot be expressed per entry on a
  shared one. This also removes a failure mode that has bitten this ecosystem before: on a shared
  cache that any library has given a `SizeLimit`, every `Set` without an entry size throws, and
  this service was one of those callers. Migration: resolve it from DI as before; if you construct
  it by hand, drop the cache argument and dispose the service.

## [v0.17.16] - 2026-09-19

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.68.2 -> 0.68.3, `LMSupply.Generator` 0.68.2 -> 0.68.3 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.15] - 2026-09-18

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.68.0 -> 0.68.2, `LMSupply.Generator` 0.68.0 -> 0.68.2 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.14] - 2026-09-17

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.67.0 -> 0.68.0, `LMSupply.Generator` 0.67.0 -> 0.68.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.13] - 2026-09-17

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.66.1 -> 0.67.0, `LMSupply.Generator` 0.66.1 -> 0.67.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.12] - 2026-09-17

### Fixed
- The packages now ship their XML documentation file (`MemoryIndexer`, `MemoryIndexer.Sdk`). `GenerateDocumentationFile` was never set, so every `///` doc was written but never delivered to a consumer. Generating it surfaced twelve doc comments with a raw `&` (`Q&A`, `Brin & Page`, `Deduplication & Quality`) that made the XML malformed, and five members whose `namespace` parameter had no `<param>` tag; all fixed. No code changes.

## [v0.17.11] - 2026-09-16

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.66.0 -> 0.66.1, `LMSupply.Generator` 0.66.0 -> 0.66.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.10] - 2026-09-16

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.65.1 -> 0.66.0, `LMSupply.Generator` 0.65.1 -> 0.66.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.9] - 2026-09-13

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.65.0 -> 0.65.1, `LMSupply.Generator` 0.65.0 -> 0.65.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.8] - 2026-09-12

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.64.0 -> 0.65.0, `LMSupply.Generator` 0.64.0 -> 0.65.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.7] - 2026-09-12

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.63.0 -> 0.64.0, `LMSupply.Generator` 0.63.0 -> 0.64.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.6] - 2026-09-11

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.62.0 -> 0.63.0, `LMSupply.Generator` 0.62.0 -> 0.63.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.5] - 2026-09-10

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.61.0 -> 0.62.0, `LMSupply.Generator` 0.61.0 -> 0.62.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.4] - 2026-09-10

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.60.0 -> 0.61.0, `LMSupply.Generator` 0.60.0 -> 0.61.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.3] - 2026-09-09

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.59.1 -> 0.60.0, `LMSupply.Generator` 0.59.1 -> 0.60.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.
- Bumped `Microsoft.SourceLink.GitHub` 10.0.103 -> 10.0.112: its `Microsoft.Build.Tasks.Git` dependency 10.0.102..10.0.110 is flagged by CVE-2026-62900 (GHSA-23fw-v26w-5fgq, moderate; NuGet audit NU1902 fails the build under `TreatWarningsAsErrors`). Build-time only (`PrivateAssets=All`); no runtime surface change.

## [v0.17.2] - 2026-09-09

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.59.0 -> 0.59.1, `LMSupply.Generator` 0.59.0 -> 0.59.1 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.1] - 2026-09-08

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.58.0 -> 0.59.0, `LMSupply.Generator` 0.58.0 -> 0.59.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.17.0] - 2026-09-08

### Changed
- MCP server (`tools/McpServer`): the HTTP mode is named for what it serves. It has run the
  Streamable HTTP transport since the ModelContextProtocol SDK 2.0 line, while the `--sse` flag,
  the `HTTP/SSE` labels in the startup banner and comments, and the info endpoint's
  `transport: "HTTP/SSE"` all named the HTTP+SSE transport that the MCP 2026-07-28 revision
  deprecates — and that this server never spoke. `--http` is the flag; `--sse` remains a
  deprecated alias that prints a warning naming the replacement and will be removed in a future
  release; the info endpoint now reports `transport: "streamable-http"`. README states the transport
  and the SDK's stateless default.

## [v0.16.14] - 2026-09-08

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.57.0 -> 0.58.0, `LMSupply.Generator` 0.57.0 -> 0.58.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.16.13] - 2026-09-07

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.55.4 -> 0.57.0, `LMSupply.Generator` 0.55.4 -> 0.57.0 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.
- Aligned third-party pins with the rest of the ecosystem: `OpenAI` 2.10.0 -> 2.12.0 (the ecosystem floor: Microsoft.Extensions.AI.OpenAI 10.9.0 caps OpenAI below 2.13.0), `BenchmarkDotNet` 0.14.0 -> 0.15.8 (cross-submodule floor consistency). No source changes.

## [v0.16.12] - 2026-09-07

### Changed
- Re-pinned sibling package(s) `LMSupply.Embedder` 0.55.0 -> 0.55.4, `LMSupply.Generator` 0.55.0 -> 0.55.4 — re-consumption of already-consumed iyulab packages via `check-pin-drift.ps1 -Fix`. No source changes.

## [v0.16.11] - 2026-09-05

### Fixed
- Re-pinned `LMSupply.Embedder`/`.Generator` from `0.54.0` to `0.55.0` — `0.54.0` was never actually
  published to nuget.org for these packages (their published history jumps `0.45.0` -> `0.55.0`),
  so `0.16.10`'s restore failed with NU1603 escalated to error and no nupkg for `0.16.10` was ever
  produced.

## [v0.16.10] - 2026-09-04

### Changed
- Re-pinned `LMSupply.Embedder`/`.Generator` from `0.42.10` to `0.54.0` — patch re-consumption of
  already-consumed sibling packages (`check-pin-drift.ps1 -Strict` flagged this as
  threshold-exceeding drift, minor gap 12; cold-GPU-kernel-hang protection propagated to all
  ONNX-backed lm-supply modules). No source changes.

## [v0.16.9] - 2026-09-04

### Changed
- `McpServer`'s `--http`/`--sse` mode now prints an explicit startup warning: this mode has no
  authentication anywhere in its request path and binds to localhost only, so any other local
  process or user account can call its tools unauthenticated. Not a behavior change - the gap
  itself is tracked as long-term debt (propose-only, gated on an actual shared/multi-tenant
  consumer appearing).

## [v0.16.8] - 2026-09-01

### Changed
- Re-pinned `LMSupply.Embedder`/`.Generator` (`0.42.2`→`0.42.10`) to their latest patch/minor.
  Already-consumed sibling, not a new dependency surface. No source changes.

## [v0.16.7] - 2026-08-29

### Fixed
- Removed `QueryExpander`'s internal `ContextExpansionMap` — a phrase-to-associated-terms table
  (`"team members"` -> `Mike, Sarah`, `"tech stack"` -> `React, Node, MongoDB, GraphQL`, etc.)
  whose entries turned out to be a test fixture's demo vocabulary, hardcoded into the shipped SDK.
  This kind of phrase-level context association is inherently user-specific (a consumer's actual
  team members, tech stack, or pets) and cannot be generalized into a static table the way
  `SynonymMap`'s general-English word synonyms can — no replacement mapping is added. Not a
  public API change (the field was private); `ExpandQuery`/`GenerateQueryVariants` keep their
  existing signatures and word-level synonym behavior unchanged. Verified no regression against
  `QueryExpanderTests` and the integration quality-comparison benchmark.

## [v0.16.6] - 2026-08-28

### Changed
- Updated `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` to 2.2.0 (previously 1.3.0).
  No public API changes — verified no usage of the capabilities the 2.0 protocol revision
  deprecated (roots, sampling, logging) or `DiscoverResult.ServerInfo`. This is the only package
  in the ecosystem running the MCP HTTP server transport, whose `Stateless` default flips to
  `true` in 2.0; live-tested the HTTP endpoint post-update (initialize/tools-list/tools-call
  round-trip) with no regression — the transport carries no session-affine state here.
- Removed the `NU5104` suppression — it existed because `ModelContextProtocol` had no stable
  release; 2.2.0 is stable, so the suppression no longer applies to anything.

## [v0.16.5] - 2026-08-24

### Changed
- Bumped `LMSupply.Embedder`/`LMSupply.Generator` 0.42.1 → 0.42.2 (dependency freshness, no known
  breaking changes consumed).

## [v0.16.4] - 2026-08-24

### Fixed
- `QueryExpander.GenerateQueryVariants` no longer expands `"code"` via `SynonymMap`. Every
  configured synonym (`program`/`script`/`implementation`/`software`) corrupted the compound noun
  "code review(s)" when substituted in — e.g. `"When are code reviews?"` became `"When are program
  reviews?"`, which no longer semantically matches stored memories about code reviews. Confirmed
  deterministic (5/5 identical repeat runs) and isolated to this single mapping — no other query in
  this package's own test corpus references "code", so removing it carries no observed recall
  cost elsewhere. `ExpandQuery` (additive term expansion, not destructive substitution) is
  similarly unaffected by other entries and needed no change.

## [v0.16.3] - 2026-08-23

### Changed
- `LMSupply.Embedder`/`.Generator` dependencies raised `0.34.17` → `0.42.1` (dependency-freshness
  gate, no MemoryIndexer API surface change).

## [v0.16.2] - 2026-08-07

### Fixed
- `RunStdioServer`/`RunHttpServer` (`tools/McpServer`) now anchor the Generic Host content root to
  `AppContext.BaseDirectory` instead of the process's ambient working directory. MCP clients launch
  this executable with an arbitrary cwd (not necessarily its own directory), which previously made
  `appsettings.json`/`appsettings.Production.json` silently go unfound and fall back to the
  hardcoded `EmbeddingProvider.Ollama`/`CompletionProvider.Mock` class defaults regardless of what
  the bundled config declared.
- `tools/McpServer/appsettings.json`'s `Embedding.Provider` was `"Local"`, a value the
  `EmbeddingProvider` enum has not had since the 0.16.0 provider-architecture change — the server
  crashed with `FormatException` on every `Development`-environment start. Corrected to `"Mock"`.
  Fixed the same stale value in `samples/appsettings.mcp-server.json`,
  `samples/appsettings.sdk-embedded.json`.
- `claude_desktop_config.example.json` and the README's "As MCP Server" section pointed to a
  nonexistent project path / an unpublished `MemoryIndexer.Mcp` dotnet-tool package. Replaced with
  the build-from-source steps that actually work today.
- `serverInfo.Version` (stdio + HTTP transports) and the HTTP `/` info endpoint's `version` field
  were hardcoded literals (`"0.1.0"`/`"0.3.0"`) that had drifted from the package version since
  introduction. Now read from assembly metadata.

### Added
- `MockEmbeddingService` and `MockTextCompletionService` log an explicit `Warning` on construction
  noting that their output is non-semantic/placeholder and unsuitable for production, so a Mock
  provider silently active in a real deployment is now observable instead of a surprise.
- The `NotSupportedException` thrown for an unimplemented `Embedding`/`Completion` provider now
  names the fix (register a real service, or set the provider to `Mock` for local development)
  instead of only stating the requirement.

### Removed
- `samples/WebApiClient` — not wired into `MemoryIndexer.slnx`, referenced from nowhere else in the
  repo, failed to restore (`NU1903` on a vulnerable transitive `Microsoft.OpenApi`), and its
  `Program.cs` referenced `EmbeddingProvider.Local`, which does not compile against the current
  enum. Dead since the 0.16.0 provider-architecture change.

## [v0.16.1]

**Gap notice — not a release entry.** Releases from v0.7.0 through v0.16.1 were published without
being recorded here. Reconstructing them now would mean describing changes from memory rather than
from a record, so the gap is marked instead of filled: for anything in that range, read the commit
history. Entries resume from the next release.

## [v0.6.0] - 2026-01-09

### Production Readiness Release

This release focuses on production-ready infrastructure: comprehensive observability, backup/restore capabilities, resource management, and a simplified provider architecture.

#### Highlights
- **Observability**: Full OpenTelemetry integration with distributed tracing and semantic conventions
- **Backup/Restore**: Export/import memories with checksum verification and conflict resolution
- **Resource Management**: Per-user/tenant limits with enforcement and usage tracking
- **Provider Architecture**: Simplified to Local (LMSupply) and Mock providers; interface-based design for external implementations

#### Provider Architecture Changes (Breaking)
- **Removed**: Built-in Ollama/OpenAI implementations
- **Added**: `LocalEmbeddingService` (LMSupply.Embedder), `LocalTextCompletionService` (LMSupply.Generator)
- **Interface-based design**: Register your own `IEmbeddingService`/`ITextCompletionService` for external LLMs

#### New MCP Tools
- `ExportMemories`, `ImportMemories`, `GetBackupStats` - Backup/restore operations
- `GetUsage`, `GetLimits`, `CanStore`, `CanStoreBatch`, `GetTenantUsage`, `GetGlobalSummary`, `RefreshUsage` - Resource management

See preview releases below for detailed changes.

**Tests**: 1148 passing (237 core + 911 SDK)

---

## [v0.6.0-preview.5] - 2026-01-09

### LocalTextCompletionService

Adds local text generation support using LMSupply.Generator, enabling fully offline LLM inference without external API dependencies.

#### New Features
- **LocalTextCompletionService**: ONNX-based text generation using LMSupply.Generator
  - Supports Phi-4, Phi-3.5, Llama 3.2, Qwen 2.5, Gemma 2 models
  - Automatic model download and caching from HuggingFace
  - GPU acceleration support (CUDA, DirectML, CoreML)
  - Lazy model loading with thread-safe initialization
- **CompletionProvider.Local**: New enum value for local completion
- **Completion Telemetry**: `CompletionOperations` counter, `CompletionLatency` histogram

#### Configuration
```json
{
  "MemoryIndexer": {
    "Completion": {
      "Provider": "Local",
      "Model": "microsoft/Phi-4-mini-instruct-onnx"
    }
  }
}
```

#### Supported Models
| Model | Parameters | Context | License |
|-------|------------|---------|---------|
| Phi-4-mini | 3.8B | 16K | MIT |
| Phi-4 | 14B | 16K | MIT |
| Phi-3.5-mini | 3.8B | 128K | MIT |
| Llama-3.2-1B | 1B | 128K | Llama |
| Llama-3.2-3B | 3B | 128K | Llama |
| Qwen2.5-3B | 3B | 128K | Apache 2.0 |

**Tests**: All 1148 tests passing (237 core + 911 SDK)

---

## [v0.6.0-preview.4] - 2026-01-09

### Provider Architecture Simplification

This preview removes built-in Ollama/OpenAI implementations in favor of interface-based design. Memory Indexer now focuses on memory management while delegating LLM/embedding concerns to external implementations.

#### Breaking Changes
- **Removed**: `OllamaEmbeddingService`, `OpenAIEmbeddingService` - Use external implementations or `LocalEmbeddingService` (LMSupply)
- **Removed**: `OllamaCompletionService`, `OpenAICompletionService` - Register your own `ITextCompletionService`
- **Removed**: `GpuStackEmbeddingTests` - Integration tests for removed services
- **Default Changed**: `CompletionOptions.Provider` now defaults to `Mock` instead of `Ollama`

#### Built-in Providers
- **Embedding**: `LocalEmbeddingService` (LMSupply), `MockEmbeddingService`
- **Completion**: `MockTextCompletionService` (new)

#### Interface-Based Design
For production use with external LLMs, register your own implementation before calling `AddMemoryIndexer()`:
```csharp
services.AddSingleton<IEmbeddingService, YourEmbeddingService>();
services.AddSingleton<ITextCompletionService, YourCompletionService>();
services.AddMemoryIndexer();
```

#### Future Package Structure (v0.7.0)
- `MemoryIndexer` - Core interfaces
- `MemoryIndexer.Sdk` - InMemory, SQLite, LMSupply
- `MemoryIndexer.Redis/PostgreSQL/Qdrant` - Storage backends
- `MemoryIndexer.Stack` - Full bundle

**Tests**: All 1150 tests passing (237 core + 913 SDK, -4 removed integration tests)

---

## [v0.6.0-preview.3] - 2026-01-09

### Resource Management

This preview adds comprehensive resource limit enforcement and usage tracking for multi-tenant deployments.

#### IResourceLimitEnforcer Interface (`IResourceLimitEnforcer.cs`)
- **Enforcement Methods**: `CanStoreAsync`, `CanStoreBatchAsync`
- **Query Methods**: `GetLimits`, `GetUsageAsync`
- **EnforcementResult**: IsAllowed, DenialReason, ExceededLimit, CurrentUsage, Limits
- **ResourceLimits**: MaxMemories, MaxStorageBytes, EnforcementEnabled, WarningThresholdPercent, Source
- **LimitType**: MemoryCount, StorageSize enum for exceeded limit identification

#### IUsageTracker Interface (`IUsageTracker.cs`)
- **Recording**: `RecordStore`, `RecordDelete`, `RecordTierPromotion`
- **Queries**: `GetUsage`, `GetTenantUsage`, `GetGlobalSummary`, `GetTrackedUsers`
- **Maintenance**: `RefreshFromStoreAsync`, `ClearUser`
- **ResourceUsage**: UserId, TenantId, MemoryCount, StorageSizeBytes, ByTier, ByType, CalculatedAt

#### InMemoryUsageTracker Implementation (`InMemoryUsageTracker.cs`)
- Thread-safe tracking with `ConcurrentDictionary` and `Interlocked` operations
- Per-user breakdown by Tier and MemoryType
- Tenant-level aggregation with user breakdown
- Global summary with top users by count and storage
- Automatic refresh from memory store

#### ResourceLimitEnforcer Implementation (`ResourceLimitEnforcer.cs`)
- Configuration-based limits via `ResourceLimitOptions`
- Tenant-specific limit overrides via `ITenantContext`
- OpenTelemetry telemetry for enforcement events and warnings
- Warning threshold detection (default 80%)

#### Configuration Options (`ResourceLimitOptions`)
- `MaxMemoriesPerUser`: Default 100,000
- `MaxStorageBytesPerUser`: Default 1GB
- `EnforcementEnabled`: Toggle for enforcement
- `WarningThresholdPercent`: Alert threshold (80%)

#### ResourceManagementTools MCP (`ResourceManagementTools.cs`)
- `GetUsage`: Get current resource usage statistics
- `GetLimits`: Get applicable resource limits
- `CanStore`: Check if store operation allowed
- `CanStoreBatch`: Check if batch operation allowed
- `GetTenantUsage`: Get tenant-level aggregation
- `GetGlobalSummary`: Get global usage statistics
- `RefreshUsage`: Force refresh from store

#### MCP MemoryTools Integration
- Pre-store enforcement check in `StoreMemory`
- Usage recording on successful store/delete
- Denial response with clear reason messaging

**Tests**: 43 new tests (ResourceLimitEnforcerTests: 19, InMemoryUsageTrackerTests: 24)
**Total Tests**: All 1154 tests passing (237 core + 917 SDK)

---

## [v0.6.0-preview.2] - 2026-01-09

### Memory Export/Import (Backup/Restore)

This preview adds complete backup and restore capabilities via JSON export/import.

#### IMemoryExporter Interface (`IMemoryExporter.cs`)
- **Export Operations**: `ExportAsync`, `ExportToStreamAsync`
- **Import Operations**: `ImportAsync`, `ImportFromStreamAsync`
- **ExportOptions**: UserId, SessionId, Since/Until filters, Tiers, Types, IncludeEmbeddings, IncludeMetadata
- **ImportOptions**: ConflictResolution, PreserveIds, ValidateChecksum, DryRun
- **ImportConflictResolution**: Skip, Replace, KeepNewer, KeepHigherConfidence, Fail

#### JsonMemoryExporter Implementation (`JsonMemoryExporter.cs`)
- JSON-based serialization with camelCase naming
- SHA256 checksum for data integrity verification
- Comprehensive conflict resolution strategies
- Activity tracing integration via `MemoryIndexerTelemetry`
- Statistics tracking: ByTier, ByType, UniqueUsers, EmbeddingsIncluded

#### BackupRestoreTools MCP (`BackupRestoreTools.cs`)
- `ExportMemories`: Export memories to JSON with filtering options
- `ImportMemories`: Import memories from JSON with conflict resolution
- `GetBackupStats`: Get export statistics without performing full export

#### InMemoryMemoryStore Enhancement
- Preserves explicitly set `CreatedAt`/`UpdatedAt` timestamps (only sets defaults when values are `default`)
- Enables accurate timestamp-based filtering for incremental backups

**Tests**: 11 new tests covering export, import, filtering, conflict resolution, streaming

---

## [v0.6.0-preview.1] - 2026-01-09

### OpenTelemetry Distributed Tracing

This preview adds comprehensive distributed tracing across all memory operations.

#### Activity Source Integration (`MemoryIndexerTelemetry`)
- **Source Name**: `MemoryIndexer` with version tracking
- **Store Operations**: `memory_indexer.store`, `memory_indexer.store_batch`
- **Recall Operations**: `memory_indexer.recall`, `memory_indexer.recall_advanced`
- **Update Operations**: `memory_indexer.update`, `memory_indexer.delete`
- **VCM Operations**: `memory_indexer.vcm_store`, `memory_indexer.vcm_recall`, `memory_indexer.vcm_sync`
- **Intelligence Operations**: `memory_indexer.classify`, `memory_indexer.summarize`, `memory_indexer.rerank`

#### Activity Tags (OpenTelemetry Semantic Conventions)
- `user.id`, `session.id`: User and session context
- `memory.type`, `memory.tier`, `memory.scope`: 3-axis model dimensions
- `memory.count`, `result.count`: Operation metrics
- `db.operation`, `db.system`: Database conventions
- Error tracking: `otel.status_code`, `exception.type`, `exception.message`

#### Instrumented Services
- `InstrumentedMemoryPrimitives`: Wraps IMemoryPrimitives with Activity spans
- `InstrumentedVCM`: Wraps IVirtualContextManager with Activity spans
- All intelligence services emit Activities for tracing

**Tests**: All 1111 tests passing (237 core + 874 SDK)

---

## [v0.5.0] - 2026-01-09

### Intelligence Integration Release

This release completes the v0.5.0 Intelligence Integration phase, exposing existing SDK intelligence features via MCP tools for LLM consumption with comprehensive documentation and testing.

#### Documentation: Advanced Intelligence Features (`docs/INTELLIGENCE.md`)
- **Conflict Resolution**: Contradiction detection and resolution strategies
  - `DetectContradiction`, `ResolveContradiction`, `AutoResolveContradiction`, `GetResolutionStrategy`
  - Configurable thresholds and semantic/rule-based hybrid detection
- **Adaptive Retrieval**: Intent-based query routing and tiered memory access
  - `ClassifyQueryIntent`, `AdaptiveRecall`, `TieredRecall`, `GetRetrievalRecommendation`
  - Query intent types: Factual, Contextual, Temporal, Relational, General
- **Graph Traversal**: Entity-based memory navigation and community detection
  - `DetectCommunities`, `ComputeImportance`, `GetTopEntities`, `FindRelatedMemories`, `ExtractSubgraph`
  - PageRank importance propagation, Label propagation community detection
- **Efficiency Features**: Token budget monitoring, recall pattern analysis, configuration validation
- **OpenTelemetry Metrics**: Complete observability integration

#### Integration Tests (`IntelligenceIntegrationTests.cs`)
- 17 tests covering complete intelligence pipeline
- Configuration validation (valid, invalid, Baddeley warnings)
- Token budget monitoring (sessions, thresholds, recommendations)
- Recall pattern analysis (duplicates, recommendations)
- Query intent classification (Factual, Contextual, Temporal)
- Conflict resolution workflow (detection, strategy, resolution)
- Full pipeline integration test

#### DI Registration Fix
- Registered `IQueryIntentClassifier` → `LocalQueryIntentClassifier` in `ServiceCollectionExtensions`
- Enables query intent classification via dependency injection

**Tests**: All 1100 tests passing (237 core + 863 SDK)

---

## [v0.5.0-preview.2] - 2026-01-09

### Production Polish Phase

This preview adds configuration validation, token budget awareness, and complete OTel intelligence metrics.

#### Token Budget Awareness Hooks (`ITokenBudgetMonitor`)
- Session-level token tracking with configurable warning thresholds
- Events: `OnBudgetWarning`, `OnBudgetExceeded`, `OnSessionEnded`
- Token estimation (~4 chars/token approximation)
- Recommendation system: Continue → ReduceScope → Compress → Conserve → Stop
- Operation breakdown tracking for analysis
- Global stats aggregation across sessions
- 16 tests covering all functionality

#### Configuration Validation (`IConfigurationValidator`)
- Validates all `MemoryIndexerOptions` sections at startup
- Returns structured errors and warnings
- Validates thresholds (0-1 ranges), positive values, required fields
- Cross-field constraints (MaxLimit >= DefaultLimit)
- Cognitive model warnings (Baddeley's 7±2 capacity)
- Type distribution sum validation
- API key warnings for cloud providers
- 21 tests covering comprehensive validation scenarios

#### Complete OpenTelemetry Intelligence Metrics
- **Counters**: Classifications, Summarizations, Deduplications, Conflict detections, Entity extractions, Rerankings, Tier promotions, Token budget warnings/exceeded, Graph queries
- **Histograms**: Classification, Summarization, Deduplication, Reranking, Graph query latency, Token budget usage ratio
- Helper methods for all intelligence operations with appropriate tags

**Tests**: All 1085 tests passing (237 core + 848 SDK)

---

## [v0.5.0-preview.1] - 2026-01-09

### Intelligence Integration Preview

This preview release exposes existing SDK intelligence features via MCP tools for LLM consumption.

#### New MCP Tools

**Graph Traversal Tools** (`GraphTraversalTools.cs`):
- `DetectCommunities` - Detect memory clusters using label propagation algorithm
- `GetCommunityMemories` - Get all memories in a specific community
- `GetCommunitySummary` - Get topic labels and key entities for a community
- `ComputeImportance` - Run PageRank to compute entity importance scores
- `GetEntityImportance` - Get importance score for a specific entity
- `GetTopEntities` - Get ranked list of most important entities
- `FindRelatedMemories` - Find memories related through shared entities
- `ExtractSubgraph` - Extract focused subgraph around specific memories

**Conflict Resolution Tools** (`ConflictResolutionTools.cs`):
- `DetectContradiction` - Detect if new content contradicts existing memories
- `ResolveContradiction` - Resolve contradiction between new content and existing memory
- `AutoResolveContradiction` - Automatically detect and resolve contradictions
- `GetResolutionStrategy` - Get recommendation for handling contradiction types

**Adaptive Retrieval Tools** (`AdaptiveRetrievalTools.cs`):
- `ClassifyQueryIntent` - Classify query intent for optimal retrieval strategy
- `AdaptiveRecall` - Smart retrieval with auto-selected strategy based on intent
- `TieredRecall` - Retrieve from specific tiers with custom priority order
- `GetRetrievalRecommendation` - Get recommendations for information type retrieval

#### Efficiency Improvements

**Session-level Recall Caching** (`OptimizedRecallService`):
- Query result caching with SHA256 cache keys for collision resistance
- TTL controlled via `LatencyOptions.QueryCacheTtlMinutes` (default: 10 min)
- `RecallCacheStatistics` for monitoring: hits, misses, duplicates, hit ratio
- Eliminates redundant embedding generation and vector search operations

**Recall Pattern Telemetry** (`RecallPatternAnalyzer`):
- Duplicate query detection with per-user tracking
- Rapid-fire recall pattern detection (configurable threshold)
- Per-user and global statistics (`RecallPatternStatistics`)
- Alert generation for problematic patterns (`RecallPatternAlert`)
- Optimization recommendations: caching, batching, query consolidation

**OpenTelemetry Metrics** (`MemoryIndexerTelemetry`):
- `memory_indexer.query_cache_hits` - Query result cache hits
- `memory_indexer.duplicate_recalls` - Duplicate recall queries detected
- `memory_indexer.rapid_fire_recalls` - Rapid-fire patterns detected
- Helper methods: `RecordQueryCacheHit`, `RecordDuplicateRecall`, `RecordRapidFireRecall`

#### Technical Details
- All tools use existing SDK intelligence services (no new implementations)
- GraphTraversalTools uses `IMemoryGraphService`, `IImportancePropagator`, and `ICommunityDetector`
- ConflictResolutionTools uses `IContradictionDetector` and `IContradictionResolver`
- AdaptiveRetrievalTools uses `IQueryIntentClassifier` and `TieredMemoryRetriever`
- Tests: All 1048 tests passing (216 core + 832 SDK)

#### Lessons Learned (TwentyQuestionsGame Evaluation)

**Validated Strengths:**
- Recall latency ~5ms (sufficient for real-time conversation)
- Core memory similarity 0.95 (critical information preserved)
- Cognitive compliance (7±2 rule) working correctly
- 21-minute session with zero errors

**Identified Improvements (added to v0.5.0 roadmap):**
- Session-level recall caching needed (LLM made 3x identical queries per turn)
- Recall pattern telemetry for detecting inefficient usage
- Token budget awareness hooks for resource monitoring

---

## [v0.4.0] - 2026-01-09

### Cognitive Architecture Completion

This release completes the 3-Axis Cognitive Memory Architecture (Type × Scope × Tier) with full tier promotion pipeline and cognitive compliance validation.

#### Phase 60: Test Code Warning Fixes
- **Fixed**: CS0219, CS8602, CS8625, xUnit2002, xUnit1026, xUnit2013 warnings in tests
- **Scope**: 5 test files across MemoryIndexer.Tests and MemoryIndexer.Sdk.Tests
- **Pattern**: Null checks for Metadata, proper xUnit assertion usage
- **Tests**: All 1015 tests passing (216 core + 799 SDK)

#### Phase 59: Benchmarks and Documentation
- **Added**: `benchmarks/run_bench.ps1` PowerShell script for automated benchmarks
- **Added**: `docs/BENCHMARKS.md` with detailed performance measurements
- **Updated**: README.md simplified with core architecture and quick start
- **Performance**: Store ~2.2μs, Recall ~1.5μs, Vector search ~812ns

#### Phase 58: Null Reference Warning Fixes
- **Fixed**: 19 CS8602/CS8603/CS8604 nullable warnings in source code
- **Scope**: MemoryPrimitivesService, MemoryService, AdvancedMemoryTools, MemoryTools
- **Pattern**: `??=` initialization for Metadata, `?? []` coalescing for collections
- **Files**: Core services and MCP tools

#### Phase 56: Per-User Cognitive Compliance Fix
- **Fixed**: Cognitive compliance check now evaluates per-user instead of globally
- **Root Cause**: Compliance summed Short tier across all users; enforcement was per-user
- **Impact**: Baddeley's 7±2 model correctly applies to each user (mind) independently
- **Files**: `samples/TwentyQuestionsGame/Benchmark/BenchmarkResult.cs`, `GameRunner.cs`

#### Phase 55: Deduplication→Confirmation Integration
- **Feature**: Duplicate detection now auto-confirms memories
- **Mechanism**: Repeated mention of facts triggers implicit confirmation
- **Files**: `MemoryPrimitivesService.cs`, `IDeduplicationService.cs`

#### Phase 53: Memory Confirmation Primitive
- **Feature**: New `memory_confirm` MCP tool for explicit confirmation
- **Purpose**: Enables Archive tier promotion eligibility (AND logic)
- **API**: `ConfirmAsync(ConfirmRequest request)`
- **Files**: `IMemoryPrimitives.cs`, `MemoryPrimitivesService.cs`, `MemoryTools.cs`

#### Phase 52: Long→Archive Promotion Pipeline
- **Feature**: Complete tier promotion from Long (T2) to Archive (T3)
- **Logic**: AND requirements - Confidence ≥ 0.8 AND ConfirmCount ≥ 3
- **Service**: `ILongTermPromoter` / `LongTermPromoterService`
- **Files**: `MemoryPromotionBackgroundService.cs`

#### Phase 51: Working Memory Capacity Enforcement
- **Feature**: Baddeley's 7±2 capacity limit for Short tier
- **Behavior**: Auto-promotes oldest items when capacity exceeded
- **Config**: `WorkingMemoryOptions.Capacity` (default: 9)
- **Files**: `MemoryPrimitivesService.cs`, `MemoryIndexerOptions.cs`

#### Phase 50: Cognitive Compliance Metrics Revision
- **Change**: Revised compliance checks aligned with cognitive science
- **Metrics**: WorkingMemory(7±2), HealthyTierFlow
- **Files**: `samples/TwentyQuestionsGame/Game/GameRunner.cs`

#### Phase 49: Cognitive-Aware Tier Selection
- **Feature**: Content-based tier assignment in game sample
- **Logic**: Game rules → Short, Q&A history → Long
- **Files**: `samples/TwentyQuestionsGame/ToolCall/ToolCallExecutor.cs`

#### Phase 48: Duplicate Question Bug Fix
- **Fixed**: Beta agent asking semantically duplicate questions
- **Solution**: Enhanced reasoning chain in BetaSystemPrompt
- **Files**: `samples/TwentyQuestionsGame/Prompts/BetaSystemPrompt.md`

### Architecture Highlights

**3-Axis Memory Model** (Type × Scope × Tier):
- **Type**: Episodic, Semantic, Procedural, Fact, Reflection (Tulving)
- **Scope**: Turn, Topic, Session, User (temporal reach)
- **Tier**: Buffer, Short, Long, Archive (Atkinson-Shiffrin + Baddeley)

```
Tier Promotion Pipeline:
┌─────────────────────────────────────────────────────────┐
│  Buffer (T0) - Sensory Store (Atkinson-Shiffrin)        │
│  TTL: 60s idle OR 500 tokens OR 3 turns                 │
├─────────────────────────────────────────────────────────┤
│  Short (T1) - Working Memory (Baddeley's 7±2)           │
│  Capacity: 9 items, auto-promote to Long when exceeded  │
├─────────────────────────────────────────────────────────┤
│  Long (T2) - Episodic Memory (Tulving)                  │
│  Session-level events and experiences                   │
├─────────────────────────────────────────────────────────┤
│  Archive (T3) - Semantic Memory (Tulving)               │
│  Promotion: Confidence ≥ 0.8 AND Confirms ≥ 3           │
└─────────────────────────────────────────────────────────┘
```

### Breaking Changes

None in this release.

### Dependencies

- .NET 10.0
- ModelContextProtocol 0.5.0-preview.1
- Microsoft.Extensions.VectorData.Abstractions 9.7.0
- LMSupply 0.8.10
- OpenAI 2.8.0
- Swashbuckle.AspNetCore 10.1.0

---

## [v0.3.0] - Previous Release

See git history for earlier changes.
