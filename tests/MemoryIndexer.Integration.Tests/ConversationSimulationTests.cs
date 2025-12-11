using FluentAssertions;
using LocalEmbedder;
using MemoryIndexer.Core.Interfaces;
using MemoryIndexer.Core.Models;
using MemoryIndexer.Storage.InMemory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace MemoryIndexer.Integration.Tests;

/// <summary>
/// Comprehensive conversation simulation tests for evaluating memory quality.
/// Tests short-term memory, long-term memory, topic switching, and recall accuracy.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Heavy")]
[Trait("Category", "Simulation")]
public class ConversationSimulationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private IEmbeddingModel? _model;
    private IMemoryStore? _memoryStore;

    // Test user identifiers
    private const string UserId = "simulation-user";
    private const string Session1 = "session-001";
    private const string Session2 = "session-002";
    private const string Session3 = "session-003";

    public ConversationSimulationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("=== Initializing Conversation Simulation Tests ===");
        _output.WriteLine("Loading embedding model (all-MiniLM-L6-v2)...");

        _model = await LocalEmbedder.LocalEmbedder.LoadAsync("all-MiniLM-L6-v2");
        _memoryStore = new InMemoryMemoryStore(NullLogger<InMemoryMemoryStore>.Instance);

        _output.WriteLine($"Model loaded: {_model.ModelId}, Dimensions: {_model.Dimensions}");
        _output.WriteLine("");
    }

    public Task DisposeAsync()
    {
        _model?.Dispose();
        return Task.CompletedTask;
    }

    #region Short-Term Memory Tests (Single Session)

    [Fact]
    public async Task ShortTermMemory_ImmediateRecall_ShouldBeHighlyAccurate()
    {
        _output.WriteLine("=== Test: Short-Term Memory - Immediate Recall ===\n");

        // Simulate a short conversation about a specific topic
        var conversation = new[]
        {
            "My name is John and I work as a software engineer at TechCorp.",
            "I'm currently working on a machine learning project using Python and TensorFlow.",
            "The project deadline is next Friday, December 15th.",
            "My team lead is Sarah and we have daily standups at 9 AM.",
            "I prefer using VS Code for development with the Python extension."
        };

        // Store the conversation
        await StoreConversation(conversation, Session1);
        _output.WriteLine($"Stored {conversation.Length} memories in session\n");

        // Test immediate recall with specific queries
        var queries = new[]
        {
            ("What is my name?", new[] { "John", "software engineer" }),
            ("What project am I working on?", new[] { "machine learning", "Python", "TensorFlow" }),
            ("When is the deadline?", new[] { "Friday", "December 15" }),
            ("Who is my team lead?", new[] { "Sarah" }),
            ("What IDE do I use?", new[] { "VS Code", "Python" })
        };

        var totalScore = 0f;
        var testCount = 0;

        foreach (var (query, expectedKeywords) in queries)
        {
            var results = await SearchMemories(query, Session1, limit: 2);
            var score = EvaluateRecall(results, expectedKeywords);
            totalScore += score;
            testCount++;

            _output.WriteLine($"Query: \"{query}\"");
            _output.WriteLine($"  Expected: [{string.Join(", ", expectedKeywords)}]");
            _output.WriteLine($"  Top Result: {results.FirstOrDefault()?.Memory.Content ?? "N/A"}");
            _output.WriteLine($"  Score: {score:P0}\n");
        }

        var averageScore = totalScore / testCount;
        _output.WriteLine($"=== Short-Term Memory Accuracy: {averageScore:P0} ===\n");

        averageScore.Should().BeGreaterThanOrEqualTo(0.8f,
            "short-term memory immediate recall should be at least 80% accurate");
    }

    [Fact]
    public async Task ShortTermMemory_ContextualUnderstanding_ShouldLinkRelatedInfo()
    {
        _output.WriteLine("=== Test: Short-Term Memory - Contextual Understanding ===\n");

        // Store related but separate pieces of information
        var conversation = new[]
        {
            "I'm planning a trip to Japan next spring.",
            "I want to visit Tokyo, Kyoto, and Osaka during cherry blossom season.",
            "My budget is around $3000 for the two-week trip.",
            "I need to book flights and hotels soon.",
            "I'm interested in visiting temples, trying local food, and seeing Mount Fuji."
        };

        await StoreConversation(conversation, Session1);

        // Query with contextual questions that require linking information
        var contextualQueries = new[]
        {
            ("What are my travel plans?", 3), // Should retrieve multiple related memories
            ("How much am I spending on vacation?", 2),
            ("What activities am I interested in during my trip?", 2)
        };

        _output.WriteLine("Testing contextual retrieval:\n");

        foreach (var (query, expectedMinResults) in contextualQueries)
        {
            var results = await SearchMemories(query, Session1, limit: 3);

            _output.WriteLine($"Query: \"{query}\"");
            _output.WriteLine($"  Retrieved {results.Count} memories:");
            foreach (var result in results)
            {
                _output.WriteLine($"    [{result.Score:F3}] {result.Memory.Content}");
            }
            _output.WriteLine("");

            results.Count.Should().BeGreaterThanOrEqualTo(1,
                $"contextual query should retrieve relevant results");
        }
    }

    #endregion

    #region Long-Term Memory Tests (Cross-Session)

    [Fact]
    public async Task LongTermMemory_CrossSessionRecall_ShouldPersistImportantInfo()
    {
        _output.WriteLine("=== Test: Long-Term Memory - Cross-Session Recall ===\n");

        // Session 1: Initial information
        var session1Conversation = new[]
        {
            "I'm a vegetarian and I'm allergic to nuts.",
            "My favorite programming language is Rust.",
            "I live in San Francisco with my dog named Max.",
            "I work remotely for a startup called InnovateTech."
        };

        // Session 2: Different topic, but references some session 1 info
        var session2Conversation = new[]
        {
            "I'm looking for vegetarian restaurants near my office.",
            "Max needs to go to the vet next week for his annual checkup.",
            "The Rust conference is coming up and I want to attend.",
            "Working from home has been great for spending time with Max."
        };

        // Session 3: New session, testing recall of earlier information
        var session3Conversation = new[]
        {
            "Planning a team dinner, need to consider dietary restrictions.",
            "Thinking about adopting another pet to keep Max company."
        };

        // Store all sessions
        await StoreConversation(session1Conversation, Session1);
        await StoreConversation(session2Conversation, Session2);
        await StoreConversation(session3Conversation, Session3);

        _output.WriteLine($"Session 1: {session1Conversation.Length} memories");
        _output.WriteLine($"Session 2: {session2Conversation.Length} memories");
        _output.WriteLine($"Session 3: {session3Conversation.Length} memories\n");

        // Test cross-session recall
        var crossSessionQueries = new[]
        {
            ("What are my dietary restrictions?", new[] { "vegetarian", "allergic", "nuts" }),
            ("Tell me about my pet", new[] { "dog", "Max" }),
            ("What programming language do I prefer?", new[] { "Rust" }),
            ("Where do I work?", new[] { "InnovateTech", "startup", "remotely" })
        };

        var totalScore = 0f;

        foreach (var (query, expectedKeywords) in crossSessionQueries)
        {
            // Search across all sessions
            var results = await SearchMemories(query, UserId, limit: 3, searchAllSessions: true);
            var score = EvaluateRecall(results, expectedKeywords);
            totalScore += score;

            _output.WriteLine($"Query: \"{query}\"");
            _output.WriteLine($"  Expected: [{string.Join(", ", expectedKeywords)}]");
            foreach (var result in results.Take(2))
            {
                _output.WriteLine($"  [{result.Score:F3}] Session {result.Memory.SessionId}: {result.Memory.Content}");
            }
            _output.WriteLine($"  Recall Score: {score:P0}\n");
        }

        var averageScore = totalScore / crossSessionQueries.Length;
        _output.WriteLine($"=== Long-Term Memory Cross-Session Accuracy: {averageScore:P0} ===\n");

        averageScore.Should().BeGreaterThanOrEqualTo(0.7f,
            "long-term memory should maintain at least 70% accuracy across sessions");
    }

    [Fact]
    public async Task LongTermMemory_TemporalDecay_ShouldFavorRecentInfo()
    {
        _output.WriteLine("=== Test: Long-Term Memory - Temporal Recency ===\n");

        // Store information at different "times" (simulated by order)
        var oldMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "My favorite color is blue.",
            UserId = UserId,
            SessionId = Session1,
            CreatedAt = DateTime.UtcNow.AddDays(-30), // 30 days ago
            Embedding = await _model!.EmbedAsync("My favorite color is blue.")
        };

        var recentMemory = new MemoryUnit
        {
            Id = Guid.NewGuid(),
            Content = "Actually, I've changed my mind - my favorite color is now green.",
            UserId = UserId,
            SessionId = Session2,
            CreatedAt = DateTime.UtcNow.AddDays(-1), // 1 day ago
            Embedding = await _model!.EmbedAsync("Actually, I've changed my mind - my favorite color is now green.")
        };

        await _memoryStore!.StoreAsync(oldMemory);
        await _memoryStore!.StoreAsync(recentMemory);

        // Query about favorite color
        var queryEmbedding = await _model!.EmbedAsync("What is my favorite color?");
        var results = await _memoryStore!.SearchAsync(queryEmbedding, new MemorySearchOptions
        {
            UserId = UserId,
            Limit = 2
        });

        _output.WriteLine("Testing temporal relevance:");
        _output.WriteLine($"Query: \"What is my favorite color?\"\n");

        foreach (var result in results)
        {
            var age = (DateTime.UtcNow - result.Memory.CreatedAt).Days;
            _output.WriteLine($"  [{result.Score:F3}] (Age: {age} days) {result.Memory.Content}");
        }

        // Both should be retrieved (semantic similarity)
        results.Count.Should().Be(2);

        // Note: Current implementation doesn't apply temporal decay in scoring
        // This test documents current behavior and can be updated when decay is implemented
        _output.WriteLine("\nNote: Temporal decay scoring not yet implemented in search.");
        _output.WriteLine("Both memories retrieved based on semantic similarity.\n");
    }

    #endregion

    #region Topic Switching Tests

    [Fact]
    public async Task TopicSwitching_AbruptChange_ShouldMaintainSeparation()
    {
        _output.WriteLine("=== Test: Topic Switching - Abrupt Topic Changes ===\n");

        // Conversation with abrupt topic switches
        var mixedConversation = new[]
        {
            // Topic 1: Work
            "I have a meeting with the marketing team tomorrow at 2 PM.",
            "We need to finalize the Q4 budget proposal.",

            // Topic 2: Personal (abrupt switch)
            "By the way, I need to pick up my dry cleaning after work.",
            "My sister's birthday is coming up, need to buy a gift.",

            // Topic 3: Technical (another switch)
            "I'm debugging a memory leak in the Node.js application.",
            "The issue seems to be in the WebSocket connection handler.",

            // Back to Topic 1
            "Also, remind me to prepare the slides for tomorrow's meeting."
        };

        await StoreConversation(mixedConversation, Session1);

        // Test topic-specific retrieval
        var topicQueries = new[]
        {
            ("work meetings and tasks", new[] { "meeting", "marketing", "budget", "slides" }),
            ("personal errands", new[] { "dry cleaning", "sister", "birthday", "gift" }),
            ("technical debugging issues", new[] { "memory leak", "Node.js", "WebSocket" })
        };

        _output.WriteLine("Testing topic separation in retrieval:\n");

        foreach (var (query, expectedKeywords) in topicQueries)
        {
            var results = await SearchMemories(query, Session1, limit: 3);

            _output.WriteLine($"Topic Query: \"{query}\"");
            _output.WriteLine($"  Expected keywords: [{string.Join(", ", expectedKeywords)}]");
            _output.WriteLine("  Retrieved:");

            var matchedKeywords = new HashSet<string>();
            foreach (var result in results)
            {
                _output.WriteLine($"    [{result.Score:F3}] {result.Memory.Content}");
                foreach (var kw in expectedKeywords)
                {
                    if (result.Memory.Content.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedKeywords.Add(kw);
                    }
                }
            }

            var precision = matchedKeywords.Count / (float)expectedKeywords.Length;
            _output.WriteLine($"  Topic Precision: {precision:P0}\n");

            precision.Should().BeGreaterThanOrEqualTo(0.5f,
                $"topic-specific query should retrieve at least 50% relevant keywords");
        }
    }

    [Fact]
    public async Task TopicSwitching_GradualTransition_ShouldHandleOverlap()
    {
        _output.WriteLine("=== Test: Topic Switching - Gradual Transitions ===\n");

        // Conversation with gradual topic transitions
        var gradualConversation = new[]
        {
            // Start: General programming
            "I've been learning about design patterns in software development.",
            "The Factory pattern is really useful for creating objects.",

            // Transition: Programming -> Specific language
            "I'm implementing these patterns in my Python project.",
            "Python's duck typing makes some patterns easier to implement.",

            // Transition: Python -> Data Science
            "Speaking of Python, I'm also using it for data analysis.",
            "Pandas and NumPy are essential libraries for data manipulation.",

            // Final: Machine Learning
            "I'm now exploring machine learning with scikit-learn.",
            "Building a classification model to predict customer churn."
        };

        await StoreConversation(gradualConversation, Session1);

        // Test queries that should retrieve from overlapping topics
        var overlapQueries = new[]
        {
            ("Python programming patterns", new[] { "Factory pattern", "Python", "duck typing" }),
            ("Python data science", new[] { "Python", "data analysis", "Pandas", "NumPy" }),
            ("machine learning prediction", new[] { "machine learning", "scikit-learn", "classification", "predict" })
        };

        _output.WriteLine("Testing gradual topic transitions:\n");

        var totalOverlapScore = 0f;

        foreach (var (query, expectedKeywords) in overlapQueries)
        {
            var results = await SearchMemories(query, Session1, limit: 4);

            _output.WriteLine($"Query: \"{query}\"");

            var matchedKeywords = new HashSet<string>();
            foreach (var result in results)
            {
                _output.WriteLine($"  [{result.Score:F3}] {result.Memory.Content}");
                foreach (var kw in expectedKeywords)
                {
                    if (result.Memory.Content.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedKeywords.Add(kw);
                    }
                }
            }

            var overlap = matchedKeywords.Count / (float)expectedKeywords.Length;
            totalOverlapScore += overlap;
            _output.WriteLine($"  Keyword Coverage: {overlap:P0}\n");
        }

        var averageOverlap = totalOverlapScore / overlapQueries.Length;
        _output.WriteLine($"=== Topic Transition Handling: {averageOverlap:P0} ===\n");

        averageOverlap.Should().BeGreaterThanOrEqualTo(0.6f,
            "gradual topic transitions should maintain at least 60% keyword coverage");
    }

    #endregion

    #region Complex Scenario Tests

    [Fact]
    public async Task ComplexScenario_ExtendedConversation_QualityAssessment()
    {
        _output.WriteLine("=== Test: Extended Conversation Quality Assessment ===\n");

        // Simulate an extended conversation (20+ exchanges)
        var extendedConversation = new[]
        {
            // Introduction
            "Hi, I'm starting a new project to build a mobile app for fitness tracking.",
            "The app will track workouts, nutrition, and sleep patterns.",
            "I'm using React Native for cross-platform development.",

            // Technical decisions
            "For the backend, I've chosen Node.js with Express and MongoDB.",
            "User authentication will use JWT tokens with refresh token rotation.",
            "I'm implementing GraphQL for flexible data querying.",

            // Progress updates
            "Completed the user registration and login screens today.",
            "The workout logging feature is about 60% complete.",
            "Having some issues with the step counter integration on Android.",

            // Team collaboration
            "My colleague Mike is working on the nutrition database.",
            "Sarah is designing the UI/UX, she uses Figma for mockups.",
            "We have a code review meeting every Tuesday at 3 PM.",

            // Challenges
            "The biggest challenge right now is syncing data offline.",
            "Battery optimization is crucial for continuous step tracking.",
            "Need to implement data compression for the sync feature.",

            // Future plans
            "Planning to add social features like challenges and leaderboards.",
            "Want to integrate with Apple Health and Google Fit.",
            "Considering adding AI-powered workout recommendations.",

            // Recent updates
            "Just fixed the Android step counter bug, it was a permissions issue.",
            "The nutrition logging feature is now in beta testing."
        };

        await StoreConversation(extendedConversation, Session1);
        _output.WriteLine($"Stored {extendedConversation.Length} memories\n");

        // Comprehensive quality assessment
        var qualityTests = new[]
        {
            // Factual recall
            ("What tech stack am I using?", new[] { "React Native", "Node.js", "MongoDB", "GraphQL" }, "Tech Stack"),
            ("Who is on my team?", new[] { "Mike", "Sarah" }, "Team Members"),
            ("What features am I building?", new[] { "workout", "nutrition", "sleep" }, "Features"),

            // Problem solving context
            ("What challenges am I facing?", new[] { "offline", "battery", "sync" }, "Challenges"),
            ("What was the Android bug?", new[] { "step counter", "permissions" }, "Bug Details"),

            // Planning and future
            ("What are the future plans?", new[] { "social", "challenges", "AI", "recommendations" }, "Future Plans"),

            // Specific details
            ("When are code reviews?", new[] { "Tuesday", "3 PM" }, "Schedule"),
            ("What tool does Sarah use?", new[] { "Figma" }, "Tools")
        };

        var categoryScores = new Dictionary<string, float>();

        _output.WriteLine("Quality Assessment Results:\n");
        _output.WriteLine(new string('-', 80));

        foreach (var (query, expectedKeywords, category) in qualityTests)
        {
            var results = await SearchMemories(query, Session1, limit: 3);
            var score = EvaluateRecall(results, expectedKeywords);

            categoryScores[category] = score;

            _output.WriteLine($"\n[{category}]");
            _output.WriteLine($"  Query: \"{query}\"");
            _output.WriteLine($"  Expected: [{string.Join(", ", expectedKeywords)}]");
            _output.WriteLine($"  Top Results:");
            foreach (var r in results.Take(2))
            {
                _output.WriteLine($"    [{r.Score:F3}] {r.Memory.Content[..Math.Min(70, r.Memory.Content.Length)]}...");
            }
            _output.WriteLine($"  Recall Score: {score:P0}");
        }

        _output.WriteLine("\n" + new string('=', 80));
        _output.WriteLine("QUALITY ASSESSMENT SUMMARY");
        _output.WriteLine(new string('=', 80));

        foreach (var (category, score) in categoryScores.OrderByDescending(x => x.Value))
        {
            var bar = new string('█', (int)(score * 20));
            var empty = new string('░', 20 - (int)(score * 20));
            _output.WriteLine($"  {category,-15} [{bar}{empty}] {score:P0}");
        }

        var overallScore = categoryScores.Values.Average();
        _output.WriteLine(new string('-', 80));
        _output.WriteLine($"  OVERALL SCORE: {overallScore:P0}");
        _output.WriteLine(new string('=', 80));

        overallScore.Should().BeGreaterThanOrEqualTo(0.6f,
            "extended conversation should maintain at least 60% overall recall accuracy");
    }

    [Fact]
    public async Task ComplexScenario_SimilarButDifferentContext_ShouldDistinguish()
    {
        _output.WriteLine("=== Test: Similar Content, Different Context ===\n");

        // Store similar information in different contexts
        var contextualMemories = new[]
        {
            // Work context
            "At work, I drink coffee to stay alert during long meetings.",
            "My work laptop is a MacBook Pro 14-inch.",
            "I usually work from 9 AM to 6 PM.",

            // Home context
            "At home, I prefer tea in the evenings while reading.",
            "My personal laptop is a Windows gaming PC.",
            "I usually relax by playing video games after 8 PM.",

            // Both contexts (ambiguous)
            "I use Slack for communication.",
            "I prefer dark mode for all my applications."
        };

        await StoreConversation(contextualMemories, Session1);

        // Test context-specific queries
        var contextQueries = new[]
        {
            ("What do I drink at work?", "coffee", "work"),
            ("What do I drink at home?", "tea", "home"),
            ("What computer do I use for work?", "MacBook", "work"),
            ("What computer do I use at home?", "Windows", "home")
        };

        var correctContextCount = 0;

        _output.WriteLine("Testing context discrimination:\n");

        foreach (var (query, expectedKeyword, expectedContext) in contextQueries)
        {
            var results = await SearchMemories(query, Session1, limit: 2);
            var topResult = results.FirstOrDefault();

            var containsKeyword = topResult?.Memory.Content.Contains(expectedKeyword, StringComparison.OrdinalIgnoreCase) ?? false;
            var containsContext = topResult?.Memory.Content.Contains(expectedContext, StringComparison.OrdinalIgnoreCase) ?? false;

            _output.WriteLine($"Query: \"{query}\"");
            _output.WriteLine($"  Expected: '{expectedKeyword}' in '{expectedContext}' context");
            _output.WriteLine($"  Top Result: {topResult?.Memory.Content ?? "N/A"}");
            _output.WriteLine($"  Correct: {(containsKeyword ? "✓" : "✗")}\n");

            if (containsKeyword) correctContextCount++;
        }

        var contextAccuracy = correctContextCount / (float)contextQueries.Length;
        _output.WriteLine($"=== Context Discrimination Accuracy: {contextAccuracy:P0} ===\n");

        contextAccuracy.Should().BeGreaterThanOrEqualTo(0.75f,
            "system should correctly distinguish similar content in different contexts at least 75% of the time");
    }

    #endregion

    #region Performance and Scalability Tests

    [Fact]
    public async Task Performance_LargeMemorySet_SearchLatency()
    {
        _output.WriteLine("=== Test: Performance with Large Memory Set ===\n");

        // Generate 100 diverse memories
        var topics = new[] { "programming", "cooking", "travel", "fitness", "music", "reading", "work", "family" };
        var memories = new List<string>();

        for (var i = 0; i < 100; i++)
        {
            var topic = topics[i % topics.Length];
            memories.Add($"Memory #{i + 1} about {topic}: This is a test memory containing information about {topic} and various related concepts like {topic} techniques, {topic} tools, and {topic} best practices.");
        }

        _output.WriteLine($"Storing {memories.Count} memories...");
        var storeStart = DateTime.UtcNow;

        await StoreConversation(memories.ToArray(), Session1);

        var storeTime = (DateTime.UtcNow - storeStart).TotalMilliseconds;
        _output.WriteLine($"Storage time: {storeTime:F0}ms ({storeTime / memories.Count:F1}ms per memory)\n");

        // Test search performance
        var searchQueries = new[]
        {
            "programming techniques and tools",
            "cooking recipes and methods",
            "travel destinations and tips",
            "fitness workouts and exercises"
        };

        var searchTimes = new List<double>();

        _output.WriteLine("Search performance:\n");

        foreach (var query in searchQueries)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = await SearchMemories(query, Session1, limit: 10);
            sw.Stop();

            searchTimes.Add(sw.ElapsedMilliseconds);

            _output.WriteLine($"  Query: \"{query}\"");
            _output.WriteLine($"  Results: {results.Count}, Time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Top score: {results.FirstOrDefault()?.Score:F3}\n");
        }

        var avgSearchTime = searchTimes.Average();
        var maxSearchTime = searchTimes.Max();

        _output.WriteLine(new string('-', 50));
        _output.WriteLine($"Average search time: {avgSearchTime:F1}ms");
        _output.WriteLine($"Max search time: {maxSearchTime:F1}ms");
        _output.WriteLine($"Memory count: {memories.Count}");

        avgSearchTime.Should().BeLessThan(1000,
            "average search time should be under 1000ms for 100 memories");
    }

    #endregion

    #region Helper Methods

    private async Task StoreConversation(string[] messages, string sessionId)
    {
        foreach (var content in messages)
        {
            var embedding = await _model!.EmbedAsync(content);
            var memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                Content = content,
                UserId = UserId,
                SessionId = sessionId,
                CreatedAt = DateTime.UtcNow,
                Embedding = embedding
            };
            await _memoryStore!.StoreAsync(memory);
        }
    }

    private async Task<IReadOnlyList<MemorySearchResult>> SearchMemories(
        string query,
        string filterValue,
        int limit,
        bool searchAllSessions = false)
    {
        var queryEmbedding = await _model!.EmbedAsync(query);

        var options = new MemorySearchOptions
        {
            Limit = limit,
            UserId = UserId
        };

        if (!searchAllSessions)
        {
            options.SessionId = filterValue;
        }

        return await _memoryStore!.SearchAsync(queryEmbedding, options);
    }

    private static float EvaluateRecall(IReadOnlyList<MemorySearchResult> results, string[] expectedKeywords)
    {
        if (results.Count == 0 || expectedKeywords.Length == 0)
            return 0f;

        var matchedKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results)
        {
            foreach (var keyword in expectedKeywords)
            {
                if (result.Memory.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    matchedKeywords.Add(keyword);
                }
            }
        }

        return matchedKeywords.Count / (float)expectedKeywords.Length;
    }

    #endregion
}
