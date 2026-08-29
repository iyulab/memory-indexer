using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Conflict;
using MemoryIndexer.Sdk.Intelligence.Operations;
using MemoryIndexer.Scoring;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Operations;

/// <summary>
/// Unit tests for SemanticOperationDecider.
/// </summary>
public class SemanticOperationDeciderTests
{
    private readonly IMemoryStore _memoryStore;
    private readonly IEmbeddingService _embeddingService;
    private readonly IContradictionDetector _contradictionDetector;
    private readonly ImportanceAnalyzer _importanceAnalyzer;
    private readonly SemanticOperationDecider _decider;

    public SemanticOperationDeciderTests()
    {
        _memoryStore = Substitute.For<IMemoryStore>();
        _embeddingService = Substitute.For<IEmbeddingService>();
        _contradictionDetector = Substitute.For<IContradictionDetector>();
        _importanceAnalyzer = new ImportanceAnalyzer(NullLogger<ImportanceAnalyzer>.Instance);

        _decider = new SemanticOperationDecider(
            _memoryStore,
            _embeddingService,
            _contradictionDetector,
            _importanceAnalyzer,
            NullLogger<SemanticOperationDecider>.Instance);
    }

    [Fact]
    public async Task DecideAsync_NoSimilarMemories_ReturnsAdd()
    {
        // Arrange
        var content = "This is important information about user preferences.";
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.1f);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MemorySearchResult>());

        // Act
        var decision = await _decider.DecideAsync(content, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MemoryOperation.Add, decision.Operation);
        Assert.True(decision.Confidence >= 0.9f);
        Assert.Contains("novel", decision.Reasoning.ToLowerInvariant());
    }

    [Fact]
    public async Task DecideAsync_LowImportanceContent_ReturnsNoop()
    {
        // Arrange
        var content = "ok"; // Very short, low importance
        var userId = "user-1";
        var options = new DecisionOptions { MinimumImportance = 0.5f };

        // Act
        var decision = await _decider.DecideAsync(content, userId, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MemoryOperation.Noop, decision.Operation);
        Assert.Contains("importance", decision.Reasoning.ToLowerInvariant());
    }

    [Fact]
    public async Task DecideAsync_DuplicateContent_ReturnsNoop()
    {
        // Arrange - Content that is very similar to existing (short, identical meaning)
        var content = "User prefers dark mode.";
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.5f);

        // Use same content for existing memory so "additional info" check returns false
        var existingMemory = CreateMemory("User prefers dark mode in the IDE and always uses dark themes.", embedding);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CreateSearchResult(existingMemory, 0.90f) });

        // Act
        var decision = await _decider.DecideAsync(content, userId, new DecisionOptions { DuplicateThreshold = 0.85f }, TestContext.Current.CancellationToken);

        // Assert - When new content is shorter than existing and has no new info, return Noop
        Assert.Equal(MemoryOperation.Noop, decision.Operation);
        Assert.Contains("duplicate", decision.Reasoning.ToLowerInvariant());
    }

    [Fact]
    public async Task DecideAsync_SimilarButEnriching_ReturnsUpdate()
    {
        // Arrange
        var content = "User prefers dark mode in the IDE. They also like blue accent colors and high contrast themes for accessibility reasons.";
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.5f);

        var existingMemory = CreateMemory("User prefers dark mode.", embedding, importance: 0.5f);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CreateSearchResult(existingMemory, 0.88f) });

        // Act
        var decision = await _decider.DecideAsync(content, userId, new DecisionOptions { DuplicateThreshold = 0.85f }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MemoryOperation.Update, decision.Operation);
        Assert.NotNull(decision.TargetMemory);
        Assert.Contains("additional", decision.Reasoning.ToLowerInvariant());
    }

    [Fact]
    public async Task DecideAsync_MultipleRelatedMemories_ProcessesCorrectly()
    {
        // Arrange - When same embeddings, internal cosine similarity = 1.0 (duplicate range)
        var content = "User likes TypeScript for web development with additional details.";
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.5f);

        // With identical embeddings, similarity will be 1.0 internally
        // So the code will detect duplicates and check for additional info
        var related1 = CreateMemory("User prefers TypeScript.", embedding);
        var related2 = CreateMemory("User uses React.", embedding);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                CreateSearchResult(related1, 0.75f),
                CreateSearchResult(related2, 0.72f)
            });

        // Act
        var decision = await _decider.DecideAsync(content, userId, new DecisionOptions
        {
            DuplicateThreshold = 0.85f,
            RelatedThreshold = 0.70f,
            DetectContradictions = false
        }, TestContext.Current.CancellationToken);

        // Assert - With same embeddings (sim=1.0), new content has additional info, so Update
        Assert.Equal(MemoryOperation.Update, decision.Operation);
        Assert.NotNull(decision.TargetMemory);
    }

    [Fact]
    public async Task DecideAsync_ContradictionDetected_ReturnsReplace()
    {
        // Arrange
        var content = "User now prefers light mode.";
        var userId = "user-1";

        // Create embeddings that produce cosine similarity ~0.8 (in related range, not duplicate)
        // Vector A = [1, 0, 0, ...], Vector B = [0.8, 0.6, 0, ...]
        // cos(θ) = 0.8 / (1 * 1) = 0.8
        var newEmbedding = new float[1024];
        newEmbedding[0] = 1.0f; // Unit vector in first dimension

        var existingEmbedding = new float[1024];
        existingEmbedding[0] = 0.8f;
        existingEmbedding[1] = 0.6f; // Rotated slightly - cosine similarity ~0.8

        var existingMemory = CreateMemory("User prefers dark mode.", existingEmbedding, importance: 0.4f);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(newEmbedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CreateSearchResult(existingMemory, 0.80f) });

        var contradictionAnalysis = new ContradictionAnalysis<MemoryUnit>
        {
            HasContradiction = true,
            NewItem = new MemoryUnit { Id = Guid.NewGuid(), Content = content, UserId = userId },
            ConflictingItem = existingMemory,
            ContradictionConfidence = 0.85f,
            ConflictDescription = "Light mode vs dark mode preference conflict"
        };

        _contradictionDetector.DetectMemoryContradictionAsync(
            Arg.Any<MemoryUnit>(),
            Arg.Any<IReadOnlyList<MemoryUnit>>(),
            Arg.Any<ContradictionDetectionOptions?>(),
            Arg.Any<CancellationToken>())
            .Returns(contradictionAnalysis);

        // Act
        var decision = await _decider.DecideAsync(content, userId, new DecisionOptions
        {
            DuplicateThreshold = 0.85f,
            RelatedThreshold = 0.70f,
            DetectContradictions = true
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(decision.ContradictionDetected);
        Assert.NotNull(decision.ContradictionDetails);
        // New content has higher importance, so Replace should be recommended
        Assert.Equal(MemoryOperation.Replace, decision.Operation);
    }

    [Fact]
    public async Task DecideAsync_ExtractsTopicsCorrectly()
    {
        // Arrange
        var content = "Configure the database connection and run the authentication API tests.";
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.1f);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MemorySearchResult>());

        // Act
        var decision = await _decider.DecideAsync(content, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains("database", decision.Topics);
        Assert.Contains("authentication", decision.Topics);
        Assert.Contains("api", decision.Topics);
        Assert.Contains("testing", decision.Topics);
        Assert.Contains("configuration", decision.Topics);
    }

    [Fact]
    public async Task DecideAsync_DetectsMemoryTypeCorrectly()
    {
        // Arrange - Procedural content
        var content = "How to deploy the application: Step 1 - Build, Step 2 - Test, Step 3 - Deploy";
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.1f);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MemorySearchResult>());

        // Act
        var decision = await _decider.DecideAsync(content, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MemoryType.Procedural, decision.SuggestedType);
    }

    [Fact]
    public async Task DecideBatchAsync_ProcessesAllContents()
    {
        // Arrange
        var contents = new List<string>
        {
            "First piece of content about configuration.",
            "Second piece about testing procedures.",
            "Third piece about deployment."
        };
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.1f);

        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MemorySearchResult>());

        // Act
        var decisions = await _decider.DecideBatchAsync(contents, userId, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, decisions.Count);
        Assert.All(decisions, d => Assert.Equal(MemoryOperation.Add, d.Operation));
    }

    [Fact]
    public async Task DecideAsync_RespectsSessionIdScope()
    {
        // Arrange
        var content = "Session-specific content.";
        var userId = "user-1";
        var sessionId = "session-123";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.1f);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MemorySearchResult>());

        var options = new DecisionOptions { SessionId = sessionId };

        // Act
        await _decider.DecideAsync(content, userId, options, TestContext.Current.CancellationToken);

        // Assert - Verify search was called with correct session scope
        await _memoryStore.Received(1).SearchAsync(
            Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Is<MemorySearchOptions>(o => o.SessionId == sessionId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecideAsync_RespectsPreferredMemoryType()
    {
        // Arrange
        var content = "Some content that could be any type.";
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.1f);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MemorySearchResult>());

        var options = new DecisionOptions { PreferredType = MemoryType.Fact };

        // Act
        var decision = await _decider.DecideAsync(content, userId, options, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MemoryType.Fact, decision.SuggestedType);
    }

    [Fact]
    public async Task DecideAsync_SingleRelatedMemory_HigherImportance_ReturnsUpdate()
    {
        // Arrange
        var content = "Updated user preference with more specific details about their workflow.";
        var userId = "user-1";
        var embedding = new float[1024];
        Array.Fill(embedding, 0.5f);

        var existingMemory = CreateMemory("Old preference", embedding, importance: 0.3f);

        _embeddingService.GenerateEmbeddingAsync(content, Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(embedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CreateSearchResult(existingMemory, 0.75f) });

        // Act
        var decision = await _decider.DecideAsync(content, userId, new DecisionOptions
        {
            DuplicateThreshold = 0.85f,
            RelatedThreshold = 0.70f,
            DetectContradictions = false
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MemoryOperation.Update, decision.Operation);
        Assert.NotNull(decision.SuggestedContent);
    }

    [Fact]
    public async Task DecideAsync_SingleRelatedMemory_LowerImportance_ReturnsAdd()
    {
        // Arrange - Content with lower importance than existing memory
        var content = "A related topic about something.";
        var userId = "user-1";

        // Create embeddings that produce cosine similarity ~0.75 (in related range)
        // Need vectors with different angles to get proper similarity
        var newEmbedding = new float[1024];
        newEmbedding[0] = 1.0f; // Unit vector in first dimension

        var existingEmbedding = new float[1024];
        existingEmbedding[0] = 0.75f;
        existingEmbedding[1] = 0.6614f; // sqrt(1 - 0.75^2) to normalize - cosine sim ~0.75

        var existingMemory = CreateMemory("Important existing memory with detailed information and context.", existingEmbedding, importance: 0.8f);

        _embeddingService.GenerateEmbeddingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(newEmbedding));
        _memoryStore.SearchAsync(Arg.Any<ReadOnlyMemory<float>>(), Arg.Any<MemorySearchOptions>(), Arg.Any<CancellationToken>())
            .Returns(new[] { CreateSearchResult(existingMemory, 0.75f) });

        // Act - Content has lower importance than existing memory
        var decision = await _decider.DecideAsync(content, userId, new DecisionOptions
        {
            DuplicateThreshold = 0.85f,
            RelatedThreshold = 0.70f,
            DetectContradictions = false,
            MinimumImportance = 0.1f
        }, TestContext.Current.CancellationToken);

        // Assert - With single related memory and lower importance, should add as distinct
        Assert.Equal(MemoryOperation.Add, decision.Operation);
        Assert.Contains("distinct", decision.Reasoning.ToLowerInvariant());
    }

    private static MemoryUnit CreateMemory(string content, float[] embedding, float importance = 0.5f)
    {
        return new MemoryUnit
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            Content = content,
            Embedding = new ReadOnlyMemory<float>(embedding),
            ImportanceScore = importance,
            Type = MemoryType.Semantic,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
    }

    private static MemorySearchResult CreateSearchResult(MemoryUnit memory, float score)
    {
        return new MemorySearchResult
        {
            Memory = memory,
            Score = score
        };
    }
}
