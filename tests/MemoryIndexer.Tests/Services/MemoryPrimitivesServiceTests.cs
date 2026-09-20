using AwesomeAssertions;
using MemoryIndexer.Configuration;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Tests.Services;

/// <summary>
/// Tests for <see cref="MemoryPrimitivesService"/> encoding.
/// </summary>
public class MemoryPrimitivesServiceTests
{
    private readonly IMemoryStore _memoryStore = Substitute.For<IMemoryStore>();
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IMemoryClassifier _classifier = Substitute.For<IMemoryClassifier>();

    public MemoryPrimitivesServiceTests()
    {
        _memoryStore.StoreAsync(Arg.Any<MemoryUnit>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<MemoryUnit>());

        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f]));

        _classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<ClassificationContext?>(), Arg.Any<CancellationToken>())
            .Returns(new MemoryClassification
            {
                Tier = Tier.Long,
                Type = MemoryType.Procedural,
                Importance = 0.9f,
                Topics = ["deployment"]
            });
    }

    private MemoryPrimitivesService CreateService(MemoryIndexerOptions options) =>
        new(
            _memoryStore,
            _embeddingService,
            Substitute.For<IScoringService>(),
            Substitute.For<IShortTermMemory>(),
            Options.Create(options),
            NullLogger<MemoryPrimitivesService>.Instance,
            Substitute.For<IShortTermMemoryOrchestrator>(),
            memoryClassifier: _classifier);

    private static EncodeRequest UnclassifiedRequest() => new()
    {
        UserId = "user-1",
        Content = "Run the migration before deploying"
    };

    [Fact]
    public async Task EncodeAsync_DefaultOptions_ClassifiesUnspecifiedTypeAndImportance()
    {
        // Arrange
        var service = CreateService(new MemoryIndexerOptions());

        // Act
        var stored = await service.EncodeAsync(UnclassifiedRequest(), TestContext.Current.CancellationToken);

        // Assert
        stored.Type.Should().Be(MemoryType.Procedural);
        stored.ImportanceScore.Should().Be(0.9f);
        stored.Topics.Should().BeEquivalentTo("deployment");
    }

    [Fact]
    public async Task EncodeAsync_ClassificationDisabled_UsesFallbacksWithoutCallingTheClassifier()
    {
        // Arrange
        var options = new MemoryIndexerOptions();
        options.Intelligence.ClassificationEnabled = false;
        var service = CreateService(options);

        // Act
        var stored = await service.EncodeAsync(UnclassifiedRequest(), TestContext.Current.CancellationToken);

        // Assert
        stored.Type.Should().Be(MemoryType.Episodic);
        stored.ImportanceScore.Should().Be(0.5f);
        stored.Topics.Should().BeEmpty();
        await _classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<string>(), Arg.Any<ClassificationContext?>(), Arg.Any<CancellationToken>());
    }
}
