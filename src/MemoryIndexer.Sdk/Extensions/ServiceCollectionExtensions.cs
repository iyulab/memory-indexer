using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Services;
using Microsoft.Extensions.Hosting;
using MemoryIndexer.Sdk.Embedding.Providers;
using MemoryIndexer.Sdk.Completion.Providers;
using MemoryIndexer.Sdk.Intelligence.Classification;
using MemoryIndexer.Sdk.Intelligence.Chunking;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using MemoryIndexer.Sdk.Intelligence.Extraction;
using MemoryIndexer.Sdk.Intelligence.Consolidation;
using MemoryIndexer.Sdk.Intelligence.Graph;
using MemoryIndexer.Sdk.Intelligence.Compression;
using MemoryIndexer.Sdk.Intelligence.ContextOptimization;
using MemoryIndexer.Sdk.Intelligence.Deduplication;
using MemoryIndexer.Sdk.Intelligence.Evaluation;
using MemoryIndexer.Sdk.Intelligence.KnowledgeGraph;
using MemoryIndexer.Sdk.Intelligence.Operations;
using MemoryIndexer.Sdk.Intelligence.Reranking;
using MemoryIndexer.Sdk.Intelligence.Retrieval;
using MemoryIndexer.Scoring;
using MemoryIndexer.Sdk.Intelligence.Scoring;
using MemoryIndexer.Sdk.Intelligence.Search;
using MemoryIndexer.Sdk.Intelligence.SelfEditing;
using MemoryIndexer.Sdk.Intelligence.Security;
using MemoryIndexer.Sdk.Intelligence.Security.MultiTenant;
using MemoryIndexer.Sdk.Intelligence.EntityResolution;
using MemoryIndexer.Sdk.Intelligence.Profile;
using MemoryIndexer.Sdk.Intelligence.Promotion;
using MemoryIndexer.Sdk.Intelligence.Quality;
using MemoryIndexer.Sdk.Intelligence.Summarization;
using MemoryIndexer.Sdk.Intelligence.Caching;
using MemoryIndexer.Sdk.Intelligence.ResourceManagement;
using MemoryIndexer.Sdk.Services;
using MemoryIndexer.Sdk.Services.Export;
using MemoryIndexer.Sdk.Observability;
using MemoryIndexer.InMemory;
using MemoryIndexer.Mock;
using MemoryIndexer.Sdk.Storage.Qdrant;
using MemoryIndexer.Sdk.Storage.Sqlite;
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

        // Register configuration validator (Phase v0.5.0)
        services.TryAddSingleton<IConfigurationValidator, ConfigurationValidator>();

        // Register core services
        services.TryAddSingleton<MemoryService>();

        // Register Simple API (Phase 33.1)
        services.TryAddSingleton<IMemoryService, SimpleMemoryService>();

        // Register VCM services (Phase 5)
        // Note: MemoryIndexer.Services.WorkingMemoryOptions is for ShortTermMemoryService cache configuration
        services.AddOptions<MemoryIndexer.Services.WorkingMemoryOptions>()
            .BindConfiguration("MemoryIndexer:VCM:WorkingMemory");
        services.AddOptions<VCMOptions>()
            .BindConfiguration("MemoryIndexer:VCM");

        // Register 3-axis model services (Phase 32.3)
        services.AddOptions<ScopeManagerOptions>()
            .BindConfiguration("MemoryIndexer:ScopeManager");

        services.TryAddSingleton<IShortTermMemory, ShortTermMemoryService>();
        services.TryAddSingleton<IMemoryPrimitives, MemoryPrimitivesService>();
        services.TryAddSingleton<IScopeManager, ScopeManager>();
        services.TryAddSingleton<ITierManager, TierManager>();
        services.TryAddSingleton<IVirtualContextManager, VirtualContextManager>();

        // Register Sensory buffer (Tier 0) - Phase 14 → Cognitive terminology (Phase 30)
        services.TryAddSingleton<IBuffer, BufferService>();

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

        services.TryAddSingleton<ILongTermStore>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemoryLongTermStore>>();
            return new InMemoryLongTermStore(logger);
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

        // Register text completion service based on configuration (Phase 25)
        services.TryAddSingleton<ITextCompletionService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MemoryIndexerOptions>>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();

            return options.Value.Completion.Provider switch
            {
                CompletionProvider.Ollama => new OllamaCompletionService(
                    httpClientFactory.CreateClient("Ollama"),
                    options,
                    sp.GetRequiredService<ILogger<OllamaCompletionService>>()),
                CompletionProvider.OpenAI or CompletionProvider.AzureOpenAI or CompletionProvider.Custom =>
                    new OpenAICompletionService(
                        httpClientFactory.CreateClient("OpenAI"),
                        options,
                        sp.GetRequiredService<ILogger<OpenAICompletionService>>()),
                _ => throw new NotSupportedException($"Completion provider {options.Value.Completion.Provider} is not yet supported")
            };
        });

        // Register scoring service
        services.TryAddSingleton<IScoringService, DefaultScoringService>();

        // Register score normalizer (Phase 21.2)
        services.TryAddSingleton<IScoreNormalizer, AdaptiveScoreNormalizer>();

        // Register memory pressure monitor (Phase 21)
        services.TryAddSingleton<IMemoryPressureMonitor, MemoryPressureMonitorService>();

        // Register memory growth monitor (Phase 22.1)
        services.TryAddSingleton<IMemoryGrowthMonitor, InMemoryGrowthMonitor>();

        // Register latency profiler and caching (Phase 22.2)
        services.TryAddSingleton<ILatencyProfiler, InMemoryLatencyProfiler>();

        // Register recall pattern analyzer and token budget monitor (Phase v0.5.0)
        services.TryAddSingleton<IRecallPatternAnalyzer, RecallPatternAnalyzer>();
        services.TryAddSingleton<ITokenBudgetMonitor, TokenBudgetMonitor>();
        services.TryAddSingleton<OptimizedRecallService>();

        // Register re-ranking service (Phase 5.4)
        services.TryAddSingleton<IRerankerService, LocalRerankerService>();

        // Register memory classifier (Phase 5.5)
        services.TryAddSingleton<IMemoryClassifier, LocalMemoryClassifier>();

        // Register knowledge extractor (Phase 25)
        services.TryAddSingleton<IKnowledgeExtractor, LlmKnowledgeExtractor>();

        // Register memory conflict resolver (Phase 26)
        services.TryAddSingleton<IMemoryConflictResolver, RecencyWeightedResolver>();
        services.TryAddSingleton<LlmConflictDetector>();

        // Register intelligence services (Phase 2)
        services.TryAddSingleton<IHybridSearchService, HybridSearchService>();
        services.TryAddSingleton<IQueryExpander, QueryExpander>();
        services.TryAddSingleton<IHydeQueryExpander, HydeQueryExpander>();
        services.TryAddSingleton<IDeduplicationService, DeduplicationService>();
        services.TryAddSingleton<IMemoryQualityService, MemoryQualityAnalyzer>();
        services.TryAddSingleton<ImportanceAnalyzer>();
        services.TryAddSingleton<TopicSegmenter>();

        // Register summarization services (Phase 3)
        services.TryAddSingleton<ISummarizationService, ExtractiveSummarizer>();

        // Register parent-child chunk manager (Phase 6)
        services.TryAddSingleton<IParentChildChunkManager, ParentChildChunkManager>();

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

        // Register memory export/import services (Phase v0.6.0-β)
        services.TryAddSingleton<IMemoryExporter, JsonMemoryExporter>();

        // Register resource management services (Phase v0.6.0-γ)
        services.TryAddSingleton<IUsageTracker, InMemoryUsageTracker>();
        services.TryAddSingleton<IResourceLimitEnforcer, ResourceLimitEnforcer>();

        // Register temporal entity store (Phase 8.1)
        services.TryAddSingleton<ITemporalEntityStore, InMemoryTemporalEntityStore>();

        // Register contradiction detection and resolution services (Phase 8.1)
        services.TryAddSingleton<IContradictionDetector, SemanticContradictionDetector>();
        services.TryAddSingleton<IContradictionResolver, DefaultContradictionResolver>();

        // Register graph-based retrieval services (Phase 8.2)
        services.TryAddSingleton<IGraphRetriever, InMemoryGraphRetriever>();

        // Register query intent classification (Phase v0.5.0)
        services.TryAddSingleton<IQueryIntentClassifier, LocalQueryIntentClassifier>();

        // Register graph intelligence services (Phase v0.5.0)
        services.TryAddSingleton<IMemoryGraphService, MemoryGraphService>();
        services.TryAddSingleton<IImportancePropagator, PageRankImportancePropagator>();
        services.TryAddSingleton<ICommunityDetector, LabelPropagationCommunityDetector>();

        // Register memory consolidation services (Phase 9)
        services.TryAddSingleton<IMemoryConsolidator, SleepBasedConsolidator>();

        // Register intelligent memory operations (Phase 10)
        services.TryAddSingleton<IMemoryOperationDecider, SemanticOperationDecider>();

        // Register summarization trigger (Phase 10)
        services.AddOptions<TriggerOptions>()
            .BindConfiguration(TriggerOptions.SectionName);
        services.TryAddSingleton<ISummarizationTrigger, ThresholdBasedTrigger>();

        // Register summarization orchestrator (Phase 11)
        services.TryAddSingleton<ISummarizationOrchestrator, SummarizationOrchestrator>();
        services.TryAddSingleton<IRollingSummaryManager, RollingSummaryManager>();

        // Register entity resolution services (Phase 12)
        services.TryAddSingleton<ICoreferenceResolver, CoreferenceResolver>();

        // Register buffer promotion services (Phase 14 → Cognitive terminology Phase 30)
        services.TryAddSingleton<ISensoryPromoter, SensoryPromoterService>();

        // Register working memory orchestrator (Phase 14.3)
        services.AddOptions<WorkingMemoryOrchestratorOptions>()
            .BindConfiguration("MemoryIndexer:VCM:WorkingOrchestrator");
        services.TryAddSingleton<IShortTermMemoryOrchestrator, ShortTermMemoryOrchestratorService>();

        // Register semantic store service (Phase 14.4 → Cognitive terminology Phase 30)
        services.AddOptions<SemanticStoreOptions>()
            .BindConfiguration("MemoryIndexer:VCM:SemanticStore");
        services.TryAddSingleton<IArchiveStore, ArchiveStoreService>();

        // Register Long → Archive promotion service (Phase 52)
        services.TryAddSingleton<ILongTermPromoter, LongTermPromoterService>();

        // Register memory promotion background worker (Phase 47)
        services.AddOptions<MemoryPromotionBackgroundOptions>()
            .BindConfiguration("MemoryIndexer:VCM:PromotionBackground");
        services.AddHostedService<MemoryPromotionBackgroundService>();

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
