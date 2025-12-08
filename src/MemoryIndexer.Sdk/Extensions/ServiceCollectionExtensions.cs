using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Services;
using MemoryIndexer.Embedding.Providers;
using MemoryIndexer.Intelligence.Scoring;
using MemoryIndexer.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
                StorageType.SqliteVec => new InMemoryMemoryStore(logger), // TODO: Implement SqliteVec
                StorageType.Qdrant => new InMemoryMemoryStore(logger),    // TODO: Implement Qdrant
                _ => new InMemoryMemoryStore(logger)
            };
        });

        services.TryAddSingleton<ISessionStore>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<InMemorySessionStore>>();
            return new InMemorySessionStore(logger);
        });

        // Register embedding service based on configuration
        services.TryAddSingleton<IEmbeddingService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MemoryIndexerOptions>>().Value;
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MockEmbeddingService>>();

            return options.Embedding.Provider switch
            {
                EmbeddingProvider.Ollama => new MockEmbeddingService(
                    sp.GetRequiredService<IOptions<MemoryIndexerOptions>>(), logger), // TODO: Implement Ollama
                EmbeddingProvider.OpenAI => new MockEmbeddingService(
                    sp.GetRequiredService<IOptions<MemoryIndexerOptions>>(), logger), // TODO: Implement OpenAI
                _ => new MockEmbeddingService(
                    sp.GetRequiredService<IOptions<MemoryIndexerOptions>>(), logger)
            };
        });

        // Register scoring service
        services.TryAddSingleton<IScoringService, DefaultScoringService>();

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
}
