namespace MemoryIndexer.Core.Configuration;

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
    /// Local ONNX-based embedding using LocalEmbedder.
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
    /// RRF k parameter for rank fusion.
    /// </summary>
    public int RrfK { get; set; } = 60;
}
