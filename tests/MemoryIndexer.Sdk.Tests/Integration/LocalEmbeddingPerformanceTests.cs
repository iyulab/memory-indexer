#if !SKIP_ONNX_TESTS
using AwesomeAssertions;
using MemoryIndexer.Sdk.Tests.Integration.Fixtures;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Integration;

/// <summary>
/// Wall-clock comparisons with the local embedding model. They are <c>Category=Performance</c>, not Integration: a single
/// CPU timing sample moves with whatever else the machine is doing, so they run on demand
/// (<c>--filter-trait "Category=Performance"</c>), not in the per-release Integration set.
/// </summary>
[Trait("Category", "Performance")]
[Trait("Category", "Heavy")]
[Trait("Category", "LocalModel")]
[Collection(EmbeddingTestCollection.Name)]
public class LocalEmbeddingPerformanceTests
{
    private readonly ITestOutputHelper _output;
    private readonly SharedEmbeddingFixture _fixture;

    public LocalEmbeddingPerformanceTests(SharedEmbeddingFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task BatchEmbedding_IsNotDramaticallySlowerThanIndividualCalls()
    {
        _fixture.EnsureAvailable();

        var texts = Enumerable.Range(1, 20)
            .Select(i => $"Sample text number {i} for testing batch embedding generation performance.")
            .ToArray();

        var sw1 = System.Diagnostics.Stopwatch.StartNew();
        foreach (var text in texts)
        {
            await _fixture.EmbeddingModel!.EmbedAsync(text, TestContext.Current.CancellationToken);
        }
        sw1.Stop();
        var individualTime = sw1.ElapsedMilliseconds;

        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        await _fixture.EmbeddingModel!.EmbedAsync(texts, TestContext.Current.CancellationToken);
        sw2.Stop();
        var batchTime = sw2.ElapsedMilliseconds;

        _output.WriteLine($"Individual requests ({texts.Length} texts): {individualTime}ms ({individualTime / texts.Length}ms avg)");
        _output.WriteLine($"Batch request ({texts.Length} texts): {batchTime}ms");

        // A generous tolerance: catches batching that serializes with per-call overhead, not scheduling noise
        // (measured: batch 4440ms vs individual 4256ms on one run).
        batchTime.Should().BeLessThanOrEqualTo((long)(individualTime * 1.5),
            "batch processing should not be dramatically slower than individual calls");
    }
}
#endif
