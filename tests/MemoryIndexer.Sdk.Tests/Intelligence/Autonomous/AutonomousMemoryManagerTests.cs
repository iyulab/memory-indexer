using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Autonomous;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Autonomous;

public class AutonomousMemoryManagerTests
{
    private readonly AutonomousMemoryManager _manager;
    private readonly Mock<IMemoryStore> _memoryStoreMock;
    private readonly Mock<ITieredMemoryStore> _tieredStoreMock;
    private readonly Mock<IVirtualContextManager> _contextManagerMock;
    private readonly Mock<IScoringService> _scoringServiceMock;

    public AutonomousMemoryManagerTests()
    {
        _memoryStoreMock = new Mock<IMemoryStore>();
        _tieredStoreMock = new Mock<ITieredMemoryStore>();
        _contextManagerMock = new Mock<IVirtualContextManager>();
        _scoringServiceMock = new Mock<IScoringService>();

        SetupDefaultMocks();

        _manager = new AutonomousMemoryManager(
            _memoryStoreMock.Object,
            _tieredStoreMock.Object,
            _contextManagerMock.Object,
            _scoringServiceMock.Object,
            NullLogger<AutonomousMemoryManager>.Instance);
    }

    private void SetupDefaultMocks()
    {
        _memoryStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _memoryStoreMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new MemoryUnit
            {
                Id = id,
                Content = "Test memory content",
                Stability = MemoryStability.Stable
            });

        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _tieredStoreMock
            .Setup(x => x.DemoteAsync(It.IsAny<MemoryUnit>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MemoryUnit m, CancellationToken _) => m);

        _contextManagerMock
            .Setup(x => x.GetContextUsageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContextUsageStatistics
            {
                TotalTokens = 1000,
                WorkingMemoryTokens = 500,
                AvailableTokens = 9000,
                WorkingMemoryCount = 5,
                SessionMemoryCount = 10,
                UserMemoryCount = 50,
                SaturationLevel = ContextSaturationLevel.Normal,
                SaturationPercentage = 10
            });

        _contextManagerMock
            .Setup(x => x.State)
            .Returns(new VirtualContextState
            {
                IsInitialized = true,
                WorkingMemoryTokens = 1000,
                MaxTokenCapacity = 10000,
                SaturationLevel = ContextSaturationLevel.Normal
            });

        _contextManagerMock
            .Setup(x => x.PageOutAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _scoringServiceMock
            .Setup(x => x.CalculateScore(It.IsAny<MemoryUnit>(), It.IsAny<ReadOnlyMemory<float>?>()))
            .Returns(0.75f);
    }

    [Fact]
    public void CurrentState_InitialState_ShouldBeValid()
    {
        // Act
        var state = _manager.CurrentState;

        // Assert
        Assert.NotNull(state);
    }

    [Fact]
    public async Task HeartbeatAsync_FirstCall_ShouldReturnValidResponse()
    {
        // Act
        var response = await _manager.HeartbeatAsync("test context");

        // Assert
        Assert.NotNull(response);
        Assert.True(response.NextHeartbeatIn > TimeSpan.Zero);
        Assert.NotNull(response.Alerts);
    }

    [Fact]
    public async Task HeartbeatAsync_ShouldUpdateCurrentState()
    {
        // Act
        await _manager.HeartbeatAsync("test context");
        var state = _manager.CurrentState;

        // Assert
        Assert.NotEqual(DateTime.MinValue, state.LastHeartbeat);
    }

    [Fact]
    public async Task HeartbeatAsync_WithDifferentContexts_ShouldMaintainState()
    {
        // Act
        var response1 = await _manager.HeartbeatAsync("context 1");
        var response2 = await _manager.HeartbeatAsync("context 2");

        // Assert
        Assert.NotNull(response1);
        Assert.NotNull(response2);
    }

    [Fact]
    public async Task AutonomousPageInAsync_ValidQuery_ShouldReturnSuccess()
    {
        // Arrange
        var testMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Relevant test content",
            Stability = MemoryStability.Stable
        };

        _memoryStoreMock
            .Setup(x => x.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<MemorySearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MemorySearchResult { Memory = testMemory, Score = 0.9f }]);

        // Act
        var result = await _manager.AutonomousPageInAsync("test query");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.PagedInMemories);
    }

    [Fact]
    public async Task AutonomousPageOutAsync_ValidRequest_ShouldReturnSuccess()
    {
        // Arrange
        _contextManagerMock
            .Setup(x => x.PageOutAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new MemoryUnit { Id = Guid.NewGuid(), Content = "Old memory" },
                new MemoryUnit { Id = Guid.NewGuid(), Content = "Newer memory" }
            ]);

        // Act
        var result = await _manager.AutonomousPageOutAsync(500);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task OptimizeMemoryAsync_DefaultOptions_ShouldReturnResult()
    {
        // Arrange
        var testMemories = Enumerable.Range(0, 5)
            .Select(i => new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = $"Memory {i}",
                Stability = MemoryStability.Stabilizing
            })
            .ToList();

        _memoryStoreMock
            .Setup(x => x.GetAllAsync(
                It.IsAny<string>(),
                It.IsAny<MemoryFilterOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testMemories);

        // Act
        var result = await _manager.OptimizeMemoryAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ActionsTaken);
    }

    [Fact]
    public async Task RecordAccessAsync_ValidMemoryId_ShouldSucceed()
    {
        // Arrange
        var memoryId = Guid.NewGuid();

        // Act
        await _manager.RecordAccessAsync(memoryId, MemoryAccessType.Read, "test context");
        var stats = await _manager.GetAccessStatisticsAsync();

        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.TotalAccesses >= 1);
    }

    [Fact]
    public async Task RecordAccessAsync_MultipleAccesses_ShouldAccumulate()
    {
        // Arrange
        var memoryId = Guid.NewGuid();

        // Act
        await _manager.RecordAccessAsync(memoryId, MemoryAccessType.Read);
        await _manager.RecordAccessAsync(memoryId, MemoryAccessType.Read);
        await _manager.RecordAccessAsync(memoryId, MemoryAccessType.Write);
        var stats = await _manager.GetAccessStatisticsAsync();

        // Assert
        Assert.True(stats.TotalAccesses >= 3);
    }

    [Fact]
    public async Task GetAccessStatisticsAsync_NoAccess_ShouldReturnEmpty()
    {
        // Act - use a new manager that hasn't recorded any access
        var stats = await _manager.GetAccessStatisticsAsync();

        // Assert
        Assert.NotNull(stats);
    }

    [Fact]
    public async Task GetSuggestedOperationsAsync_ShouldReturnSuggestions()
    {
        // Act
        var suggestions = await _manager.GetSuggestedOperationsAsync();

        // Assert
        Assert.NotNull(suggestions);
    }

    [Fact]
    public async Task RequestOperationAsync_RetrieveOperation_ShouldSucceed()
    {
        // Arrange
        var memoryId = Guid.NewGuid();
        var request = new MemoryOperationRequest
        {
            OperationType = MemoryOperationType.Retrieve,
            UserId = "test_user",
            TargetMemoryIds = [memoryId]
        };

        // Act
        var result = await _manager.RequestOperationAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task RequestOperationAsync_PageInOperation_ShouldSucceed()
    {
        // Arrange
        var request = new MemoryOperationRequest
        {
            OperationType = MemoryOperationType.PageIn,
            UserId = "test_user",
            Query = "test query"
        };

        // Act
        var result = await _manager.RequestOperationAsync(request);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RequestOperationAsync_PageOutOperation_ShouldSucceed()
    {
        // Arrange
        var request = new MemoryOperationRequest
        {
            OperationType = MemoryOperationType.PageOut,
            UserId = "test_user"
        };

        // Act
        var result = await _manager.RequestOperationAsync(request);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RequestOperationAsync_OptimizeOperation_ShouldSucceed()
    {
        // Arrange
        var request = new MemoryOperationRequest
        {
            OperationType = MemoryOperationType.Optimize,
            UserId = "test_user"
        };

        // Act
        var result = await _manager.RequestOperationAsync(request);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RequestOperationAsync_ArchiveOperation_ShouldSucceed()
    {
        // Arrange
        var memoryId = Guid.NewGuid();
        var request = new MemoryOperationRequest
        {
            OperationType = MemoryOperationType.Archive,
            UserId = "test_user",
            TargetMemoryIds = [memoryId]
        };

        // Act
        var result = await _manager.RequestOperationAsync(request);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RequestOperationAsync_DeleteOperation_ShouldSucceed()
    {
        // Arrange
        var memoryId = Guid.NewGuid();
        var request = new MemoryOperationRequest
        {
            OperationType = MemoryOperationType.Delete,
            UserId = "test_user",
            TargetMemoryIds = [memoryId]
        };

        _memoryStoreMock
            .Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _manager.RequestOperationAsync(request);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task HeartbeatAsync_RepeatedCalls_ShouldTrackScheduling()
    {
        // Act
        var response1 = await _manager.HeartbeatAsync("context");
        await Task.Delay(100); // Small delay
        var response2 = await _manager.HeartbeatAsync("context");

        // Assert
        Assert.NotNull(response1);
        Assert.NotNull(response2);
        // Both should have valid intervals
        Assert.True(response1.NextHeartbeatIn > TimeSpan.Zero);
        Assert.True(response2.NextHeartbeatIn > TimeSpan.Zero);
    }

    [Fact]
    public async Task AutonomousPageInAsync_EmptyResult_ShouldStillSucceed()
    {
        // Arrange - default mock returns empty list

        // Act
        var result = await _manager.AutonomousPageInAsync("query with no results");

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.PagedInMemories);
    }
}
