using FluentAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Sdk.Intelligence.Inference;
using MemoryIndexer.Sdk.Intelligence.Profile;
using MemoryIndexer.Sdk.Mcp.Tools;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Mcp.Tools;

/// <summary>
/// Tests for ProfileEvolutionTools MCP tools.
/// Phase v0.10.0: User Profile Evolution.
/// </summary>
public class ProfileEvolutionToolsTests
{
    private readonly Mock<IFactInferenceEngine> _mockInferenceEngine;
    private readonly Mock<IProfileSnapshotService> _mockSnapshotService;
    private readonly Mock<IConfidenceDecayStrategy> _mockDecayStrategy;
    private readonly Mock<IProfileExporter> _mockProfileExporter;
    private readonly Mock<IArchiveStore> _mockArchiveStore;
    private readonly ProfileEvolutionTools _tools;

    public ProfileEvolutionToolsTests()
    {
        _mockInferenceEngine = new Mock<IFactInferenceEngine>();
        _mockSnapshotService = new Mock<IProfileSnapshotService>();
        _mockDecayStrategy = new Mock<IConfidenceDecayStrategy>();
        _mockProfileExporter = new Mock<IProfileExporter>();
        _mockArchiveStore = new Mock<IArchiveStore>();

        _mockProfileExporter.Setup(e => e.Format).Returns("json");

        _tools = new ProfileEvolutionTools(
            _mockInferenceEngine.Object,
            _mockSnapshotService.Object,
            _mockDecayStrategy.Object,
            _mockProfileExporter.Object,
            _mockArchiveStore.Object);
    }

    #region InferFacts Tests

    [Fact]
    public async Task InferFacts_ShouldCallInferenceEngine()
    {
        // Arrange
        var inferenceResult = new InferenceResult
        {
            UserId = "user1",
            SourceFactCount = 5,
            InferredFacts =
            [
                new InferredFact
                {
                    Content = "User enjoys food",
                    Category = FactCategory.Preference,
                    Type = InferenceType.Generalization,
                    Confidence = 0.8f,
                    SourceFactKeys = ["pref1", "pref2"]
                }
            ],
            StoredCount = 0,
            ProcessingTimeMs = 100
        };

        _mockInferenceEngine
            .Setup(e => e.InferForUserAsync("user1", It.IsAny<InferenceOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inferenceResult);

        // Act
        var result = await _tools.InferFacts("user1");

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().Be("user1");
        result.SourceFactCount.Should().Be(5);
        result.InferredCount.Should().Be(1);
        result.Inferences.Should().HaveCount(1);
        result.Inferences![0].Type.Should().Be("Generalization");
    }

    [Fact]
    public async Task InferFacts_WithOptions_ShouldPassOptions()
    {
        // Arrange
        _mockInferenceEngine
            .Setup(e => e.InferForUserAsync(
                "user1",
                It.Is<InferenceOptions>(o => o.MinSourceConfidence == 0.8f && o.MaxResults == 10),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InferenceResult { UserId = "user1", InferredFacts = [] });

        // Act
        var result = await _tools.InferFacts("user1", minConfidence: 0.8f, maxResults: 10);

        // Assert
        result.Success.Should().BeTrue();
        _mockInferenceEngine.Verify(
            e => e.InferForUserAsync("user1", It.Is<InferenceOptions>(o => o.MinSourceConfidence == 0.8f), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region GetInferenceRules Tests

    [Fact]
    public async Task GetInferenceRules_ShouldReturnRegisteredRules()
    {
        // Arrange
        var mockRule = new Mock<IInferenceRule>();
        mockRule.Setup(r => r.Name).Returns("TestRule");
        mockRule.Setup(r => r.Description).Returns("Test description");
        mockRule.Setup(r => r.Type).Returns(InferenceType.Custom);

        _mockInferenceEngine.Setup(e => e.GetRegisteredRules())
            .Returns(new List<IInferenceRule> { mockRule.Object });

        // Act
        var result = await _tools.GetInferenceRules();

        // Assert
        result.Success.Should().BeTrue();
        result.RuleCount.Should().Be(1);
        result.Rules.Should().HaveCount(1);
        result.Rules![0].Name.Should().Be("TestRule");
        result.Rules[0].Type.Should().Be("Custom");
    }

    #endregion

    #region CreateProfileSnapshot Tests

    [Fact]
    public async Task CreateProfileSnapshot_ShouldCreateSnapshot()
    {
        // Arrange
        var snapshot = new ProfileSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = "user1",
            Label = "Test snapshot",
            CreatedAt = DateTime.UtcNow,
            Facts = [new SemanticStoreEntry { Key = "test", Value = "value" }],
            Stats = new ProfileStats
            {
                TotalFacts = 1,
                ConfirmedFacts = 1,
                StaleFactCount = 0,
                AverageConfidence = 0.9f,
                CompletenessScore = 0.5f
            }
        };

        _mockSnapshotService
            .Setup(s => s.CreateSnapshotAsync("user1", "Test snapshot", It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        // Act
        var result = await _tools.CreateProfileSnapshot("user1", "Test snapshot");

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().Be("user1");
        result.Label.Should().Be("Test snapshot");
        result.FactCount.Should().Be(1);
        result.Stats.Should().NotBeNull();
        result.Stats!.TotalFacts.Should().Be(1);
    }

    #endregion

    #region ListProfileSnapshots Tests

    [Fact]
    public async Task ListProfileSnapshots_ShouldListSnapshots()
    {
        // Arrange
        var snapshots = new List<ProfileSnapshotSummary>
        {
            new() { Id = Guid.NewGuid(), UserId = "user1", Label = "Snapshot 1", CreatedAt = DateTime.UtcNow.AddDays(-1), FactCount = 10 },
            new() { Id = Guid.NewGuid(), UserId = "user1", Label = "Snapshot 2", CreatedAt = DateTime.UtcNow, FactCount = 15 }
        };

        _mockSnapshotService
            .Setup(s => s.ListSnapshotsAsync("user1", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        // Act
        var result = await _tools.ListProfileSnapshots("user1", 10);

        // Assert
        result.Success.Should().BeTrue();
        result.SnapshotCount.Should().Be(2);
        result.Snapshots.Should().HaveCount(2);
    }

    #endregion

    #region CompareSnapshots Tests

    [Fact]
    public async Task CompareSnapshots_ShouldReturnDiff()
    {
        // Arrange
        var olderId = Guid.NewGuid();
        var diff = new ProfileDiff
        {
            UserId = "user1",
            OlderTimestamp = DateTime.UtcNow.AddDays(-1),
            NewerTimestamp = DateTime.UtcNow,
            AddedFacts = [new SemanticStoreEntry { Key = "new", Value = "New fact" }],
            RemovedFacts = [],
            ModifiedFacts = [new FactChange { Key = "modified", OldValue = "Old", NewValue = "New", ChangeType = FactChangeType.ValueChange }],
            Summary = new DiffSummary { AddedCount = 1, RemovedCount = 0, ModifiedCount = 1 }
        };

        _mockSnapshotService
            .Setup(s => s.CompareSnapshotsAsync("user1", olderId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(diff);

        // Act
        var result = await _tools.CompareSnapshots(olderId.ToString(), "user1");

        // Assert
        result.Success.Should().BeTrue();
        result.HasChanges.Should().BeTrue();
        result.Summary!.AddedCount.Should().Be(1);
        result.Summary.ModifiedCount.Should().Be(1);
        result.Summary.TotalChanges.Should().Be(2);
    }

    [Fact]
    public async Task CompareSnapshots_WithInvalidGuid_ShouldReturnError()
    {
        // Act
        var result = await _tools.CompareSnapshots("invalid-guid", "user1");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region GetStaleFacts Tests

    [Fact]
    public async Task GetStaleFacts_WithTimeBasedStrategy_ShouldReturnStaleFacts()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "stale", Value = "Stale fact", Confidence = 0.6f, UpdatedAt = DateTime.UtcNow.AddDays(-200), IsActive = true }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        // Use real TimeBasedDecayStrategy for this test
        var options = Options.Create(new ConfidenceDecayOptions { HalfLifeDays = 90 });
        var realDecayStrategy = new TimeBasedDecayStrategy(options);

        var toolsWithRealDecay = new ProfileEvolutionTools(
            _mockInferenceEngine.Object,
            _mockSnapshotService.Object,
            realDecayStrategy,
            _mockProfileExporter.Object,
            _mockArchiveStore.Object);

        // Act
        var result = await toolsWithRealDecay.GetStaleFacts("user1", 0.5f);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalFacts.Should().Be(1);
    }

    [Fact]
    public async Task GetStaleFacts_WithNonTimeBasedStrategy_ShouldReturnEmpty()
    {
        // Arrange
        var facts = new List<SemanticStoreEntry>
        {
            new() { Key = "test", Value = "Test", IsActive = true }
        };

        _mockArchiveStore.Setup(a => a.GetAllAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(facts);

        // Act
        var result = await _tools.GetStaleFacts("user1");

        // Assert
        result.Success.Should().BeTrue();
        result.StaleCount.Should().Be(0); // Mock strategy doesn't implement GetStaleFacts
    }

    #endregion

    #region CalculateDecay Tests

    [Fact]
    public async Task CalculateDecay_ExistingFact_ShouldCalculateDecay()
    {
        // Arrange
        var fact = new SemanticStoreEntry
        {
            Key = "test",
            Value = "Test value",
            Confidence = 0.9f,
            UpdatedAt = DateTime.UtcNow.AddDays(-30)
        };

        _mockArchiveStore.Setup(a => a.GetAsync("user1", "test", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fact);

        _mockDecayStrategy.Setup(d => d.CalculateDecayedConfidence(fact, It.IsAny<DateTime>()))
            .Returns(0.8f);
        _mockDecayStrategy.Setup(d => d.NeedsReconfirmation(fact, It.IsAny<float>()))
            .Returns(false);

        // Act
        var result = await _tools.CalculateDecay("test", "user1");

        // Assert
        result.Success.Should().BeTrue();
        result.Key.Should().Be("test");
        result.OriginalConfidence.Should().Be(0.9f);
        result.DecayedConfidence.Should().Be(0.8f);
        result.DecayAmount.Should().BeApproximately(0.1f, 0.01f);
    }

    [Fact]
    public async Task CalculateDecay_NonExistentFact_ShouldReturnError()
    {
        // Arrange
        _mockArchiveStore.Setup(a => a.GetAsync("user1", "nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SemanticStoreEntry?)null);

        // Act
        var result = await _tools.CalculateDecay("nonexistent", "user1");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("nonexistent");
    }

    #endregion

    #region ExportProfile Tests

    [Fact]
    public async Task ExportProfile_ShouldExportUserData()
    {
        // Arrange
        var exportResult = new ProfileExportResult
        {
            Success = true,
            Data = "{\"activeFacts\": []}",
            Metadata = new ProfileExportMetadata
            {
                UserId = "user1",
                Format = "json",
                FactCount = 5,
                ActiveFactCount = 4,
                ArchivedFactCount = 1,
                SizeBytes = 1024,
                Checksum = "abc123"
            }
        };

        _mockProfileExporter
            .Setup(e => e.ExportAsync("user1", It.IsAny<ProfileExportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exportResult);

        // Act
        var result = await _tools.ExportProfile("user1");

        // Assert
        result.Success.Should().BeTrue();
        result.UserId.Should().Be("user1");
        result.Format.Should().Be("json");
        result.FactCount.Should().Be(5);
        result.ActiveFactCount.Should().Be(4);
        result.ArchivedFactCount.Should().Be(1);
        result.SizeBytes.Should().Be(1024);
        result.Checksum.Should().Be("abc123");
        result.Data.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExportProfile_WithOptions_ShouldPassOptions()
    {
        // Arrange
        _mockProfileExporter
            .Setup(e => e.ExportAsync(
                "user1",
                It.Is<ProfileExportOptions>(o => o.IncludeArchived && o.IncludeHistory),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProfileExportResult
            {
                Success = true,
                Data = "{}",
                Metadata = new ProfileExportMetadata { UserId = "user1", Format = "json" }
            });

        // Act
        var result = await _tools.ExportProfile("user1", includeArchived: true, includeHistory: true);

        // Assert
        result.Success.Should().BeTrue();
        _mockProfileExporter.Verify(
            e => e.ExportAsync("user1", It.Is<ProfileExportOptions>(o => o.IncludeArchived && o.IncludeHistory), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExportProfile_OnFailure_ShouldReturnError()
    {
        // Arrange
        _mockProfileExporter
            .Setup(e => e.ExportAsync("user1", It.IsAny<ProfileExportOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProfileExportResult
            {
                Success = false,
                ErrorMessage = "Export failed"
            });

        // Act
        var result = await _tools.ExportProfile("user1");

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Export failed");
    }

    #endregion
}
