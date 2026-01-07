using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;

namespace MemoryIndexer.Configuration;

/// <summary>
/// Root configuration options for Memory Indexer.
/// </summary>
public sealed class MemoryIndexerOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "MemoryIndexer";

    /// <summary>
    /// Storage configuration.
    /// </summary>
    public StorageOptions Storage { get; set; } = new();

    /// <summary>
    /// Embedding service configuration.
    /// </summary>
    public EmbeddingOptions Embedding { get; set; } = new();

    /// <summary>
    /// Text completion service configuration.
    /// Phase 25: Semantic Knowledge Extraction.
    /// </summary>
    public CompletionOptions Completion { get; set; } = new();

    /// <summary>
    /// Scoring configuration.
    /// </summary>
    public ScoringOptions Scoring { get; set; } = new();

    /// <summary>
    /// Search configuration.
    /// </summary>
    public SearchOptions Search { get; set; } = new();

    /// <summary>
    /// Security configuration.
    /// </summary>
    public SecurityOptions Security { get; set; } = new();

    /// <summary>
    /// Multi-tenant configuration.
    /// </summary>
    public MultiTenantOptions MultiTenant { get; set; } = new();

    /// <summary>
    /// Intelligence services configuration.
    /// </summary>
    public IntelligenceOptions Intelligence { get; set; } = new();

    /// <summary>
    /// Recently buffer (Tier 0) configuration.
    /// </summary>
    public RecentlyBufferOptions RecentlyBuffer { get; set; } = new();

    /// <summary>
    /// Deduplication configuration.
    /// Phase 21: Smart Deduplication & Quality Control.
    /// </summary>
    public DeduplicationOptions Deduplication { get; set; } = new();

    /// <summary>
    /// Memory growth control configuration.
    /// Phase 22.1: Memory Growth Rate Control.
    /// </summary>
    public MemoryGrowthOptions MemoryGrowth { get; set; } = new();

    /// <summary>
    /// Latency optimization configuration.
    /// Phase 22.2: Recall Latency Optimization.
    /// </summary>
    public LatencyOptions Latency { get; set; } = new();

    /// <summary>
    /// Memory type balancing configuration.
    /// Phase 23.1: Memory Type Distribution Balancing.
    /// </summary>
    public TypeBalancerOptions TypeBalancing { get; set; } = new();
}

/// <summary>
/// Security configuration options.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// Whether PII detection is enabled.
    /// </summary>
    public bool EnablePiiDetection { get; set; } = true;

    /// <summary>
    /// Minimum confidence for PII detection.
    /// </summary>
    public float PiiMinConfidence { get; set; } = 0.5f;

    /// <summary>
    /// Whether prompt injection detection is enabled.
    /// </summary>
    public bool EnableInjectionDetection { get; set; } = true;

    /// <summary>
    /// Maximum allowed risk level for inputs.
    /// </summary>
    public int MaxAllowedRiskLevel { get; set; } = 1; // Low

    /// <summary>
    /// Whether rate limiting is enabled.
    /// </summary>
    public bool EnableRateLimiting { get; set; } = true;

    /// <summary>
    /// Permits per minute for store operations.
    /// </summary>
    public int StorePermitsPerMinute { get; set; } = 60;

    /// <summary>
    /// Permits per minute for recall operations.
    /// </summary>
    public int RecallPermitsPerMinute { get; set; } = 100;

    /// <summary>
    /// Global permits per minute.
    /// </summary>
    public int GlobalPermitsPerMinute { get; set; } = 200;

    /// <summary>
    /// Whether audit logging is enabled.
    /// </summary>
    public bool EnableAuditLogging { get; set; } = true;

    /// <summary>
    /// Whether memory lineage tracking is enabled.
    /// </summary>
    public bool EnableLineageTracking { get; set; } = true;
}

/// <summary>
/// Multi-tenant configuration options.
/// </summary>
public sealed class MultiTenantOptions
{
    /// <summary>
    /// Whether multi-tenant mode is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Header name for tenant identification.
    /// </summary>
    public string TenantHeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    /// Whether to enforce tenant isolation.
    /// </summary>
    public bool EnforceIsolation { get; set; } = true;

    /// <summary>
    /// Default tenant ID when none is specified.
    /// </summary>
    public string? DefaultTenantId { get; set; }

    /// <summary>
    /// Whether to use per-tenant encryption.
    /// </summary>
    public bool EnablePerTenantEncryption { get; set; }
}

/// <summary>
/// Storage configuration options.
/// </summary>
public sealed class StorageOptions
{
    /// <summary>
    /// Storage provider type.
    /// </summary>
    public StorageType Type { get; set; } = StorageType.InMemory;

    /// <summary>
    /// Connection string for the storage provider.
    /// For SQLite: file path (e.g., "memory.db")
    /// For Qdrant: endpoint URL (e.g., "http://localhost:6334")
    /// </summary>
    public string ConnectionString { get; set; } = "memory.db";

    /// <summary>
    /// Collection/table name for memories.
    /// </summary>
    public string CollectionName { get; set; } = "memories";

    /// <summary>
    /// Vector dimensions for storage.
    /// </summary>
    public int VectorDimensions { get; set; } = 768;

    /// <summary>
    /// Qdrant-specific configuration options.
    /// </summary>
    public QdrantOptions Qdrant { get; set; } = new();

    /// <summary>
    /// SQLite-specific configuration options.
    /// </summary>
    public SqliteOptions Sqlite { get; set; } = new();
}

/// <summary>
/// SQLite-specific configuration options.
/// </summary>
public sealed class SqliteOptions
{
    /// <summary>
    /// Enable WAL (Write-Ahead Logging) mode for better concurrency.
    /// </summary>
    public bool UseWalMode { get; set; } = true;

    /// <summary>
    /// FTS5 tokenizer for full-text search.
    /// Options: "trigram" (best for CJK/multilingual), "unicode61", "porter" (English stemming)
    /// </summary>
    public string FtsTokenizer { get; set; } = "trigram";

    /// <summary>
    /// SQLite cache size in KB. Default: 2000 (2MB).
    /// </summary>
    public int CacheSizeKb { get; set; } = 2000;

    /// <summary>
    /// Enable full-text search using FTS5.
    /// </summary>
    public bool EnableFullTextSearch { get; set; } = true;

    /// <summary>
    /// Busy timeout in milliseconds. How long to wait when database is locked.
    /// </summary>
    public int BusyTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// HNSW index M parameter (graph connectivity).
    /// Higher values = better recall, more memory.
    /// </summary>
    public int HnswM { get; set; } = 16;

    /// <summary>
    /// HNSW index efConstruction parameter.
    /// Higher values = better index quality, slower indexing.
    /// </summary>
    public int HnswEfConstruction { get; set; } = 128;

    /// <summary>
    /// HNSW search ef parameter.
    /// Higher values = better recall, slower search.
    /// </summary>
    public int HnswEfSearch { get; set; } = 64;

    // ===== Zero-Config Auto-Management Settings =====

    /// <summary>
    /// Auto-VACUUM mode. Default: INCREMENTAL for automatic space reclamation.
    /// </summary>
    public SqliteAutoVacuumMode AutoVacuum { get; set; } = SqliteAutoVacuumMode.Incremental;

    /// <summary>
    /// Enable automatic background maintenance (optimize, checkpoint, cleanup).
    /// </summary>
    public bool EnableAutoMaintenance { get; set; } = true;

    /// <summary>
    /// Interval for automatic maintenance tasks (minutes). Default: 30 minutes.
    /// </summary>
    public int MaintenanceIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum database size in MB. When exceeded, old memories are automatically deleted.
    /// Set to 0 for unlimited. Default: 500MB.
    /// </summary>
    public long MaxDatabaseSizeMb { get; set; } = 500;

    /// <summary>
    /// Automatically delete memories older than this many days.
    /// Set to 0 to disable. Default: 90 days.
    /// </summary>
    public int AutoCleanupOldMemoriesDays { get; set; } = 90;

    /// <summary>
    /// WAL checkpoint interval (minutes). Default: 10 minutes.
    /// </summary>
    public int CheckpointIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Number of pages to free during incremental vacuum.
    /// Higher = faster cleanup, more I/O. Default: 100 pages.
    /// </summary>
    public int IncrementalVacuumPages { get; set; } = 100;
}

/// <summary>
/// SQLite auto-vacuum modes.
/// </summary>
public enum SqliteAutoVacuumMode
{
    /// <summary>
    /// No automatic vacuum. Requires manual VACUUM command.
    /// </summary>
    None = 0,

    /// <summary>
    /// Full automatic vacuum on every transaction (slower writes).
    /// </summary>
    Full = 1,

    /// <summary>
    /// Incremental vacuum (best for zero-config). Space reclaimed gradually.
    /// </summary>
    Incremental = 2
}

/// <summary>
/// Qdrant-specific configuration options.
/// </summary>
public sealed class QdrantOptions
{
    /// <summary>
    /// API key for authentication (optional).
    /// </summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Storage provider types.
/// </summary>
public enum StorageType
{
    /// <summary>
    /// In-memory storage (for testing).
    /// </summary>
    InMemory,

    /// <summary>
    /// SQLite with vector extension.
    /// </summary>
    SqliteVec,

    /// <summary>
    /// Qdrant vector database.
    /// </summary>
    Qdrant
}

/// <summary>
/// Embedding service configuration options.
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>
    /// Embedding provider type.
    /// </summary>
    public EmbeddingProvider Provider { get; set; } = EmbeddingProvider.Ollama;

    /// <summary>
    /// Model name/ID to use for embeddings.
    /// </summary>
    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Embedding dimensions (must match model output).
    /// </summary>
    public int Dimensions { get; set; } = 768;

    /// <summary>
    /// Endpoint URL for the embedding service.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// API key (for OpenAI or other cloud providers).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Batch size for embedding generation.
    /// </summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// Cache TTL in minutes (0 = disabled).
    /// </summary>
    public int CacheTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Embedding provider types.
/// </summary>
public enum EmbeddingProvider
{
    /// <summary>
    /// Mock provider for testing (returns random embeddings).
    /// </summary>
    Mock,

    /// <summary>
    /// Ollama local inference.
    /// </summary>
    Ollama,

    /// <summary>
    /// OpenAI API.
    /// </summary>
    OpenAI,

    /// <summary>
    /// Azure OpenAI Service.
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// Custom HTTP endpoint (OpenAI-compatible).
    /// </summary>
    Custom,

    /// <summary>
    /// Local ONNX-based embedding using LocalAI.Embedder.
    /// </summary>
    Local
}

/// <summary>
/// Scoring configuration options.
/// Based on Generative Agents formula.
/// </summary>
public sealed class ScoringOptions
{
    /// <summary>
    /// Weight for recency component (α).
    /// </summary>
    public float RecencyWeight { get; set; } = 1.0f;

    /// <summary>
    /// Weight for importance component (β).
    /// </summary>
    public float ImportanceWeight { get; set; } = 1.0f;

    /// <summary>
    /// Weight for relevance component (γ).
    /// </summary>
    public float RelevanceWeight { get; set; } = 1.0f;

    /// <summary>
    /// Decay factor for recency calculation.
    /// decay_factor ^ hours_since_access
    /// 0.99 = ~3 day half-life, 0.995 = ~6 day half-life
    /// </summary>
    public float DecayFactor { get; set; } = 0.99f;

    /// <summary>
    /// Maximum expected access count for normalization.
    /// </summary>
    public int MaxExpectedAccessCount { get; set; } = 100;

    /// <summary>
    /// Recency bias mitigation factor (0.0 - 1.0).
    /// Lower values reduce recency impact, allowing older relevant memories to rank higher.
    /// 0.0 = No recency bias (recency ignored)
    /// 0.5 = Balanced (50% recency reduction)
    /// 1.0 = Full recency (default behavior)
    /// Phase 20.2: Prevents over-bias toward recent memories.
    /// </summary>
    public float RecencyBiasMitigation { get; set; } = 0.5f;
}

/// <summary>
/// Search configuration options.
/// </summary>
public sealed class SearchOptions
{
    /// <summary>
    /// Default number of results to return.
    /// </summary>
    public int DefaultLimit { get; set; } = 5;

    /// <summary>
    /// Maximum number of results allowed.
    /// </summary>
    public int MaxLimit { get; set; } = 100;

    /// <summary>
    /// Minimum similarity score threshold.
    /// </summary>
    public float MinScore { get; set; }

    /// <summary>
    /// Weight for dense (vector) search in hybrid retrieval.
    /// </summary>
    public float DenseWeight { get; set; } = 0.6f;

    /// <summary>
    /// Weight for sparse (BM25) search in hybrid retrieval.
    /// </summary>
    public float SparseWeight { get; set; } = 0.4f;

    /// <summary>
    /// MMR diversity parameter (λ).
    /// Higher = more relevance, Lower = more diversity.
    /// </summary>
    public float MmrLambda { get; set; } = 0.7f;

    /// <summary>
    /// Similarity threshold for duplicate detection.
    /// </summary>
    public float DuplicateThreshold { get; set; } = 0.80f;

    /// <summary>
    /// Number of recent memories to check for duplicates on encode.
    /// 0 = check all memories (expensive). Recommended: 20-50.
    /// </summary>
    public int DuplicateLookbackWindow { get; set; } = 20;

    /// <summary>
    /// RRF k parameter for rank fusion.
    /// </summary>
    public int RrfK { get; set; } = 60;

    /// <summary>
    /// Model ID for re-ranking. Supported: bge-reranker-base, bge-reranker-large, bge-reranker-v2-m3.
    /// </summary>
    public string? RerankerModel { get; set; }

    /// <summary>
    /// Whether to enable re-ranking for search results.
    /// </summary>
    public bool EnableReranking { get; set; } = true;

    /// <summary>
    /// Initial candidate multiplier for re-ranking.
    /// E.g., if topK=5 and multiplier=4, retrieves 20 candidates for re-ranking.
    /// </summary>
    public int RerankCandidateMultiplier { get; set; } = 4;

    /// <summary>
    /// Whether to enable HyDE (Hypothetical Document Embeddings) for complex queries.
    /// </summary>
    public bool EnableHyde { get; set; }

    /// <summary>
    /// Number of hypothetical documents to generate for HyDE ensemble.
    /// </summary>
    public int HydeDocumentCount { get; set; } = 3;

    /// <summary>
    /// Minimum query length (words) to trigger HyDE.
    /// Short queries may not benefit from hypothetical expansion.
    /// </summary>
    public int HydeMinQueryWords { get; set; } = 3;
}

/// <summary>
/// Intelligence services configuration options.
/// </summary>
public sealed class IntelligenceOptions
{
    /// <summary>
    /// Whether intelligence services are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Model ID for memory classification. Supported: phi-3-mini, Qwen2.5-1.5B, Qwen2.5-3B, Llama-3.2-1B.
    /// </summary>
    public string? ClassifierModel { get; set; }

    /// <summary>
    /// Whether automatic classification is enabled for new memories.
    /// </summary>
    public bool ClassificationEnabled { get; set; } = true;

    /// <summary>
    /// Whether to enable automatic fact extraction.
    /// </summary>
    public bool FactExtractionEnabled { get; set; } = true;

    /// <summary>
    /// Whether to enable automatic summarization.
    /// </summary>
    public bool SummarizationEnabled { get; set; } = true;

    /// <summary>
    /// Maximum tokens for generator output.
    /// </summary>
    public int MaxGeneratorTokens { get; set; } = 512;

    /// <summary>
    /// Temperature for generator output (0.0 - 1.0).
    /// Lower values produce more deterministic output.
    /// </summary>
    public float GeneratorTemperature { get; set; } = 0.1f;
}

/// <summary>
/// Recently buffer (Tier 0) configuration options.
/// Controls the async staging area before memories are promoted to Working tier.
/// </summary>
/// <remarks>
/// Multi-signal promotion triggers (OR logic):
/// - IdleTimeout: Promotes when no activity for specified duration
/// - TokenThreshold: Promotes when accumulated tokens exceed threshold
/// - TurnThreshold: Promotes when turn count exceeds threshold
/// </remarks>
public sealed class RecentlyBufferOptions
{
    /// <summary>
    /// Whether the Recently buffer is enabled.
    /// When disabled, memories go directly to Working tier.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Idle timeout before triggering promotion.
    /// Promotion occurs when no new content for this duration.
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Token threshold for triggering promotion.
    /// Promotion occurs when accumulated tokens exceed this value.
    /// Default: 500 tokens.
    /// </summary>
    public int TokenThreshold { get; set; } = 500;

    /// <summary>
    /// Turn threshold for triggering promotion.
    /// Promotion occurs when turn count exceeds this value.
    /// Default: 3 turns.
    /// </summary>
    public int TurnThreshold { get; set; } = 3;

    /// <summary>
    /// Maximum buffer size per user.
    /// When exceeded, oldest items are promoted regardless of triggers.
    /// Default: 100 items.
    /// </summary>
    public int MaxBufferSize { get; set; } = 100;

    /// <summary>
    /// Maximum total tokens per user buffer.
    /// When exceeded, oldest items are promoted.
    /// Default: 10000 tokens.
    /// </summary>
    public int MaxBufferTokens { get; set; } = 10000;

    /// <summary>
    /// Interval for checking promotion triggers.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan TriggerCheckInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether to enable async background promotion worker.
    /// When disabled, promotion only occurs on explicit flush.
    /// </summary>
    public bool EnableBackgroundWorker { get; set; } = true;
}

/// <summary>
/// Deduplication configuration options.
/// Phase 21: Smart Deduplication & Quality Control.
/// </summary>
public sealed class DeduplicationOptions
{
    /// <summary>
    /// Whether deduplication is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default similarity threshold for duplicate detection (0.0 - 1.0).
    /// Default: 0.80 (80% similarity).
    /// </summary>
    public float DefaultSimilarityThreshold { get; set; } = 0.80f;

    /// <summary>
    /// Number of recent memories to check for duplicates.
    /// 0 = check all memories (expensive). Recommended: 20-50.
    /// Default: 20.
    /// </summary>
    public int LookbackWindow { get; set; } = 20;

    /// <summary>
    /// Exact duplicate threshold (>= 0.95): Skip.
    /// Default: 0.95.
    /// </summary>
    public float ExactDuplicateThreshold { get; set; } = 0.95f;

    /// <summary>
    /// High similarity threshold (0.85-0.94): Merge.
    /// Default: 0.85.
    /// </summary>
    public float HighSimilarityThreshold { get; set; } = 0.85f;

    /// <summary>
    /// Medium similarity threshold (0.75-0.84): Update.
    /// Default: 0.75.
    /// </summary>
    public float MediumSimilarityThreshold { get; set; } = 0.75f;

    /// <summary>
    /// Low similarity threshold (0.65-0.74): AddWithRelation.
    /// Below this: Add as new memory.
    /// Default: 0.65.
    /// </summary>
    public float LowSimilarityThreshold { get; set; } = 0.65f;

    /// <summary>
    /// ContentType-aware deduplication rules.
    /// Key: NewContentType, Value: Dictionary of ExistingContentType -> DuplicateAction.
    /// Example: { "QUESTION": { "QUESTION": Skip, "CONFIRMED": AddWithRelation } }
    /// </summary>
    public Dictionary<string, Dictionary<string, DuplicateAction>>? ContentTypeRules { get; set; }
}

/// <summary>
/// Memory growth control configuration options.
/// Phase 22.1: Intelligent Memory Growth Management.
/// </summary>
/// <remarks>
/// Controls memory growth rate through:
/// - Importance-based filtering: Prevents low-value memories from storage
/// - Topic-based deduplication: Reduces redundant topic storage
/// - Dynamic thresholds: Adapts filtering based on memory pressure
/// - Growth rate monitoring: Tracks and controls memories/round metric
/// </remarks>
public sealed class MemoryGrowthOptions
{
    /// <summary>
    /// Maximum allowed growth rate (memories per round).
    /// Target: 4.0 memories/round (41% reduction from 6.8 baseline).
    /// Default: 4.0.
    /// </summary>
    public float MaxGrowthRatePerRound { get; set; } = 4.0f;

    /// <summary>
    /// Minimum importance score required for storage (0.0 - 1.0).
    /// Memories below this threshold are filtered out during storage.
    /// Default: 0.3 (30% importance threshold).
    /// </summary>
    public float MinImportanceForStorage { get; set; } = 0.3f;

    /// <summary>
    /// Whether topic-based pre-storage deduplication is enabled.
    /// Prevents duplicate topics from being stored multiple times.
    /// Default: true.
    /// </summary>
    public bool TopicBasedDedup { get; set; } = true;

    /// <summary>
    /// Whether dynamic threshold adjustment is enabled.
    /// Adapts MinImportanceForStorage based on memory pressure.
    /// Default: true.
    /// </summary>
    public bool DynamicThresholds { get; set; } = true;

    /// <summary>
    /// Threshold multiplier under low memory pressure (&lt; 50% capacity).
    /// Lower threshold = accept more memories when space available.
    /// Default: 0.8 (MinImportanceForStorage * 0.8).
    /// </summary>
    public float LowPressureThresholdMultiplier { get; set; } = 0.8f;

    /// <summary>
    /// Threshold multiplier under high memory pressure (> 80% capacity).
    /// Higher threshold = stricter filtering when space constrained.
    /// Default: 1.5 (MinImportanceForStorage * 1.5).
    /// </summary>
    public float HighPressureThresholdMultiplier { get; set; } = 1.5f;
}

/// <summary>
/// Latency optimization configuration options.
/// Phase 22.2: Recall Latency Optimization.
/// </summary>
/// <remarks>
/// Controls recall latency through:
/// - Embedding cache with LRU eviction
/// - Batch query processing for parallel searches
/// - Early termination for sufficient results
/// - Query result caching with TTL
/// - Per-tier latency budgets and timeout configuration
/// </remarks>
public sealed class LatencyOptions
{
    /// <summary>
    /// Whether latency profiling is enabled.
    /// Default: true.
    /// </summary>
    public bool ProfilingEnabled { get; set; } = true;

    /// <summary>
    /// Latency budget for Working Memory tier (hot path).
    /// Target: &lt; 100ms.
    /// Default: 100ms.
    /// </summary>
    public double WorkingMemoryBudgetMs { get; set; } = 100.0;

    /// <summary>
    /// Latency budget for Session Memory tier (warm path).
    /// Target: &lt; 300ms.
    /// Default: 300ms.
    /// </summary>
    public double SessionMemoryBudgetMs { get; set; } = 300.0;

    /// <summary>
    /// Latency budget for User Profile tier (cold path).
    /// Target: &lt; 500ms.
    /// Default: 500ms.
    /// </summary>
    public double UserProfileBudgetMs { get; set; } = 500.0;

    /// <summary>
    /// Whether embedding cache is enabled.
    /// Default: true.
    /// </summary>
    public bool EmbeddingCacheEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of cached embeddings (LRU eviction).
    /// Default: 1000.
    /// </summary>
    public int EmbeddingCacheSize { get; set; } = 1000;

    /// <summary>
    /// Embedding cache TTL in minutes.
    /// Default: 60 minutes.
    /// </summary>
    public int EmbeddingCacheTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Whether query result cache is enabled.
    /// Default: true.
    /// </summary>
    public bool QueryCacheEnabled { get; set; } = true;

    /// <summary>
    /// Maximum number of cached query results (LRU eviction).
    /// Default: 500.
    /// </summary>
    public int QueryCacheSize { get; set; } = 500;

    /// <summary>
    /// Query result cache TTL in minutes.
    /// Default: 10 minutes.
    /// </summary>
    public int QueryCacheTtlMinutes { get; set; } = 10;

    /// <summary>
    /// Whether batch query processing is enabled for parallel searches.
    /// Default: true.
    /// </summary>
    public bool BatchProcessingEnabled { get; set; } = true;

    /// <summary>
    /// Maximum batch size for parallel query processing.
    /// Default: 10.
    /// </summary>
    public int MaxBatchSize { get; set; } = 10;

    /// <summary>
    /// Whether early termination is enabled when sufficient results are found.
    /// Default: true.
    /// </summary>
    public bool EarlyTerminationEnabled { get; set; } = true;

    /// <summary>
    /// Confidence threshold for early termination.
    /// When average confidence of top results exceeds this, stop searching.
    /// Default: 0.9 (90%).
    /// </summary>
    public float EarlyTerminationConfidence { get; set; } = 0.9f;

    /// <summary>
    /// Minimum results required before early termination can trigger.
    /// Default: 3.
    /// </summary>
    public int EarlyTerminationMinResults { get; set; } = 3;
}

/// <summary>
/// Memory type balancing configuration options.
/// Phase 23.1: Memory Type Distribution Balancing.
/// </summary>
/// <remarks>
/// Controls memory type distribution through:
/// - Target distribution percentages for each memory type
/// - Boost factors for underrepresented types in retrieval
/// - Sensitivity control for adaptive weighting
/// </remarks>
public sealed class TypeBalancerOptions
{
    /// <summary>
    /// Whether type balancing is enabled.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Target distribution percentages for memory types.
    /// Values should sum to ~1.0 (100%).
    /// </summary>
    public Dictionary<MemoryType, float> TargetDistribution { get; set; } = new()
    {
        [MemoryType.Episodic] = 0.40f,    // 40% target
        [MemoryType.Semantic] = 0.30f,    // 30% target
        [MemoryType.Procedural] = 0.20f,  // 20% target
        [MemoryType.Fact] = 0.10f,        // 10% target
        [MemoryType.Reflection] = 0.0f    // Generated during consolidation, not direct classification
    };

    /// <summary>
    /// Boost sensitivity factor.
    /// Higher values = stronger correction for imbalanced types.
    /// Default: 2.0 (e.g., 15% deviation → 30% boost).
    /// </summary>
    public float BoostSensitivity { get; set; } = 2.0f;

    /// <summary>
    /// Maximum boost factor allowed.
    /// Prevents over-correction for severely underrepresented types.
    /// Default: 0.5 (50% maximum boost).
    /// </summary>
    public float MaxBoost { get; set; } = 0.5f;

    /// <summary>
    /// Minimum number of memories required before applying balancing.
    /// Prevents skewed distribution in early usage.
    /// Default: 20 memories.
    /// </summary>
    public int MinMemoriesForBalancing { get; set; } = 20;
}

/// <summary>
/// Text completion service configuration options.
/// Phase 25: Semantic Knowledge Extraction.
/// </summary>
public sealed class CompletionOptions
{
    /// <summary>
    /// Completion provider type.
    /// </summary>
    public CompletionProvider Provider { get; set; } = CompletionProvider.Ollama;

    /// <summary>
    /// Model name/ID to use for completions.
    /// </summary>
    public string Model { get; set; } = "llama3.2:1b";

    /// <summary>
    /// Endpoint URL for the completion service.
    /// </summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>
    /// API key (for OpenAI or other cloud providers).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Default temperature for generation (0.0 = deterministic, 1.0 = creative).
    /// </summary>
    public float DefaultTemperature { get; set; } = 0.1f;

    /// <summary>
    /// Default maximum tokens to generate.
    /// </summary>
    public int DefaultMaxTokens { get; set; } = 500;
}

/// <summary>
/// Completion provider types.
/// </summary>
public enum CompletionProvider
{
    /// <summary>
    /// Mock provider for testing (returns placeholder text).
    /// </summary>
    Mock,

    /// <summary>
    /// Ollama local inference.
    /// </summary>
    Ollama,

    /// <summary>
    /// OpenAI API.
    /// </summary>
    OpenAI,

    /// <summary>
    /// Azure OpenAI Service.
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// Custom HTTP endpoint (OpenAI-compatible).
    /// </summary>
    Custom
}
