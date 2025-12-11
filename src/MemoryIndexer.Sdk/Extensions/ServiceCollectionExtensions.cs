using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Services;
using MemoryIndexer.Embedding.Providers;
using MemoryIndexer.Intelligence.Chunking;
using MemoryIndexer.Intelligence.Compression;
using MemoryIndexer.Intelligence.ContextOptimization;
using MemoryIndexer.Intelligence.Deduplication;
using MemoryIndexer.Intelligence.Evaluation;
using MemoryIndexer.Intelligence.KnowledgeGraph;
using MemoryIndexer.Intelligence.Scoring;
using MemoryIndexer.Intelligence.Search;
using MemoryIndexer.Intelligence.SelfEditing;
using MemoryIndexer.Intelligence.Security;
using MemoryIndexer.Intelligence.Security.MultiTenant;
using MemoryIndexer.Intelligence.Summarization;
using MemoryIndexer.Sdk.Observability;
using MemoryIndexer.Storage.InMemory;
using MemoryIndexer.Storage.Qdrant;
using MemoryIndexer.Storage.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// Extension methods for registering Memory Indexer services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Memory Indexer services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMemoryIndexer(
        this IServiceCollection services,
        Action<MemoryIndexerOptions>? configure = null)
    {
        // Register options
        services.AddOptions<MemoryIndexerOptions>()
            .BindConfiguration(MemoryIndexerOptions.SectionName);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // Register core services
        services.TryAddSingleton<MemoryService>();

        // Register storage based on configuration
        services.TryAddSingleton<IMemoryStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value;
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryMemoryStore>>();

            return options.Storage.Type switch
            {
                StorageType.InMemory => new InMemoryMemoryStore(logger),
                StorageType.SqliteVec => new SqliteVecMemoryStore(
                    databasePath: options.Storage.ConnectionString ?? "memories.db",
                    vectorDimensions: options.Storage.VectorDimensions > 0 ? options.Storage.VectorDimensions : options.Embedding.Dimensions,
                    options: options.Storage.Sqlite,
                    logger: sp.GetRequiredService<ILogger<SqliteVecMemoryStore>>()),
                StorageType.Qdrant => CreateQdrantStore(options, sp.GetRequiredService<ILogger<QdrantMemoryStore>>()),
                _ => new InMemoryMemoryStore(logger)
            };
        });

        services.TryAddSingleton<ISessionStore>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemorySessionStore>>();
            return new InMemorySessionStore(logger);
        });

        // Register memory cache for embedding caching
        services.TryAddSingleton<IMemoryCache, MemoryCache>();

        // Register HTTP client factory
        services.AddHttpClient();

        // Register embedding service based on configuration
        services.TryAddSingleton<IEmbeddingService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MemoryIndexerOptions>>();
            var cache = sp.GetRequiredService<IMemoryCache>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            return options.Value.Embedding.Provider switch
            {
                EmbeddingProvider.Local => new LocalEmbeddingService(
                    cache,
                    options,
                    sp.GetRequiredService<ILogger<LocalEmbeddingService>>()),
                EmbeddingProvider.Ollama => new OllamaEmbeddingService(
                    httpClientFactory.CreateClient("Ollama"),
                    cache,
                    options,
                    sp.GetRequiredService<ILogger<OllamaEmbeddingService>>()),
                EmbeddingProvider.OpenAI or EmbeddingProvider.AzureOpenAI or EmbeddingProvider.Custom =>
                    new OpenAIEmbeddingService(
                        httpClientFactory.CreateClient("OpenAI"),
                        cache,
                        options,
                        sp.GetRequiredService<ILogger<OpenAIEmbeddingService>>()),
                _ => new MockEmbeddingService(
                    options,
                    sp.GetRequiredService<ILogger<MockEmbeddingService>>())
            };
        });

        // Register scoring service
        services.TryAddSingleton<IScoringService, DefaultScoringService>();

        // Register intelligence services (Phase 2)
        services.TryAddSingleton<IHybridSearchService, HybridSearchService>();
        services.TryAddSingleton<IQueryExpander, QueryExpander>();
        services.TryAddSingleton<DuplicateDetector>();
        services.TryAddSingleton<ImportanceAnalyzer>();
        services.TryAddSingleton<TopicSegmenter>();

        // Register summarization services (Phase 3)
        services.TryAddSingleton<ISummarizationService, ExtractiveSummarizer>();

        // Register compression services (Phase 3)
        services.TryAddSingleton<IPromptCompressor, LLMLinguaCompressor>();

        // Register knowledge graph services (Phase 3)
        services.TryAddSingleton<IKnowledgeGraphService, EntityExtractor>();

        // Register self-editing memory services (Phase 3)
        services.TryAddSingleton<ISelfEditingMemoryService, MemGPTStyleMemoryManager>();

        // Register context optimization services (Phase 3)
        services.TryAddSingleton<IContextOptimizer, ContextWindowOptimizer>();

        // Register security services (Phase 4)
        services.TryAddSingleton<IPiiDetector, RegexPiiDetector>();
        services.TryAddSingleton<IPromptInjectionDetector, PromptInjectionDetector>();

        // Register rate limiting (Phase 4.2)
        services.TryAddSingleton<RateLimitOptions>();
        services.TryAddSingleton<IRateLimiter, SlidingWindowRateLimiter>();

        // Register memory lineage tracking (Phase 4.2)
        services.TryAddSingleton<IMemoryLineageTracker, InMemoryLineageTracker>();

        // Register multi-tenant services (Phase 4.3)
        services.TryAddSingleton<AsyncLocalTenantContextAccessor>();
        services.TryAddSingleton<ITenantContext>(sp => sp.GetRequiredService<AsyncLocalTenantContextAccessor>());
        services.TryAddSingleton<ITenantContextAccessor>(sp => sp.GetRequiredService<AsyncLocalTenantContextAccessor>());
        services.TryAddSingleton<IAuthorizationService, DefaultAuthorizationService>();
        services.TryAddSingleton<IAuditLogger, InMemoryAuditLogger>();

        // Register evaluation services (Phase 4.4)
        services.TryAddSingleton<QualityTargets>();
        services.TryAddSingleton<IRetrievalEvaluator, DefaultRetrievalEvaluator>();
        services.TryAddSingleton<ILoCoMoEvaluator, LoCoMoEvaluator>();

        // Register observability services (Phase 4.5)
        services.TryAddSingleton<InstrumentedMemoryService>();

        return services;
    }

    /// <summary>
    /// Adds Memory Indexer services with a specific configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMemoryIndexer(
        this IServiceCollection services,
        MemoryIndexerOptions options)
    {
        return services.AddMemoryIndexer(o =>
        {
            o.Storage = options.Storage;
            o.Embedding = options.Embedding;
            o.Scoring = options.Scoring;
            o.Search = options.Search;
        });
    }

    private static QdrantMemoryStore CreateQdrantStore(
        MemoryIndexerOptions options,
        ILogger<QdrantMemoryStore> logger)
    {
        // Parse connection string for host:port format
        var connectionString = options.Storage.ConnectionString ?? "localhost:6334";
        var parts = connectionString.Replace("http://", "").Replace("https://", "").Split(':');
        var host = parts.Length > 0 ? parts[0] : "localhost";
        var port = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : 6334;

        return new QdrantMemoryStore(
            host: host,
            port: port,
            apiKey: options.Storage.Qdrant.ApiKey,
            collectionName: options.Storage.CollectionName ?? "memories",
            vectorDimensions: options.Storage.VectorDimensions > 0 ? options.Storage.VectorDimensions : options.Embedding.Dimensions,
            logger: logger);
    }
}
