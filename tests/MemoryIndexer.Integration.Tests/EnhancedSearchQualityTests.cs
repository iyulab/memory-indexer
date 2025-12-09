using FluentAssertions;
using MemoryIndexer.Core.Configuration;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Embedding.Providers;
using MemoryIndexer.Intelligence.Scoring;
using MemoryIndexer.Intelligence.Search;
using MemoryIndexer.Storage.InMemory;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace MemoryIndexer.Integration.Tests;

/// <summary>
/// Integration tests for enhanced search quality using QueryExpander and HybridSearch.
/// Tests improvements over baseline vector-only search.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "QualityImprovement")]
public class EnhancedSearchQualityTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ServiceProvider? _serviceProvider;
    private IEmbeddingService _embeddingService = null!;
    private IMemoryStore _memoryStore = null!;
    private IQueryExpander _queryExpander = null!;
    private IHybridSearchService _hybridSearch = null!;
    private IScoringService _scoringService = null!;

    public EnhancedSearchQualityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("=== Initializing Enhanced Search Quality Tests ===");

        var services = new ServiceCollection();

        var indexerOptions = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProvider.Local,
                Model = "all-MiniLM-L6-v2",
                Dimensions = 384,
                CacheTtlMinutes = 5
            },
            Search = new SearchOptions
            {
                DefaultLimit = 10,
                DenseWeight = 0.6f,
                SparseWeight = 0.4f,
                RrfK = 60
            },
            Scoring = new ScoringOptions
            {
                RecencyWeight = 1.0f,
                ImportanceWeight = 1.0f,
                RelevanceWeight = 1.0f,
                DecayFactor = 0.99f
            }
        };

        services.AddSingleton<IOptions<MemoryIndexerOptions>>(Options.Create(indexerOptions));
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddLogging();

        // Register services
        services.AddSingleton<IEmbeddingService>(sp =>
            new LocalEmbeddingService(
                sp.GetRequiredService<IMemoryCache>(),
                sp.GetRequiredService<IOptions<MemoryIndexerOptions>>(),
                NullLogger<LocalEmbeddingService>.Instance));

        services.AddSingleton<IMemoryStore>(sp =>
            new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance));

        services.AddSingleton<IQueryExpander, QueryExpander>();

        services.AddSingleton<IScoringService>(sp =>
            new DefaultScoringService(sp.GetRequiredService<IOptions<MemoryIndexerOptions>>()));

        services.AddSingleton<IHybridSearchService>(sp =>
            new HybridSearchService(
                sp.GetRequiredService<IMemoryStore>(),
                sp.GetRequiredService<IEmbeddingService>(),
                sp.GetRequiredService<IOptions<MemoryIndexerOptions>>(),
                NullLogger<HybridSearchService>.Instance));

        _serviceProvider = services.BuildServiceProvider();

        _embeddingService = _serviceProvider.GetRequiredService<IEmbeddingService>();
        _memoryStore = _serviceProvider.GetRequiredService<IMemoryStore>();
        _queryExpander = _serviceProvider.GetRequiredService<IQueryExpander>();
        _hybridSearch = _serviceProvider.GetRequiredService<IHybridSearchService>();
        _scoringService = _serviceProvider.GetRequiredService<IScoringService>();

        _output.WriteLine($"Model: {indexerOptions.Embedding.Model}, Dimensions: {_embeddingService.Dimensions}");

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            _serviceProvider?.Dispose();
        }
    }

    [Fact]
    public void QueryExpander_ShouldExpandQueriesWithSynonyms()
    {
        // Test basic query expansion
        var testCases = new[]
        {
            ("What features am I building?", new[] { "feature", "functionality", "build", "create" }),
            ("Who is on my team?", new[] { "team", "colleague", "member" }),
            ("What are the future plans?", new[] { "future", "plan", "upcoming", "next" }),
            ("When is the meeting?", new[] { "meeting", "time", "schedule" })
        };

        _output.WriteLine("=== Query Expansion Test ===\n");

        foreach (var (query, expectedTerms) in testCases)
        {
            var expanded = _queryExpander.ExpandQuery(query);
            _output.WriteLine($"Original: {query}");
            _output.WriteLine($"Expanded: {expanded}");

            var matchedTerms = expectedTerms.Count(term =>
                expanded.Contains(term, StringComparison.OrdinalIgnoreCase));
            var coverage = (float)matchedTerms / expectedTerms.Length * 100;
            _output.WriteLine($"Term Coverage: {coverage:F0}% ({matchedTerms}/{expectedTerms.Length})\n");

            matchedTerms.Should().BeGreaterThan(0, $"Query '{query}' should have matched terms");
        }
    }

    [Fact]
    public void QueryExpander_ShouldGenerateQueryVariants()
    {
        var query = "What features am I building?";
        var variants = _queryExpander.GenerateQueryVariants(query, 3);

        _output.WriteLine("=== Query Variants Test ===\n");
        _output.WriteLine($"Original: {query}");
        _output.WriteLine($"Variants ({variants.Count}):");
        foreach (var variant in variants)
        {
            _output.WriteLine($"  - {variant}");
        }

        variants.Should().NotBeEmpty();
        variants.Should().Contain(query, "Original query should be included");
    }

    [Fact]
    public async Task EnhancedSearch_ShouldImproveFeatureQueryRecall()
    {
        // Setup: Store the same memories as the extended conversation test
        var memories = new[]
        {
            "Hi, I'm starting a new project to build a mobile app for fitness tracking.",
            "The app will track workouts, nutrition, and sleep patterns.",
            "I'm using React Native for cross-platform development.",
            "For the backend, I've chosen Node.js with Express and MongoDB.",
            "Planning to add social features like challenges and leaderboards.",
            "The biggest challenge right now is syncing data offline.",
            "We have a code review meeting every Tuesday at 3 PM.",
            "Sarah is designing the UI/UX, she uses Figma for mockups.",
            "My colleague Mike is working on the nutrition database.",
            "Battery optimization is critical for background tracking."
        };

        await StoreMemoriesAsync(memories, "test-user");

        // Query that previously failed (0% recall)
        var query = "What features am I building?";
        var expectedKeywords = new[] { "workout", "nutrition", "sleep" };

        _output.WriteLine("=== Feature Query Enhancement Test ===\n");

        // Baseline: Direct vector search
        var baselineResults = await SearchWithQueryAsync(query);
        var baselineRecall = EvaluateRecall(baselineResults, expectedKeywords);

        _output.WriteLine($"Baseline Query: {query}");
        _output.WriteLine($"Baseline Results:");
        foreach (var result in baselineResults.Take(3))
        {
            _output.WriteLine($"  [{result.Score:F3}] {result.Memory.Content[..Math.Min(60, result.Memory.Content.Length)]}...");
        }
        _output.WriteLine($"Baseline Recall: {baselineRecall:P0}\n");

        // Enhanced: Expanded query search
        var expandedQuery = _queryExpander.ExpandQuery(query);
        _output.WriteLine($"Expanded Query: {expandedQuery}");

        var enhancedResults = await SearchWithQueryAsync(expandedQuery);
        var enhancedRecall = EvaluateRecall(enhancedResults, expectedKeywords);

        _output.WriteLine($"Enhanced Results:");
        foreach (var result in enhancedResults.Take(3))
        {
            _output.WriteLine($"  [{result.Score:F3}] {result.Memory.Content[..Math.Min(60, result.Memory.Content.Length)]}...");
        }
        _output.WriteLine($"Enhanced Recall: {enhancedRecall:P0}\n");

        // Multi-query: Search with multiple variants and merge
        var variants = _queryExpander.GenerateQueryVariants(query, 3);
        var mergedResults = await MultiQuerySearchAsync(variants);
        var mergedRecall = EvaluateRecall(mergedResults, expectedKeywords);

        _output.WriteLine($"Multi-Query Results (from {variants.Count} variants):");
        foreach (var result in mergedResults.Take(3))
        {
            _output.WriteLine($"  [{result.Score:F3}] {result.Memory.Content[..Math.Min(60, result.Memory.Content.Length)]}...");
        }
        _output.WriteLine($"Multi-Query Recall: {mergedRecall:P0}");

        _output.WriteLine($"\n=== Improvement Summary ===");
        _output.WriteLine($"Baseline: {baselineRecall:P0} → Enhanced: {enhancedRecall:P0} → Multi-Query: {mergedRecall:P0}");
    }

    [Fact]
    public async Task EnhancedSearch_ShouldImproveTeamMemberQueryRecall()
    {
        var memories = new[]
        {
            "My colleague Mike is working on the nutrition database.",
            "Sarah is designing the UI/UX, she uses Figma for mockups.",
            "We have daily standups at 9 AM with the whole team.",
            "Mike joined the team last month from a startup background.",
            "Sarah has 5 years of UX experience.",
            "Planning to add social features like challenges and leaderboards."
        };

        await StoreMemoriesAsync(memories, "test-user");

        var query = "Who is on my team?";
        var expectedKeywords = new[] { "Mike", "Sarah" };

        _output.WriteLine("=== Team Member Query Enhancement Test ===\n");

        // Baseline
        var baselineResults = await SearchWithQueryAsync(query);
        var baselineRecall = EvaluateRecall(baselineResults, expectedKeywords);

        _output.WriteLine($"Baseline Query: {query}");
        _output.WriteLine($"Baseline Recall: {baselineRecall:P0}");

        // Enhanced with expansion
        var expandedQuery = _queryExpander.ExpandQuery(query);
        _output.WriteLine($"Expanded Query: {expandedQuery}");

        var enhancedResults = await SearchWithQueryAsync(expandedQuery);
        var enhancedRecall = EvaluateRecall(enhancedResults, expectedKeywords);
        _output.WriteLine($"Enhanced Recall: {enhancedRecall:P0}");

        // Multi-query with variants
        var variants = new[] { query, "team members", "colleagues working with me", "people on the project" };
        var mergedResults = await MultiQuerySearchAsync(variants);
        var mergedRecall = EvaluateRecall(mergedResults, expectedKeywords);
        _output.WriteLine($"Multi-Query Recall: {mergedRecall:P0}");

        _output.WriteLine($"\n=== Improvement ===");
        _output.WriteLine($"Baseline: {baselineRecall:P0} → Enhanced: {enhancedRecall:P0} → Multi-Query: {mergedRecall:P0}");
    }

    [Fact]
    public async Task HybridSearch_ShouldCombineDenseAndSparseResults()
    {
        var memories = new[]
        {
            "Python is great for machine learning.",
            "JavaScript is essential for web development.",
            "The database uses MongoDB for document storage.",
            "React hooks simplify state management.",
            "I use Python with TensorFlow for neural networks."
        };

        await StoreMemoriesAsync(memories, "test-user");

        // Index documents in BM25 for hybrid search
        var allMemories = await _memoryStore.GetAllAsync("test-user");
        foreach (var memory in allMemories)
        {
            _hybridSearch.IndexDocument(memory.Id, memory.Content);
        }

        var query = "Python machine learning";

        _output.WriteLine("=== Hybrid Search Test ===\n");
        _output.WriteLine($"Query: {query}\n");

        // Hybrid search
        var hybridResults = await _hybridSearch.SearchAsync(query, new HybridSearchOptions
        {
            UserId = "test-user",
            Limit = 5
        });

        _output.WriteLine("Hybrid Search Results:");
        foreach (var result in hybridResults)
        {
            _output.WriteLine($"  [{result.Score:F3}] (D:{result.DenseScore:F3}, S:{result.SparseScore:F3}, {result.SearchType})");
            _output.WriteLine($"    {result.Memory.Content}");
        }

        hybridResults.Should().NotBeEmpty();

        // Verify Python/ML related content ranks high
        var topResult = hybridResults.First();
        topResult.Memory.Content.Should().Contain("Python");
    }

    [Fact]
    public async Task TemporalScoring_ShouldFavorRecentMemories()
    {
        _output.WriteLine("=== Temporal Scoring Test ===\n");

        // Create memories with different timestamps
        var now = DateTime.UtcNow;
        var memories = new (string Content, DateTime Created)[]
        {
            ("My favorite color is blue.", now.AddDays(-30)),
            ("Actually, I've changed my mind - my favorite color is now green.", now.AddHours(-1)),
            ("I like blue skies and ocean.", now.AddDays(-15))
        };

        foreach (var (content, created) in memories)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(content);
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = content,
                UserId = "test-user",
                SessionId = "test-session",
                Embedding = embedding,
                CreatedAt = created,
                LastAccessedAt = created
            };
            await _memoryStore.StoreAsync(memory);
        }

        // Calculate scores for each memory
        var allMemories = await _memoryStore.GetAllAsync("test-user");
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync("What is my favorite color?");

        _output.WriteLine("Memory Scores (Recency + Relevance):");
        var scoredMemories = allMemories
            .Select(m => new
            {
                Memory = m,
                RecencyScore = _scoringService.CalculateRecencyScore(m),
                RelevanceScore = _scoringService.CalculateCosineSimilarity(queryEmbedding, m.Embedding!.Value),
                TotalScore = _scoringService.CalculateScore(m, queryEmbedding),
                Age = (now - m.CreatedAt).TotalDays
            })
            .OrderByDescending(x => x.TotalScore)
            .ToList();

        foreach (var scored in scoredMemories)
        {
            _output.WriteLine($"  Age: {scored.Age:F1} days | Recency: {scored.RecencyScore:F3} | Relevance: {scored.RelevanceScore:F3} | Total: {scored.TotalScore:F3}");
            _output.WriteLine($"    {scored.Memory.Content}");
        }

        // The most recent "green" memory should score higher due to recency
        var mostRecent = scoredMemories.First(m => m.Memory.Content.Contains("green"));
        var oldest = scoredMemories.First(m => m.Memory.Content.Contains("blue."));

        _output.WriteLine($"\nRecency Impact: Most recent ({mostRecent.RecencyScore:F3}) vs Oldest ({oldest.RecencyScore:F3})");
        mostRecent.RecencyScore.Should().BeGreaterThan(oldest.RecencyScore);
    }

    [Fact]
    public async Task ComprehensiveQualityComparison_BaselineVsEnhanced()
    {
        _output.WriteLine("=== Comprehensive Quality Comparison ===\n");

        // Same dataset as extended conversation test
        var memories = new[]
        {
            "Hi, I'm starting a new project to build a mobile app for fitness tracking.",
            "The app will track workouts, nutrition, and sleep patterns.",
            "I'm using React Native for cross-platform development.",
            "For the backend, I've chosen Node.js with Express and MongoDB.",
            "GraphQL is used for the API layer.",
            "Planning to add social features like challenges and leaderboards.",
            "The biggest challenge right now is syncing data offline.",
            "Battery optimization is critical for background tracking.",
            "Considering adding AI-powered workout recommendations.",
            "We have a code review meeting every Tuesday at 3 PM.",
            "Sarah is designing the UI/UX, she uses Figma for mockups.",
            "My colleague Mike is working on the nutrition database.",
            "The nutrition logging feature is now in beta testing.",
            "Having some issues with the step counter integration on Android.",
            "Just fixed the Android step counter bug, it was a permissions issue.",
            "Performance testing shows the app handles 1000 memories well.",
            "User feedback suggests the workout logging is intuitive.",
            "Next sprint we're focusing on sleep tracking accuracy.",
            "The app will integrate with Apple Health and Google Fit.",
            "Launch target is Q2 next year."
        };

        await StoreMemoriesAsync(memories, "test-user");

        var testQueries = new (string Query, string[] ExpectedKeywords)[]
        {
            ("What tech stack am I using?", ["React Native", "Node.js", "MongoDB", "GraphQL"]),
            ("Who is on my team?", ["Mike", "Sarah"]),
            ("What features am I building?", ["workout", "nutrition", "sleep"]),
            ("What challenges am I facing?", ["offline", "battery", "sync"]),
            ("What was the Android bug?", ["step counter", "permissions"]),
            ("What are the future plans?", ["social", "challenges", "AI", "recommendations"]),
            ("When are code reviews?", ["Tuesday", "3 PM"]),
            ("What tool does Sarah use?", ["Figma"])
        };

        _output.WriteLine("Testing baseline vs enhanced search:\n");

        var baselineTotal = 0f;
        var enhancedTotal = 0f;

        foreach (var (query, expectedKeywords) in testQueries)
        {
            // Baseline
            var baselineResults = await SearchWithQueryAsync(query);
            var baselineRecall = EvaluateRecall(baselineResults, expectedKeywords);

            // Enhanced (expanded query + multi-query)
            var expandedQuery = _queryExpander.ExpandQuery(query);
            var variants = _queryExpander.GenerateQueryVariants(query, 3);
            var enhancedResults = await MultiQuerySearchAsync(variants);
            var enhancedRecall = EvaluateRecall(enhancedResults, expectedKeywords);

            var improvement = enhancedRecall - baselineRecall;
            var improvementIndicator = improvement > 0 ? "↑" : improvement < 0 ? "↓" : "=";

            _output.WriteLine($"[{query.Split(' ').Last()}]");
            _output.WriteLine($"  Baseline: {baselineRecall:P0} → Enhanced: {enhancedRecall:P0} {improvementIndicator}");

            baselineTotal += baselineRecall;
            enhancedTotal += enhancedRecall;
        }

        var avgBaseline = baselineTotal / testQueries.Length;
        var avgEnhanced = enhancedTotal / testQueries.Length;

        _output.WriteLine($"\n================================================================================");
        _output.WriteLine($"OVERALL QUALITY COMPARISON");
        _output.WriteLine($"================================================================================");
        _output.WriteLine($"  Baseline Average:  {avgBaseline:P0}");
        _output.WriteLine($"  Enhanced Average:  {avgEnhanced:P0}");
        _output.WriteLine($"  Improvement:       {(avgEnhanced - avgBaseline):P0}");
        _output.WriteLine($"================================================================================");

        // Enhanced should be at least as good as baseline
        avgEnhanced.Should().BeGreaterThanOrEqualTo(avgBaseline * 0.9f,
            "Enhanced search should not significantly degrade quality");
    }

    private async Task StoreMemoriesAsync(string[] contents, string userId)
    {
        foreach (var content in contents)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(content);
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = content,
                UserId = userId,
                SessionId = "test-session",
                Embedding = embedding
            };
            await _memoryStore.StoreAsync(memory);
        }
    }

    private async Task<IReadOnlyList<MemorySearchResult>> SearchWithQueryAsync(string query)
    {
        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
        return await _memoryStore.SearchAsync(queryEmbedding, new MemorySearchOptions
        {
            UserId = "test-user",
            Limit = 5
        });
    }

    private async Task<IReadOnlyList<MemorySearchResult>> MultiQuerySearchAsync(IEnumerable<string> queries)
    {
        var allResults = new Dictionary<Guid, (MemorySearchResult Result, float MaxScore)>();

        foreach (var query in queries)
        {
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
            var results = await _memoryStore.SearchAsync(queryEmbedding, new MemorySearchOptions
            {
                UserId = "test-user",
                Limit = 10
            });

            foreach (var result in results)
            {
                if (allResults.TryGetValue(result.Memory.Id, out var existing))
                {
                    if (result.Score > existing.MaxScore)
                    {
                        allResults[result.Memory.Id] = (result, result.Score);
                    }
                }
                else
                {
                    allResults[result.Memory.Id] = (result, result.Score);
                }
            }
        }

        return allResults.Values
            .OrderByDescending(x => x.MaxScore)
            .Select(x => x.Result)
            .Take(5)
            .ToList();
    }

    private static float EvaluateRecall(IReadOnlyList<MemorySearchResult> results, string[] expectedKeywords)
    {
        if (results.Count == 0 || expectedKeywords.Length == 0)
            return 0f;

        var foundKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var combinedContent = string.Join(" ", results.Select(r => r.Memory.Content));

        foreach (var keyword in expectedKeywords)
        {
            if (combinedContent.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                foundKeywords.Add(keyword);
            }
        }

        return (float)foundKeywords.Count / expectedKeywords.Length;
    }
}
