
using MemoryIndexer.VectorData;
using Microsoft.Extensions.VectorData;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Services;
using MemoryIndexer.Services.ContextBuilding;
using MemoryIndexer.Services.TokenCounting;
using Microsoft.Extensions.Hosting;
using MemoryIndexer.Mock;
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
using MemoryIndexer.Sdk.Intelligence.Reflection;
using MemoryIndexer.Sdk.Intelligence.Retrieval;
using MemoryIndexer.Sdk.Intelligence.SelfCorrection;
using MemoryIndexer.Scoring;
using MemoryIndexer.Sdk.Intelligence.Scoring;
using MemoryIndexer.Sdk.Intelligence.Search;
using MemoryIndexer.Sdk.Intelligence.SelfEditing;
using MemoryIndexer.Sdk.Intelligence.Security;
using MemoryIndexer.Sdk.Intelligence.Security.MultiTenant;
using MemoryIndexer.Sdk.Intelligence.EntityResolution;
using MemoryIndexer.Sdk.Intelligence.Profile;
using MemoryIndexer.Sdk.Intelligence.Inference;
using MemoryIndexer.Sdk.Intelligence.Promotion;
using MemoryIndexer.Sdk.Intelligence.Retention;
using MemoryIndexer.Sdk.Intelligence.Quality;
using MemoryIndexer.Sdk.Intelligence.Summarization;
using MemoryIndexer.Sdk.Intelligence.Caching;
using MemoryIndexer.Sdk.Intelligence.ResourceManagement;
using MemoryIndexer.Sdk.Services;
using MemoryIndexer.Sdk.Services.Export;
using MemoryIndexer.Sdk.Observability;
using MemoryIndexer.InMemory;
using MemoryIndexer.Sdk.Storage.Sqlite;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// Builder for configuring Memory Indexer services.
/// </summary>
public interface IMemoryIndexerBuilder
{
    /// <summary>
    /// The service collection being configured.
    /// </summary>
    IServiceCollection Services { get; }
}

/// <summary>
/// Default implementation of IMemoryIndexerBuilder.
/// </summary>
internal sealed class MemoryIndexerBuilder : IMemoryIndexerBuilder
{
    public IServiceCollection Services { get; }

    public MemoryIndexerBuilder(IServiceCollection services)
    {
        Services = services;
    }
}

/// <summary>
/// Extension methods for registering Memory Indexer services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Memory Indexer services to the service collection.
    /// By default, uses InMemoryMemoryStore. Use WithSqliteVec() for persistent storage,
    /// or register your own IMemoryStore before calling this method.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration action.</param>
    /// <returns>Builder for additional configuration.</returns>
    public static IMemoryIndexerBuilder AddMemoryIndexer(
        this IServiceCollection services,
        Action<MemoryIndexerOptions>? configure = null)
    {
        // Register options
        // Bound when the host starts, so a configuration value that does not bind (an unknown Storage:Type, say)
        // fails startup instead of the first request that resolves a store
        services.AddOptions<MemoryIndexerOptions>()
            .BindConfigurationIfPresent(MemoryIndexerOptions.SectionName)
            .ValidateOnStart();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        // The rules (ranges, consistency between settings) run when the options are first built - at host start through
        // ValidateOnStart above - so a value out of range fails startup with its setting named, not a later request.
        services.TryAddSingleton<IConfigurationValidator, ConfigurationValidator>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MemoryIndexerOptions>, ConfigurationValidationAtStartup>());

        // Register core services
        services.TryAddSingleton<MemoryService>();

        // Register Simple API (Phase 33.1)
        services.TryAddSingleton<IMemoryService, SimpleMemoryService>();

        // How ShortTermMemoryService keeps its entries (the section keeps its historical name, MemoryIndexer:VCM:WorkingMemory);
        // its capacity is MemoryIndexer:WorkingMemory:Capacity (the Short tier's)
        services.AddOptions<MemoryIndexer.Services.WorkingMemoryCacheOptions>()
            .BindConfigurationIfPresent("MemoryIndexer:VCM:WorkingMemory");

        services.TryAddSingleton<IShortTermMemory, ShortTermMemoryService>();
        // Completion is optional here: its fallbacks run when none is configured (TextCompletion.OrNull),
        // instead of a placeholder service being handed to it.
        services.TryAddSingleton<IMemoryPrimitives>(sp => new MemoryPrimitivesService(
            sp.GetRequiredService<IMemoryStore>(),
            sp.GetRequiredService<IEmbeddingService>(),
            sp.GetRequiredService<IScoringService>(),
            sp.GetRequiredService<IShortTermMemory>(),
            sp.GetRequiredService<IOptions<MemoryIndexerOptions>>(),
            sp.GetRequiredService<ILogger<MemoryPrimitivesService>>(),
            sp.GetRequiredService<IShortTermMemoryOrchestrator>(),
            sp.GetService<IRerankerService>(),
            sp.GetService<IDeduplicationService>(),
            sp.GetService<IMemoryClassifier>(),
            TextCompletion.OrNull(sp)));
        services.TryAddSingleton<IScopeManager, ScopeManager>();
        services.TryAddSingleton<ITierManager, TierManager>();

        // Register Context Budget API services (Phase v0.9.0)
        services.TryAddSingleton<ITokenCounter, ApproximateTokenCounter>();
        services.TryAddSingleton<IContextBuilder, ContextBuilder>();

        // Register Sensory buffer (Tier 0) - Phase 14 → Cognitive terminology (Phase 30)
        services.TryAddSingleton<IBuffer, BufferService>();

        // Register the configured built-in store (Storage:Type, default InMemory). WithSqliteVec() selects SQLite in
        // code; an IMemoryStore the application registered before calling AddMemoryIndexer() wins over both.
        services.TryAddSingleton<IMemoryStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value;
            return options.Storage.Type switch
            {
                StorageType.InMemory => new InMemoryMemoryStore(sp.GetRequiredService<ILogger<InMemoryMemoryStore>>()),
                StorageType.SqliteVec => CreateSqliteVecStore(sp, options, databasePath: null),
                StorageType.VectorStore => CreateVectorStoreStore(sp, options, collectionName: null),
                _ => throw new InvalidOperationException(
                    $"MemoryIndexer:Storage:Type '{options.Storage.Type}' is not a built-in store. Use InMemory, SqliteVec or VectorStore, " +
                    "or register your own IMemoryStore before calling AddMemoryIndexer()."),
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
        // Only Mock is built in. Custom (the default) is the application's own IEmbeddingService, registered before or
        // after AddMemoryIndexer() - this registration yields to it.
        services.TryAddSingleton<IEmbeddingService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MemoryIndexerOptions>>();

            return options.Value.Embedding.Provider switch
            {
                EmbeddingProvider.Mock => new MockEmbeddingService(
                    options,
                    sp.GetRequiredService<ILogger<MockEmbeddingService>>()),
                _ => throw new NotSupportedException(
                    $"Embedding provider '{options.Value.Embedding.Provider}' requires external implementation. " +
                    "Register your own IEmbeddingService before calling AddMemoryIndexer() (see README.md 'As SDK'), " +
                    "or set MemoryIndexer:Embedding:Provider to 'Mock' for local development " +
                    "(deterministic, non-semantic embeddings - not suitable for production search quality).")
            };
        });

        // Register text completion service based on configuration (Phase 25)
        // The library builds no LLM client: an application registers its own ITextCompletionService (a registration
        // before or after this call wins over the default below). Without one, None (the default) resolves to a service
        // that throws when asked (LLM-only services then take their own error path), and the services that work without
        // completion are given null (TextCompletion.OrNull).
        services.TryAddSingleton<ITextCompletionService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MemoryIndexerOptions>>();

            return options.Value.Completion.Provider switch
            {
                CompletionProvider.None => new UnconfiguredTextCompletionService(),
                CompletionProvider.Mock => new MockTextCompletionService(
                    sp.GetRequiredService<ILogger<MockTextCompletionService>>()),
                _ => throw new NotSupportedException(
                    $"Completion provider '{options.Value.Completion.Provider}' requires external implementation. " +
                    "Register your own ITextCompletionService, or leave MemoryIndexer:Completion:Provider unset " +
                    "('None') to run without completion.")
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
        // Note: MockRerankerService is a passthrough. For real re-ranking,
        // register your own IRerankerService before calling AddMemoryIndexer().
        services.TryAddSingleton<IRerankerService, MockRerankerService>();

        // Register memory classifier (Phase 5.5)
        services.TryAddSingleton<IMemoryClassifier, LocalMemoryClassifier>();

        // Register knowledge extractor (Phase 25)
        services.TryAddSingleton<IKnowledgeExtractor, LlmKnowledgeExtractor>();

        // Register fact extractor with context awareness (Phase v0.9.1)
        // Handles direct statements with promotion path determination
        services.TryAddSingleton<IFactExtractor, LlmFactExtractor>();

        // Register fast-track promoter for high-confidence facts (Phase v0.9.1)
        // Enables Buffer → Archive direct path for confidence ≥ 0.9
        services.TryAddSingleton<IFastTrackPromoter, FastTrackPromoterService>();

        // Register fact validator for category-specific conflict resolution (Phase v0.9.2)
        services.TryAddSingleton<IFactValidator, FactValidatorService>();

        // Register profile evolution services (Phase v0.10.0)
        services.AddOptions<ConfidenceDecayOptions>()
            .BindConfigurationIfPresent("MemoryIndexer:ConfidenceDecay");
        services.TryAddSingleton<IConfidenceDecayStrategy, TimeBasedDecayStrategy>();
        services.TryAddSingleton<IProfileSnapshotService, ProfileSnapshotService>();
        services.TryAddSingleton<IProfileExporter, JsonProfileExporter>();
        services.TryAddSingleton<IFactInferenceEngine, FactInferenceService>();

        // Register retention policy services (Phase v0.11.0)
        services.AddOptions<RetentionPolicyOptions>()
            .BindConfigurationIfPresent("MemoryIndexer:RetentionPolicy");
        services.TryAddSingleton<IRetentionPolicy, DefaultRetentionPolicy>();
        services.TryAddSingleton<IRetentionPolicyService, RetentionPolicyService>();

        // Register memory conflict resolver (Phase 26)
        services.TryAddSingleton<IMemoryConflictResolver, RecencyWeightedResolver>();
        services.TryAddSingleton(sp => new LlmConflictDetector(
            TextCompletion.OrNull(sp), sp.GetRequiredService<ILogger<LlmConflictDetector>>()));

        // Register intelligence services (Phase 2)
        services.TryAddSingleton<IHybridSearchService, HybridSearchService>();
        services.TryAddSingleton<IQueryExpander, QueryExpander>();
        services.TryAddSingleton<IHydeQueryExpander, HydeQueryExpander>();
        services.TryAddSingleton<IDeduplicationService, DeduplicationService>();
        // Finding and merging duplicate groups across stored memories (backs the advanced memory tools)
        services.TryAddSingleton<DuplicateDetector>();
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

        // Tier-weighted retrieval by query intent (backs the adaptive retrieval tools)
        services.TryAddSingleton<TieredMemoryRetriever>();
        services.TryAddSingleton<ITieredRetrievalStrategy>(sp => sp.GetRequiredService<TieredMemoryRetriever>());

        // Reflection and self-correction (back the memory reflection tools)
        services.TryAddSingleton<IReflectionEngine, ReflectionEngine>();
        services.TryAddSingleton<IMemorySelfCorrector, MemorySelfCorrector>();

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
            .BindConfigurationIfPresent(TriggerOptions.SectionName);
        services.TryAddSingleton<ISummarizationTrigger, ThresholdBasedTrigger>();

        // Register summarization orchestrator (Phase 11)
        services.TryAddSingleton<ISummarizationOrchestrator, SummarizationOrchestrator>();
        services.TryAddSingleton<IRollingSummaryManager, RollingSummaryManager>();

        // Register entity resolution services (Phase 12)
        services.TryAddSingleton<ICoreferenceResolver, CoreferenceResolver>();

        // Register buffer promotion services (Phase 14 → Cognitive terminology Phase 30)
        services.TryAddSingleton<ISensoryPromoter, SensoryPromoterService>();

        // Register working memory orchestrator (Phase 14.3)
        services.TryAddSingleton<IShortTermMemoryOrchestrator, ShortTermMemoryOrchestratorService>();

        // Register semantic store service (Phase 14.4 → Cognitive terminology Phase 30)
        services.AddOptions<SemanticStoreOptions>()
            .BindConfigurationIfPresent("MemoryIndexer:VCM:SemanticStore");
        // The profile lives in the configured IMemoryStore (under ArchiveUserKey), so it persists with the memories
        services.TryAddSingleton<IArchiveStore, ArchiveStoreService>();

        // Register Long → Archive promotion service (Phase 52)
        services.TryAddSingleton<ILongTermPromoter, LongTermPromoterService>();

        // Register memory promotion background worker (Phase 47)
        services.AddOptions<MemoryPromotionBackgroundOptions>()
            .BindConfigurationIfPresent("MemoryIndexer:VCM:PromotionBackground");
        services.AddHostedService<MemoryPromotionBackgroundService>();

        return new MemoryIndexerBuilder(services);
    }

    /// <summary>
    /// Adds Memory Indexer services with a specific configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>Builder for additional configuration.</returns>
    public static IMemoryIndexerBuilder AddMemoryIndexer(
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

    /// <summary>
    /// Configures Memory Indexer to use SQLite with vector extension for persistent storage.
    /// </summary>
    /// <param name="builder">The Memory Indexer builder.</param>
    /// <param name="databasePath">Optional database path. If not specified, uses Storage.ConnectionString from options or "memories.db".</param>
    /// <returns>The builder for chaining.</returns>
    /// <param name="connectionOpened">Runs on the connection right after it opens (an encryption key, other pragmas).
    /// See <see cref="SqliteVecMemoryStore"/>.</param>
    public static IMemoryIndexerBuilder WithSqliteVec(
        this IMemoryIndexerBuilder builder,
        string? databasePath = null,
        Action<Microsoft.Data.Sqlite.SqliteConnection>? connectionOpened = null)
    {
        // Remove default InMemory registration and replace with SqliteVec
        var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryStore));
        if (descriptor != null)
        {
            builder.Services.Remove(descriptor);
        }

        builder.Services.AddSingleton<IMemoryStore>(sp =>
            CreateSqliteVecStore(sp, sp.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value, databasePath, connectionOpened));

        return builder;
    }

    /// <summary>
    /// Embeds with the <c>Microsoft.Extensions.AI</c> <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> the
    /// application registered (OpenAI, Azure OpenAI, Ollama, LMSupply, IronHive) instead of an <see cref="IEmbeddingService"/>
    /// it writes — <see cref="EmbeddingGeneratorEmbeddingService"/>. Replaces any embedding service registered so far.
    /// </summary>
    /// <param name="builder">The Memory Indexer builder.</param>
    /// <param name="queriesServiceKey">
    /// The key of a keyed generator that embeds queries (an asymmetric model's query side); null embeds queries with the
    /// unkeyed generator.
    /// </param>
    /// <param name="dimensions">The vector size, when the generator does not report one.</param>
    /// <returns>The builder for chaining.</returns>
    public static IMemoryIndexerBuilder WithEmbeddingGenerator(
        this IMemoryIndexerBuilder builder,
        object? queriesServiceKey = null,
        int? dimensions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<IEmbeddingService>();
        builder.Services.AddSingleton<IEmbeddingService>(sp => new EmbeddingGeneratorEmbeddingService(
            sp.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(),
            queriesServiceKey is null
                ? null
                : sp.GetRequiredKeyedService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(queriesServiceKey),
            dimensions));
        return builder;
    }

    /// <summary>
    /// Configures Memory Indexer to keep memories in the Microsoft.Extensions.VectorData <c>VectorStore</c> the application
    /// registers (Qdrant, PostgreSQL/pgvector, …) — <see cref="VectorStoreMemoryStore"/>. The vector size is the registered
    /// embedding service's <see cref="IEmbeddingService.Dimensions"/>, as for SQLite.
    /// </summary>
    /// <param name="builder">The Memory Indexer builder.</param>
    /// <param name="collectionName">Collection name; default <c>MemoryIndexer:Storage:CollectionName</c> («memories»).</param>
    /// <returns>The builder for chaining.</returns>
    public static IMemoryIndexerBuilder WithVectorStore(this IMemoryIndexerBuilder builder, string? collectionName = null)
    {
        var descriptor = builder.Services.FirstOrDefault(d => d.ServiceType == typeof(IMemoryStore));
        if (descriptor != null)
        {
            builder.Services.Remove(descriptor);
        }

        builder.Services.AddSingleton<IMemoryStore>(sp =>
            CreateVectorStoreStore(sp, sp.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value, collectionName));

        return builder;
    }

    private static VectorStoreMemoryStore CreateVectorStoreStore(IServiceProvider sp, MemoryIndexerOptions options, string? collectionName)
    {
        var vectorStore = sp.GetService<VectorStore>() ?? throw new InvalidOperationException(
            "MemoryIndexer:Storage:Type is VectorStore, but no Microsoft.Extensions.VectorData VectorStore is registered. " +
            "Register the connector's VectorStore (e.g. Qdrant, PgVector) before resolving the memory store.");
        var dimensions = sp.GetRequiredService<IEmbeddingService>().Dimensions;

        return new VectorStoreMemoryStore(
            vectorStore,
            new VectorStoreMemoryStoreOptions
            {
                CollectionName = collectionName ?? options.Storage.CollectionName,
                Dimensions = dimensions
            },
            sp.GetRequiredService<ILogger<VectorStoreMemoryStore>>());
    }

    private static SqliteVecMemoryStore CreateSqliteVecStore(
        IServiceProvider sp, MemoryIndexerOptions options, string? databasePath,
        Action<Microsoft.Data.Sqlite.SqliteConnection>? connectionOpened = null)
    {
        var resolvedPath = databasePath ?? options.Storage.ConnectionString ?? "memories.db";
        var dimensions = sp.GetRequiredService<IEmbeddingService>().Dimensions;

        return new SqliteVecMemoryStore(
            databasePath: resolvedPath,
            vectorDimensions: dimensions,
            options: options.Storage.Sqlite,
            logger: sp.GetRequiredService<ILogger<SqliteVecMemoryStore>>(),
            connectionOpened: connectionOpened);
    }
}
