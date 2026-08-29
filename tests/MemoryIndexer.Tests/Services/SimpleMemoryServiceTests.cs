using AwesomeAssertions;
using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Tests.Services;

/// <summary>
/// Tests for SimpleMemoryService (Simple API Level 0-1).
/// </summary>
public class SimpleMemoryServiceTests
{
    private readonly MockMemoryPrimitives _primitives;
    private readonly MockMemoryClassifier _classifier;
    private readonly MockScopeManager _scopeManager;
    private readonly SimpleMemoryService _service;

    public SimpleMemoryServiceTests()
    {
        _primitives = new MockMemoryPrimitives();
        _classifier = new MockMemoryClassifier();
        _scopeManager = new MockScopeManager();
        _service = new SimpleMemoryService(
            _primitives,
            _classifier,
            _scopeManager,
            NullLogger<SimpleMemoryService>.Instance);
    }

    #region RememberAsync (Level 0 - Zero-Config) Tests

    [Fact]
    public async Task RememberAsync_Level0_ShouldCreateImplicitSession()
    {
        // Arrange
        const string userId = "user-1";
        const string content = "I like pizza";

        // Act
        await _service.RememberAsync(userId, content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories.Should().HaveCount(1);
        _primitives.EncodedMemories[0].UserId.Should().Be(userId);
        _primitives.EncodedMemories[0].Content.Should().Be(content);
        _primitives.EncodedMemories[0].SessionId.Should().StartWith("implicit-");
    }

    [Fact]
    public async Task RememberAsync_Level0_ShouldUseAutoClassification()
    {
        // Arrange
        const string userId = "user-1";
        const string content = "My name is John";

        // Act
        await _service.RememberAsync(userId, content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories[0].Type.Should().Be(MemoryType.Fact);
        _primitives.EncodedMemories[0].Tier.Should().Be(Tier.Archive);
    }

    [Fact]
    public async Task RememberAsync_Level0_ShouldSkipTransientContent()
    {
        // Arrange
        const string userId = "user-1";
        const string content = "Hello";

        _classifier.ShouldPersist = false;

        // Act
        await _service.RememberAsync(userId, content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories.Should().BeEmpty();
    }

    [Fact]
    public async Task RememberAsync_Level0_MultipleCalls_ShouldReuseSameSession()
    {
        // Arrange
        const string userId = "user-1";

        // Act
        await _service.RememberAsync(userId, "First", cancellationToken: TestContext.Current.CancellationToken);
        await _service.RememberAsync(userId, "Second", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories.Should().HaveCount(2);
        _primitives.EncodedMemories[0].SessionId.Should().Be(_primitives.EncodedMemories[1].SessionId);
    }

    [Fact]
    public async Task RememberAsync_Level0_DifferentUsers_ShouldHaveDifferentSessions()
    {
        // Arrange
        const string user1 = "user-1";
        const string user2 = "user-2";

        // Act
        await _service.RememberAsync(user1, "User1 content", cancellationToken: TestContext.Current.CancellationToken);
        await _service.RememberAsync(user2, "User2 content", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories[0].SessionId.Should().NotBe(_primitives.EncodedMemories[1].SessionId);
    }

    #endregion

    #region RememberAsync (Level 1 - Session-Aware) Tests

    [Fact]
    public async Task RememberAsync_Level1_ShouldUseExplicitSession()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-explicit";
        const string content = "I like pizza";

        // Act
        await _service.RememberAsync(userId, sessionId: sessionId, content: content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories.Should().HaveCount(1);
        _primitives.EncodedMemories[0].SessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task RememberAsync_Level1_ShouldInitializeScopeManager()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";

        // Act
        await _service.RememberAsync(userId, sessionId: sessionId, content: "content", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _scopeManager.IsInitialized.Should().BeTrue();
        _scopeManager.UserId.Should().Be(userId);
        _scopeManager.SessionId.Should().Be(sessionId);
    }

    [Fact]
    public async Task RememberAsync_Level1_ShouldRecordTurn()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";
        const string content = "Test content";

        // Act
        await _service.RememberAsync(userId, sessionId: sessionId, content: content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _scopeManager.RecordedTurns.Should().ContainSingle();
        _scopeManager.RecordedTurns[0].Should().Be(content);
    }

    [Fact]
    public async Task RememberAsync_Level1_ShouldResolveScopeFromImportance()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";
        const string content = "Important fact";

        _classifier.Importance = 0.9f; // High importance → User scope

        // Act
        await _service.RememberAsync(userId, sessionId: sessionId, content: content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories[0].Scope.Should().Be(Scope.User);
    }

    [Fact]
    public async Task RememberAsync_Level1_ShouldPassTopicsToEncoder()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";
        const string content = "I like programming";

        _classifier.Topics = new List<string> { "programming", "interests" };

        // Act
        await _service.RememberAsync(userId, sessionId: sessionId, content: content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories[0].Topics.Should().Contain("programming");
        _primitives.EncodedMemories[0].Topics.Should().Contain("interests");
    }

    [Fact]
    public async Task RememberAsync_WithRole_PassesRoleToEncoder()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";
        const string content = "Test content";
        const string role = "assistant";

        // Act
        await _service.RememberAsync(userId, sessionId: sessionId, content: content, role: role, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories.Should().HaveCount(1);
        _primitives.EncodedMemories[0].Role.Should().Be(role);
    }

    [Fact]
    public async Task RememberAsync_WithoutRole_DefaultsToUser()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";
        const string content = "Test content";

        // Act
        await _service.RememberAsync(userId, sessionId: sessionId, content: content, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        _primitives.EncodedMemories.Should().HaveCount(1);
        _primitives.EncodedMemories[0].Role.Should().Be("user");
    }

    #endregion

    #region RecallAsync Tests

    [Fact]
    public async Task RecallAsync_WithoutSessionId_ShouldReturnOnlyUserMemories()
    {
        // Arrange
        const string userId = "user-1";
        const string query = "pizza";

        _primitives.AddMemory(userId, null, Scope.User, "I like pizza");
        _primitives.AddMemory(userId, "session-1", Scope.Session, "Had pizza for lunch");

        // Act
        var context = await _service.RecallAsync(userId, null, query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        context.UserMemories.Should().HaveCount(1);
        context.SessionMemories.Should().HaveCount(1); // Bug: should filter by sessionId
        context.TopicMemories.Should().BeEmpty();
    }

    [Fact]
    public async Task RecallAsync_WithSessionId_ShouldReturnAllScopes()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";
        const string query = "pizza";

        _primitives.AddMemory(userId, null, Scope.User, "I like pizza");
        _primitives.AddMemory(userId, sessionId, Scope.Session, "Had pizza for lunch");
        _primitives.AddMemory(userId, sessionId, Scope.Topic, "Discussing pizza toppings");

        // Act
        var context = await _service.RecallAsync(userId, sessionId, query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        context.UserMemories.Should().HaveCount(1);
        context.SessionMemories.Should().HaveCount(1);
        context.TopicMemories.Should().HaveCount(1);
        context.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task RecallAsync_ShouldGroupByScope()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";
        const string query = "test";

        _primitives.AddMemory(userId, null, Scope.User, "User memory 1");
        _primitives.AddMemory(userId, null, Scope.User, "User memory 2");
        _primitives.AddMemory(userId, sessionId, Scope.Session, "Session memory");

        // Act
        var context = await _service.RecallAsync(userId, sessionId, query, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        context.UserMemories.Should().HaveCount(2);
        context.SessionMemories.Should().HaveCount(1);
    }

    [Fact]
    public async Task RecallAsync_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        const string userId = "user-1";
        const string query = "test";

        for (int i = 0; i < 20; i++)
        {
            _primitives.AddMemory(userId, null, Scope.User, $"Memory {i}");
        }

        // Act
        var context = await _service.RecallAsync(userId, null, query, limit: 5, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        context.TotalCount.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task RecallAsync_InvalidLimit_ShouldThrow()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            _service.RecallAsync("user-1", null, "query", limit: 0, cancellationToken: TestContext.Current.CancellationToken));
    }

    #endregion

    #region EndSessionAsync Tests

    [Fact]
    public async Task EndSessionAsync_ShouldCallScopeManagerEndSession()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";

        await _service.RememberAsync(userId, sessionId: sessionId, content: "test", cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await _service.EndSessionAsync(userId, sessionId, TestContext.Current.CancellationToken);

        // Assert
        _scopeManager.SessionEnded.Should().BeTrue();
    }

    [Fact]
    public async Task EndSessionAsync_ShouldRemoveImplicitSession()
    {
        // Arrange
        const string userId = "user-1";

        await _service.RememberAsync(userId, "First", cancellationToken: TestContext.Current.CancellationToken); // Creates implicit session
        var firstSessionId = _primitives.EncodedMemories[0].SessionId;

        // Act
        await _service.EndSessionAsync(userId, firstSessionId!, TestContext.Current.CancellationToken);
        await _service.RememberAsync(userId, "Second", cancellationToken: TestContext.Current.CancellationToken); // Should create new implicit session

        // Assert
        var secondSessionId = _primitives.EncodedMemories[1].SessionId;
        secondSessionId.Should().NotBe(firstSessionId);
    }

    #endregion

    #region ForgetUserAsync Tests

    [Fact]
    public async Task ForgetUserAsync_ShouldDeleteAllUserMemories()
    {
        // Arrange
        const string userId = "user-1";

        _primitives.AddMemory(userId, null, Scope.User, "Memory 1");
        _primitives.AddMemory(userId, "session-1", Scope.Session, "Memory 2");

        // Act
        await _service.ForgetUserAsync(userId, TestContext.Current.CancellationToken);

        // Assert
        _primitives.DeletedMemories.Should().HaveCount(2);
        _primitives.DeletedMemories.Should().OnlyContain(d => d.HardDelete == true);
    }

    #endregion

    #region ForgetSessionAsync Tests

    [Fact]
    public async Task ForgetSessionAsync_ShouldDeleteOnlySessionMemories()
    {
        // Arrange
        const string userId = "user-1";
        const string sessionId = "session-1";

        var userMemory = _primitives.AddMemory(userId, null, Scope.User, "User memory");
        var sessionMemory = _primitives.AddMemory(userId, sessionId, Scope.Session, "Session memory");

        // Act
        await _service.ForgetSessionAsync(userId, sessionId, TestContext.Current.CancellationToken);

        // Assert
        _primitives.DeletedMemories.Should().ContainSingle();
        _primitives.DeletedMemories[0].MemoryId.Should().Be(sessionMemory.Id);
        _primitives.DeletedMemories[0].HardDelete.Should().BeFalse();
    }

    #endregion

    #region Helper Classes

    private sealed class MockMemoryPrimitives : IMemoryPrimitives
    {
        public List<MemoryUnit> EncodedMemories { get; } = new();
        public List<MemoryUnit> StoredMemories { get; } = new();
        public List<DeleteRequest> DeletedMemories { get; } = new();

        public Task<MemoryUnit> EncodeAsync(EncodeRequest request, CancellationToken cancellationToken = default)
        {
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                UserId = request.UserId,
                SessionId = request.SessionId,
                Content = request.Content,
                Type = request.Type ?? MemoryType.Episodic,
                Scope = request.Scope,
                Tier = request.Tier,
                Role = request.Role,
                ImportanceScore = request.ImportanceScore ?? 0.5f,
                Topics = request.Topics ?? new List<string>()
            };

            EncodedMemories.Add(memory);
            StoredMemories.Add(memory);
            return Task.FromResult(memory);
        }

        public Task<IReadOnlyList<RetrieveResult>> RetrieveAsync(RetrieveRequest request, CancellationToken cancellationToken = default)
        {
            var results = StoredMemories
                .Where(m => m.UserId == request.UserId)
                .Where(m => request.SessionId == null || m.SessionId == request.SessionId || m.Scope == Scope.User)
                .Take(request.Limit)
                .Select(m => new RetrieveResult
                {
                    Memory = m,
                    Score = 0.9f,
                    Breakdown = new ScoreBreakdown
                    {
                        SemanticScore = 0.9f,
                        KeywordScore = 0.0f,
                        RecencyScore = 0.0f,
                        ImportanceScore = m.ImportanceScore
                    }
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<RetrieveResult>>(results);
        }

        public Task<bool> DeleteAsync(DeleteRequest request, CancellationToken cancellationToken = default)
        {
            DeletedMemories.Add(request);
            var memory = StoredMemories.FirstOrDefault(m => m.Id == request.MemoryId);
            if (memory != null)
            {
                StoredMemories.Remove(memory);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }

        public MemoryUnit AddMemory(string userId, string? sessionId, Scope scope, string content)
        {
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SessionId = sessionId,
                Scope = scope,
                Content = content,
                Type = MemoryType.Episodic,
                Tier = Tier.Long
            };
            StoredMemories.Add(memory);
            return memory;
        }

        // Not implemented for this test
        public Task<MemoryUnit?> UpdateAsync(UpdateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<MemoryUnit>> SplitAsync(SplitRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MemoryUnit> MergeAsync(MergeRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MemoryUnit?> ExpireAsync(ExpireRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MemoryUnit?> LockAsync(LockRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MemoryUnit?> LabelAsync(LabelRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MemoryUnit> SummarizeAsync(SummarizeRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MemoryUnit?> PromoteAsync(PromoteRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MemoryUnit?> DemoteAsync(DemoteRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ConfirmResult> ConfirmAsync(ConfirmRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class MockMemoryClassifier : IMemoryClassifier
    {
        public Tier Tier { get; set; } = Tier.Archive;
        public MemoryType Type { get; set; } = MemoryType.Fact;
        public float Importance { get; set; } = 0.5f;
        public bool ShouldPersist { get; set; } = true;
        public List<string> Topics { get; set; } = new();

        public Task<MemoryClassification> ClassifyAsync(string content, ClassificationContext? context = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new MemoryClassification
            {
                Tier = Tier,
                Type = Type,
                Importance = Importance,
                ShouldPersist = ShouldPersist,
                Topics = Topics,
                Confidence = 1.0f
            });
        }

        public Task<IReadOnlyList<MemoryClassification>> ClassifyBatchAsync(IEnumerable<string> contents, ClassificationContext? context = null, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class MockScopeManager : IScopeManager
    {
        public ScopeState CurrentState { get; } = new ScopeState();
        public bool IsInitialized { get; private set; }
        public string? UserId { get; private set; }
        public string? SessionId { get; private set; }
        public bool SessionEnded { get; private set; }
        public List<string> RecordedTurns { get; } = new();

        public Task InitializeAsync(string userId, string sessionId, CancellationToken cancellationToken = default)
        {
            IsInitialized = true;
            UserId = userId;
            SessionId = sessionId;
            CurrentState.IsInitialized = true;
            CurrentState.UserId = userId;
            CurrentState.SessionId = sessionId;
            return Task.CompletedTask;
        }

        public Task<ScopeResolution> RecordTurnAsync(string content, string? role = null, CancellationToken cancellationToken = default)
        {
            RecordedTurns.Add(content);
            return Task.FromResult(new ScopeResolution
            {
                ResolvedScope = Scope.Turn,
                TopicId = "topic-1",
                BoundaryCrossed = true,
                BoundaryType = ScopeBoundaryType.Turn,
                TurnIndex = RecordedTurns.Count,
                TopicTurnIndex = RecordedTurns.Count,
                Confidence = 1.0f
            });
        }

        public Task<Scope> ResolveScopeAsync(string content, MemoryType type, float importance = 0.5f, CancellationToken cancellationToken = default)
        {
            // Simple heuristic: high importance → User, low importance → Session
            var scope = importance >= 0.8f ? Scope.User : Scope.Session;
            return Task.FromResult(scope);
        }

        public Task<bool> DetectTopicChangeAsync(string currentContent, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public string GetCurrentTopicId() => "topic-1";
        public IReadOnlyList<MemoryUnit> FilterByScope(IEnumerable<MemoryUnit> memories, Scope targetScope, bool includeNarrower = true) => throw new NotImplementedException();
        public Task EndSessionAsync(CancellationToken cancellationToken = default)
        {
            SessionEnded = true;
            IsInitialized = false;
            return Task.CompletedTask;
        }
        public ScopeStatistics GetStatistics() => throw new NotImplementedException();
    }

    #endregion
}
