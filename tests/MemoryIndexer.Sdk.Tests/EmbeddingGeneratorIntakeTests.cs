using System.Collections.Concurrent;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Extensions;
using MemoryIndexer.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MemoryIndexer.Sdk.Tests;

/// <summary>
/// Memory Indexer takes <c>Microsoft.Extensions.AI</c> embedding generators directly: the application registers its
/// generator(s) and writes no <see cref="IEmbeddingService"/>. A keyed generator carries an asymmetric model's query side.
/// </summary>
public sealed class EmbeddingGeneratorIntakeTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"mi-meai-{Guid.NewGuid():N}.db");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _db, _db + "-wal", _db + "-shm" })
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The SQLite store sizes its vector table from the embedding service — the generator's 4 dimensions, with no
    /// dimension configured anywhere (the built-in default, 768, would refuse every write).
    /// </summary>
    [Fact]
    public async Task Memories_are_stored_with_the_documents_generator_and_recalled_with_the_queries_generator()
    {
        var documents = new RecordingGenerator(text => text.Contains("tea", StringComparison.Ordinal) ? 0 : 1);
        var queries = new RecordingGenerator(_ => 0);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(documents);
        services.AddKeyedSingleton<IEmbeddingGenerator<string, Embedding<float>>>("query", queries);
        services.AddMemoryIndexer()
            .WithSqliteVec(_db)
            .WithEmbeddingGenerator(queriesServiceKey: "query");
        await using var provider = services.BuildServiceProvider();
        var memory = provider.GetRequiredService<IMemoryService>();

        await memory.RememberAsync("u", sessionId: null, "The user prefers green tea.", type: MemoryType.Fact, cancellationToken: Ct);
        await memory.RememberAsync("u", sessionId: null, "The user lives in Busan.", type: MemoryType.Fact, cancellationToken: Ct);
        Assert.Empty(queries.Inputs);
        Assert.NotEmpty(documents.Inputs);
        documents.Inputs.Clear();

        var recalled = await memory.RecallAsync("u", sessionId: null, "drink preference", limit: 1, cancellationToken: Ct);

        Assert.Contains("drink preference", queries.Inputs);
        Assert.DoesNotContain("drink preference", documents.Inputs);
        Assert.Contains(recalled.AllMemories(), m => m.Content.Contains("tea", StringComparison.Ordinal));
    }

    [Fact]
    public void WithEmbeddingGenerator_replaces_the_embedding_service_registered_before_it()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new RecordingGenerator(_ => 0));
        services.AddMemoryIndexer(o => o.Embedding.Provider = EmbeddingProvider.Mock).WithEmbeddingGenerator();
        using var provider = services.BuildServiceProvider();

        Assert.IsType<EmbeddingGeneratorEmbeddingService>(provider.GetRequiredService<IEmbeddingService>());
    }

    [Fact]
    public void The_dimension_comes_from_the_generator_or_the_argument_and_they_must_agree()
    {
        Assert.Equal(RecordingGenerator.Dimensions, new EmbeddingGeneratorEmbeddingService(new RecordingGenerator(_ => 0)).Dimensions);
        Assert.Equal(16, new EmbeddingGeneratorEmbeddingService(new RecordingGenerator(_ => 0, reportDimensions: false), dimensions: 16).Dimensions);

        var unknown = Assert.Throws<ArgumentException>(() => new EmbeddingGeneratorEmbeddingService(new RecordingGenerator(_ => 0, reportDimensions: false)));
        Assert.Contains("does not report a dimension", unknown.Message, StringComparison.Ordinal);
        var contradicting = Assert.Throws<ArgumentException>(() => new EmbeddingGeneratorEmbeddingService(new RecordingGenerator(_ => 0), dimensions: 5));
        Assert.Contains("reports 4", contradicting.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_batch_is_one_generator_call_and_a_short_result_set_throws()
    {
        var generator = new RecordingGenerator(_ => 0);
        var service = new EmbeddingGeneratorEmbeddingService(generator);

        var vectors = await service.GenerateBatchEmbeddingsAsync(["a", "b", "c"], Ct);

        Assert.Equal(1, generator.CallCount);
        Assert.Equal(3, vectors.Count);

        var shorted = new EmbeddingGeneratorEmbeddingService(new RecordingGenerator(_ => 0, dropLast: true));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => shorted.GenerateBatchEmbeddingsAsync(["a", "b"], Ct));
        Assert.Contains("positional", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_vector_of_another_size_throws()
    {
        var service = new EmbeddingGeneratorEmbeddingService(new RecordingGenerator(_ => 0, reportDimensions: false), dimensions: 8);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateEmbeddingAsync("text", Ct));

        Assert.Contains("4-dimension", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A generator that puts each text on one axis of a 4-dimension space and records what it embedded.</summary>
    private sealed class RecordingGenerator(Func<string, int> axis, bool reportDimensions = true, bool dropLast = false)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public const int Dimensions = 4;

        public ConcurrentQueue<string> Inputs { get; } = new();
        public int CallCount;

        private readonly EmbeddingGeneratorMetadata _metadata =
            new("test-provider", providerUri: null, defaultModelId: "test-model", defaultModelDimensions: reportDimensions ? Dimensions : null);

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            var list = values.ToList();
            foreach (var v in list)
                Inputs.Enqueue(v);
            var vectors = list.Select(v =>
            {
                var vector = new float[Dimensions];
                vector[axis(v)] = 1f;
                return new Embedding<float>(vector);
            }).ToList();
            if (dropLast && vectors.Count > 0)
                vectors.RemoveAt(vectors.Count - 1);
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(vectors));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(EmbeddingGeneratorMetadata) ? _metadata
            : serviceKey is null && serviceType.IsInstanceOfType(this) ? this
            : null;

        public void Dispose() { }
    }
}
