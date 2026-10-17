using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Profile;
using MemoryIndexer.Sdk.Intelligence.Promotion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Promotion;

/// <summary>
/// Fast-track promotion against the real archive store: a fact that replaces an earlier one keeps the earlier one as
/// history, and a fact promoted again with the same value counts as a confirmation rather than a fresh entry.
/// </summary>
public class FastTrackPromoterArchiveHistoryTests
{
    private const string UserId = "user1";
    private const string NameKey = "identity:user:name";

    private readonly IFactExtractor _extractor = Substitute.For<IFactExtractor>();
    private readonly IMemoryStore _memoryStore = Substitute.For<IMemoryStore>();
    private readonly ArchiveStoreService _archive;
    private readonly FastTrackPromoterService _service;

    public FastTrackPromoterArchiveHistoryTests()
    {
        var embedding = Substitute.For<IEmbeddingService>();
        embedding.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>([1f, 0f]));
        _archive = new ArchiveStoreService(
            embedding,
            Options.Create(new SemanticStoreOptions()),
            NullLogger<ArchiveStoreService>.Instance);
        _memoryStore.GetAllAsync(Arg.Any<string>(), Arg.Any<MemoryFilterOptions>(), Arg.Any<CancellationToken>())
            .Returns(new List<MemoryUnit>());
        _service = new FastTrackPromoterService(
            _extractor,
            _archive,
            NullLogger<FastTrackPromoterService>.Instance,
            _memoryStore);
    }

    [Fact]
    public async Task ReplacingAFact_KeepsTheEarlierValueAsHistory()
    {
        await PromoteAsync("User's name is John", FactConflictAction.Add, FactConflictType.None);
        await PromoteAsync("User's name is Mike", FactConflictAction.Replace, FactConflictType.Contradiction);

        var current = await _archive.GetAsync(UserId, NameKey, TestContext.Current.CancellationToken);
        current!.Value.Should().Be("User's name is Mike");

        var history = await _archive.GetHistoryAsync(UserId, NameKey, TestContext.Current.CancellationToken);
        history.Select(e => e.Value).Should().Equal("User's name is Mike", "User's name is John");
        history[1].IsActive.Should().BeFalse();
        history[1].ValidTo.Should().NotBeNull();
    }

    [Fact]
    public async Task PromotingTheSameFactAgain_CountsAsAConfirmation()
    {
        await PromoteAsync("User's name is John", FactConflictAction.Add, FactConflictType.None);
        await PromoteAsync("User's name is John", FactConflictAction.Add, FactConflictType.None);

        var entry = await _archive.GetAsync(UserId, NameKey, TestContext.Current.CancellationToken);
        entry!.ConfirmationCount.Should().Be(2);

        var history = await _archive.GetHistoryAsync(UserId, NameKey, TestContext.Current.CancellationToken);
        history.Should().ContainSingle();
    }

    private async Task PromoteAsync(string content, FactConflictAction action, FactConflictType conflict)
    {
        var fact = new UserFact
        {
            Content = content,
            Category = FactCategory.Identity,
            Confidence = 0.95f,
            PromotionPath = PromotionPath.FastTrack,
            Subject = "user",
            Predicate = "name",
            Object = content[(content.LastIndexOf(' ') + 1)..],
        };
        _extractor.ExtractAsync(Arg.Any<FactExtractionContext>(), Arg.Any<CancellationToken>())
            .Returns(new FactExtractionResult
            {
                Facts = [fact],
                OverallConfidence = 0.95f,
                ContextType = StatementContextType.Direct,
            });
        _extractor.ValidateAsync(Arg.Any<UserFact>(), Arg.Any<IReadOnlyList<MemoryUnit>>(), Arg.Any<CancellationToken>())
            .Returns(new FactValidationResult
            {
                IsValid = true,
                ConflictType = conflict,
                RecommendedAction = action,
            });

        await _service.ProcessAsync(
            new FactExtractionContext { Content = content, UserId = UserId },
            TestContext.Current.CancellationToken);
    }
}
