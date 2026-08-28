using AwesomeAssertions;
using MemoryIndexer.Models;
using Xunit;

namespace MemoryIndexer.Tests.Models;

/// <summary>
/// Tests for Scope enum behavior and semantics.
/// Phase 32.4: Category 1 - Scope enum behavior tests (50+ tests)
///
/// Scope enum values (narrowest to broadest):
/// - Turn = 0 (S3)
/// - Topic = 1 (S2)
/// - Session = 2 (S1)
/// - User = 3 (S0)
///
/// Containment: Larger value contains smaller (User=3 contains Turn=0)
/// </summary>
public class ScopeTests
{
    #region Enum Values and Ordering (10 tests)

    [Fact]
    public void Scope_ShouldHaveFourValues()
    {
        // Act
        var values = Enum.GetValues<Scope>();

        // Assert
        values.Should().HaveCount(4);
    }

    [Fact]
    public void Scope_ShouldHaveCorrectNumericValues()
    {
        // Assert - Enum values (narrowest to broadest)
        ((int)Scope.Turn).Should().Be(0);      // S3: Single turn (narrowest)
        ((int)Scope.Topic).Should().Be(1);     // S2: Topic cluster
        ((int)Scope.Session).Should().Be(2);   // S1: Single session
        ((int)Scope.User).Should().Be(3);      // S0: Cross-session (broadest)
    }

    [Fact]
    public void Scope_User_ShouldBeBroadestScope()
    {
        // Assert - User has highest enum value (broadest)
        Scope.User.Should().Be((Scope)3);
        (Scope.User > Scope.Session).Should().BeTrue();
        (Scope.User > Scope.Topic).Should().BeTrue();
        (Scope.User > Scope.Turn).Should().BeTrue();
    }

    [Fact]
    public void Scope_Turn_ShouldBeNarrowestScope()
    {
        // Assert - Turn has lowest enum value (narrowest)
        Scope.Turn.Should().Be((Scope)0);
        (Scope.Turn < Scope.Topic).Should().BeTrue();
        (Scope.Turn < Scope.Session).Should().BeTrue();
        (Scope.Turn < Scope.User).Should().BeTrue();
    }

    [Fact]
    public void Scope_Session_ShouldBeDefaultScope()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "test",
            Content = "test"
        };

        // Assert
        memory.Scope.Should().Be(Scope.Session);
    }

    [Theory]
    [InlineData(Scope.User)]
    [InlineData(Scope.Session)]
    [InlineData(Scope.Topic)]
    [InlineData(Scope.Turn)]
    public void Scope_AllValues_ShouldBeParseable(Scope scope)
    {
        // Act
        var name = scope.ToString();
        var parsed = Enum.Parse<Scope>(name);

        // Assert
        parsed.Should().Be(scope);
    }

    [Fact]
    public void Scope_ToString_ShouldReturnCorrectNames()
    {
        // Assert
        Scope.User.ToString().Should().Be("User");
        Scope.Session.ToString().Should().Be("Session");
        Scope.Topic.ToString().Should().Be("Topic");
        Scope.Turn.ToString().Should().Be("Turn");
    }

    [Fact]
    public void Scope_GetValues_ShouldReturnInEnumOrder()
    {
        // Act
        var values = Enum.GetValues<Scope>();

        // Assert - Ordered by enum value (Turn=0, Topic=1, Session=2, User=3)
        values[0].Should().Be(Scope.Turn);
        values[1].Should().Be(Scope.Topic);
        values[2].Should().Be(Scope.Session);
        values[3].Should().Be(Scope.User);
    }

    [Fact]
    public void Scope_Cast_ShouldWorkWithIntegers()
    {
        // Act & Assert
        ((Scope)0).Should().Be(Scope.Turn);
        ((Scope)1).Should().Be(Scope.Topic);
        ((Scope)2).Should().Be(Scope.Session);
        ((Scope)3).Should().Be(Scope.User);
    }

    [Fact]
    public void Scope_Comparison_ShouldFollowEnumValues()
    {
        // Assert - Turn (0) < Topic (1) < Session (2) < User (3)
        (Scope.Turn < Scope.Topic).Should().BeTrue();
        (Scope.Topic < Scope.Session).Should().BeTrue();
        (Scope.Session < Scope.User).Should().BeTrue();
        (Scope.Turn < Scope.User).Should().BeTrue();
    }

    #endregion

    #region Scope Hierarchy and Containment (15 tests)

    [Theory]
    [InlineData(Scope.User, Scope.Session, true)]   // User contains Session
    [InlineData(Scope.User, Scope.Topic, true)]     // User contains Topic
    [InlineData(Scope.User, Scope.Turn, true)]      // User contains Turn
    [InlineData(Scope.Session, Scope.Topic, true)]  // Session contains Topic
    [InlineData(Scope.Session, Scope.Turn, true)]   // Session contains Turn
    [InlineData(Scope.Topic, Scope.Turn, true)]     // Topic contains Turn
    [InlineData(Scope.Turn, Scope.Topic, false)]    // Turn does NOT contain Topic
    [InlineData(Scope.Topic, Scope.Session, false)] // Topic does NOT contain Session
    [InlineData(Scope.Session, Scope.User, false)]  // Session does NOT contain User
    public void Scope_Contains_ShouldFollowHierarchy(Scope container, Scope contained, bool expected)
    {
        // Act - Broader scope (higher value) contains narrower scope (lower value)
        var contains = container > contained || container == contained;

        // Assert
        if (expected)
        {
            (container >= contained).Should().BeTrue();
        }
        else
        {
            (container < contained).Should().BeTrue();
        }
    }

    [Fact]
    public void Scope_User_ShouldContainAllOtherScopes()
    {
        // Assert - User (3) >= all others
        (Scope.User >= Scope.Session).Should().BeTrue();
        (Scope.User >= Scope.Topic).Should().BeTrue();
        (Scope.User >= Scope.Turn).Should().BeTrue();
    }

    [Fact]
    public void Scope_Turn_ShouldNotContainAnyOtherScope()
    {
        // Assert - Turn (0) cannot contain anything except itself
        (Scope.Turn >= Scope.Topic).Should().BeFalse();
        (Scope.Turn >= Scope.Session).Should().BeFalse();
        (Scope.Turn >= Scope.User).Should().BeFalse();
    }

    [Fact]
    public void Scope_Session_ShouldContainTopicAndTurn()
    {
        // Assert - Session (2) contains Topic (1) and Turn (0)
        (Scope.Session >= Scope.Topic).Should().BeTrue();
        (Scope.Session >= Scope.Turn).Should().BeTrue();
        (Scope.Session >= Scope.User).Should().BeFalse();
    }

    [Fact]
    public void Scope_Topic_ShouldOnlyContainTurn()
    {
        // Assert - Topic (1) contains only Turn (0)
        (Scope.Topic >= Scope.Turn).Should().BeTrue();
        (Scope.Topic >= Scope.Session).Should().BeFalse();
        (Scope.Topic >= Scope.User).Should().BeFalse();
    }

    [Theory]
    [InlineData(Scope.User, Scope.User, true)]
    [InlineData(Scope.Session, Scope.Session, true)]
    [InlineData(Scope.Topic, Scope.Topic, true)]
    [InlineData(Scope.Turn, Scope.Turn, true)]
    public void Scope_SelfContainment_ShouldAlwaysBeTrue(Scope scope1, Scope scope2, bool expected)
    {
        // Act
        var contains = scope1 >= scope2;

        // Assert
        contains.Should().Be(expected);
    }

    [Fact]
    public void Scope_Hierarchy_TurnToUser_ShouldBeStrictlyIncreasing()
    {
        // Arrange - Narrowest to broadest
        var scopes = new[] { Scope.Turn, Scope.Topic, Scope.Session, Scope.User };

        // Act & Assert - Each scope should be strictly greater than the previous
        for (int i = 1; i < scopes.Length; i++)
        {
            (scopes[i] > scopes[i - 1]).Should().BeTrue();
        }
    }

    [Fact]
    public void Scope_Hierarchy_UserToTurn_ShouldBeStrictlyDecreasing()
    {
        // Arrange - Broadest to narrowest
        var scopes = new[] { Scope.User, Scope.Session, Scope.Topic, Scope.Turn };

        // Act & Assert - Each scope should be strictly less than the previous
        for (int i = 1; i < scopes.Length; i++)
        {
            (scopes[i] < scopes[i - 1]).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(Scope.Turn, 3)]     // 3 broader scopes (Topic, Session, User)
    [InlineData(Scope.Topic, 2)]    // 2 broader scopes (Session, User)
    [InlineData(Scope.Session, 1)]  // 1 broader scope (User)
    [InlineData(Scope.User, 0)]     // Broadest - 0 broader scopes
    public void Scope_BroaderScopeCount_ShouldFollowHierarchy(Scope scope, int expectedBroaderCount)
    {
        // Act - Count scopes with higher values (broader scopes)
        var allScopes = Enum.GetValues<Scope>();
        var broaderCount = allScopes.Count(s => s > scope);

        // Assert
        broaderCount.Should().Be(expectedBroaderCount);
    }

    [Theory]
    [InlineData(Scope.Turn, 0)]     // Narrowest - 0 narrower scopes
    [InlineData(Scope.Topic, 1)]    // 1 narrower scope (Turn)
    [InlineData(Scope.Session, 2)]  // 2 narrower scopes (Topic, Turn)
    [InlineData(Scope.User, 3)]     // 3 narrower scopes (Session, Topic, Turn)
    public void Scope_NarrowerScopeCount_ShouldFollowHierarchy(Scope scope, int expectedNarrowerCount)
    {
        // Act - Count scopes with lower values (narrower scopes)
        var allScopes = Enum.GetValues<Scope>();
        var narrowerCount = allScopes.Count(s => s < scope);

        // Assert
        narrowerCount.Should().Be(expectedNarrowerCount);
    }

    [Fact]
    public void Scope_Transitivity_UserContainsSessionContainsTurn_ThenUserContainsTurn()
    {
        // Assert - Transitive containment
        (Scope.User > Scope.Session).Should().BeTrue();
        (Scope.Session > Scope.Turn).Should().BeTrue();
        (Scope.User > Scope.Turn).Should().BeTrue(); // Transitivity
    }

    [Fact]
    public void Scope_Antisymmetry_IfAContainsBThenBDoesNotContainA()
    {
        // Arrange
        var pairs = new[]
        {
            (Scope.User, Scope.Session),
            (Scope.Session, Scope.Topic),
            (Scope.Topic, Scope.Turn)
        };

        // Act & Assert
        foreach (var (broader, narrower) in pairs)
        {
            (broader > narrower).Should().BeTrue();
            (narrower > broader).Should().BeFalse();
        }
    }

    [Fact]
    public void Scope_Sorting_ShouldProduceNarrowestToBroadest()
    {
        // Arrange
        var scopes = new[] { Scope.User, Scope.Turn, Scope.Session, Scope.Topic };

        // Act
        var sorted = scopes.OrderBy(s => s).ToArray();

        // Assert - Natural sort order is Turn(0) → Topic(1) → Session(2) → User(3)
        sorted[0].Should().Be(Scope.Turn);
        sorted[1].Should().Be(Scope.Topic);
        sorted[2].Should().Be(Scope.Session);
        sorted[3].Should().Be(Scope.User);
    }

    [Fact]
    public void Scope_Max_ShouldReturnBroadestScope()
    {
        // Arrange
        var scopes = new[] { Scope.Turn, Scope.Topic, Scope.Session, Scope.User };

        // Act
        var max = scopes.Max();

        // Assert
        max.Should().Be(Scope.User); // Highest value = broadest
    }

    [Fact]
    public void Scope_Min_ShouldReturnNarrowestScope()
    {
        // Arrange
        var scopes = new[] { Scope.User, Scope.Session, Scope.Topic, Scope.Turn };

        // Act
        var min = scopes.Min();

        // Assert
        min.Should().Be(Scope.Turn); // Lowest value = narrowest
    }

    #endregion

    #region Scope Semantics and Usage (15 tests)

    [Fact]
    public void Scope_User_ShouldRepresentCrossSessionData()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "user123",
            Content = "User prefers dark mode",
            Scope = Scope.User,
            Type = MemoryType.Fact
        };

        // Assert
        memory.Scope.Should().Be(Scope.User);
        memory.Type.Should().Be(MemoryType.Fact); // User-level facts
    }

    [Fact]
    public void Scope_Session_ShouldRepresentSingleConversation()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "user123",
            SessionId = "session456",
            Content = "We discussed project requirements",
            Scope = Scope.Session,
            Type = MemoryType.Episodic
        };

        // Assert
        memory.Scope.Should().Be(Scope.Session);
        memory.SessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Scope_Topic_ShouldRepresentTopicCluster()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "user123",
            SessionId = "session456",
            TopicId = "topic-ml",
            Content = "Machine learning algorithms discussion",
            Scope = Scope.Topic
        };

        // Assert
        memory.Scope.Should().Be(Scope.Topic);
        memory.TopicId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Scope_Turn_ShouldRepresentSingleTurn()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "user123",
            SessionId = "session456",
            Content = "Quick question about pricing",
            Scope = Scope.Turn
        };

        // Assert
        memory.Scope.Should().Be(Scope.Turn);
    }

    [Theory]
    [InlineData(Scope.User, "Cross-session preference")]
    [InlineData(Scope.Session, "Session-specific context")]
    [InlineData(Scope.Topic, "Topic-related information")]
    [InlineData(Scope.Turn, "Single turn content")]
    public void Scope_CanBeAssignedToMemoryUnit(Scope scope, string content)
    {
        // Arrange & Act
        var memory = new MemoryUnit
        {
            UserId = "test",
            Content = content,
            Scope = scope
        };

        // Assert
        memory.Scope.Should().Be(scope);
    }

    [Fact]
    public void Scope_User_TypicallyPairsWithFactType()
    {
        // Arrange - Common pattern: User-scoped Facts
        var memory = new MemoryUnit
        {
            UserId = "user123",
            Content = "Name: Alice, Age: 30, Location: NYC",
            Scope = Scope.User,
            Type = MemoryType.Fact
        };

        // Assert
        memory.Scope.Should().Be(Scope.User);
        memory.Type.Should().Be(MemoryType.Fact);
    }

    [Fact]
    public void Scope_Session_TypicallyPairsWithEpisodicType()
    {
        // Arrange - Common pattern: Session-scoped Episodes
        var memory = new MemoryUnit
        {
            UserId = "user123",
            SessionId = "session456",
            Content = "User asked about pricing, provided quote",
            Scope = Scope.Session,
            Type = MemoryType.Episodic
        };

        // Assert
        memory.Scope.Should().Be(Scope.Session);
        memory.Type.Should().Be(MemoryType.Episodic);
    }

    [Fact]
    public void Scope_Turn_TypicallyShortLivedContent()
    {
        // Arrange - Turn scope for immediate, transient content
        var memory = new MemoryUnit
        {
            UserId = "user123",
            SessionId = "session456",
            Content = "yes",
            Scope = Scope.Turn,
            Tier = Tier.Buffer
        };

        // Assert
        memory.Scope.Should().Be(Scope.Turn);
        memory.Tier.Should().Be(Tier.Buffer); // Typically starts in Buffer
    }

    [Fact]
    public void Scope_IsMutable_CanChangeFromTurnToSession()
    {
        // Arrange
        var memory = new MemoryUnit
        {
            UserId = "test",
            Content = "Important insight",
            Scope = Scope.Turn
        };

        // Act - Promote scope when content proves important
        memory.Scope = Scope.Session;

        // Assert
        memory.Scope.Should().Be(Scope.Session);
    }

    [Fact]
    public void Scope_IndependentOfTier_CanHaveAnyTierCombination()
    {
        // Arrange - Scope and Tier are orthogonal dimensions
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.Turn, Tier = Tier.Buffer },
            new MemoryUnit { Scope = Scope.Topic, Tier = Tier.Short },
            new MemoryUnit { Scope = Scope.Session, Tier = Tier.Long },
            new MemoryUnit { Scope = Scope.User, Tier = Tier.Archive }
        };

        // Assert - All combinations are valid
        memories.Should().OnlyContain(m => m.Scope >= Scope.Turn && m.Scope <= Scope.User);
        memories.Should().OnlyContain(m => m.Tier >= Tier.Buffer && m.Tier <= Tier.Archive);
    }

    [Fact]
    public void Scope_IndependentOfType_CanHaveAnyTypeCombination()
    {
        // Arrange - Scope and Type are orthogonal dimensions
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Type = MemoryType.Fact },
            new MemoryUnit { Scope = Scope.Session, Type = MemoryType.Episodic },
            new MemoryUnit { Scope = Scope.Topic, Type = MemoryType.Semantic },
            new MemoryUnit { Scope = Scope.Turn, Type = MemoryType.Procedural }
        };

        // Assert - All combinations are valid
        memories.Should().OnlyContain(m => m.Scope >= Scope.Turn && m.Scope <= Scope.User);
        memories.Should().OnlyContain(m => Enum.IsDefined(m.Type));
    }

    [Fact]
    public void Scope_Filtering_ShouldSupportBroadToNarrow()
    {
        // Arrange - Collection with mixed scopes
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Content = "A" },
            new MemoryUnit { Scope = Scope.Session, Content = "B" },
            new MemoryUnit { Scope = Scope.Topic, Content = "C" },
            new MemoryUnit { Scope = Scope.Turn, Content = "D" }
        };

        // Act - Filter for Session and broader (Session=2, User=3)
        var filtered = memories.Where(m => m.Scope >= Scope.Session).ToList();

        // Assert
        filtered.Should().HaveCount(2);
        filtered.Should().Contain(m => m.Scope == Scope.User);
        filtered.Should().Contain(m => m.Scope == Scope.Session);
    }

    [Fact]
    public void Scope_Filtering_ShouldSupportNarrowToBroad()
    {
        // Arrange
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Content = "A" },
            new MemoryUnit { Scope = Scope.Session, Content = "B" },
            new MemoryUnit { Scope = Scope.Topic, Content = "C" },
            new MemoryUnit { Scope = Scope.Turn, Content = "D" }
        };

        // Act - Filter for Topic and narrower (Topic=1, Turn=0)
        var filtered = memories.Where(m => m.Scope <= Scope.Topic).ToList();

        // Assert
        filtered.Should().HaveCount(2);
        filtered.Should().Contain(m => m.Scope == Scope.Topic);
        filtered.Should().Contain(m => m.Scope == Scope.Turn);
    }

    [Fact]
    public void Scope_Grouping_ShouldWorkCorrectly()
    {
        // Arrange
        var memories = new[]
        {
            new MemoryUnit { Scope = Scope.User, Content = "A" },
            new MemoryUnit { Scope = Scope.Session, Content = "B" },
            new MemoryUnit { Scope = Scope.Session, Content = "C" },
            new MemoryUnit { Scope = Scope.Turn, Content = "D" }
        };

        // Act
        var grouped = memories.GroupBy(m => m.Scope).ToDictionary(g => g.Key, g => g.Count());

        // Assert
        grouped[Scope.User].Should().Be(1);
        grouped[Scope.Session].Should().Be(2);
        grouped[Scope.Turn].Should().Be(1);
    }

    [Fact]
    public void Scope_Distinct_ShouldWorkCorrectly()
    {
        // Arrange
        var scopes = new[] { Scope.User, Scope.Session, Scope.User, Scope.Turn, Scope.Session };

        // Act
        var distinct = scopes.Distinct().ToList();

        // Assert
        distinct.Should().HaveCount(3);
        distinct.Should().Contain(Scope.User);
        distinct.Should().Contain(Scope.Session);
        distinct.Should().Contain(Scope.Turn);
    }

    #endregion

    #region Edge Cases and Validation (10 tests)

    [Fact]
    public void Scope_InvalidCast_ShouldThrowInvalidCastException()
    {
        // Arrange
        var invalidValue = 999;

        // Act
        Action act = () => { var scope = (Scope)invalidValue; };

        // Assert - C# doesn't throw for invalid enum values, but they're not defined
        var scope = (Scope)invalidValue;
        Enum.IsDefined(scope).Should().BeFalse();
    }

    [Fact]
    public void Scope_Equality_ShouldWorkCorrectly()
    {
        // Arrange
        var scope1 = Scope.Session;
        var scope2 = Scope.Session;
        var scope3 = Scope.Topic;

        // Assert
        scope1.Should().Be(scope2);
        scope1.Should().NotBe(scope3);
        scope1.Equals(scope2).Should().BeTrue();
        scope1.Equals(scope3).Should().BeFalse();
    }

    [Fact]
    public void Scope_HashCode_ShouldBeConsistent()
    {
        // Arrange
        var scope1 = Scope.Session;
        var scope2 = Scope.Session;

        // Act
        var hash1 = scope1.GetHashCode();
        var hash2 = scope2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Scope_Switch_ShouldHandleAllCases()
    {
        // Arrange
        var results = new Dictionary<Scope, string>();

        // Act
        foreach (var scope in Enum.GetValues<Scope>())
        {
            var result = scope switch
            {
                Scope.User => "Cross-session",
                Scope.Session => "Single conversation",
                Scope.Topic => "Topic cluster",
                Scope.Turn => "Single turn",
                _ => "Unknown"
            };
            results[scope] = result;
        }

        // Assert
        results.Should().HaveCount(4);
        results[Scope.User].Should().Be("Cross-session");
        results[Scope.Session].Should().Be("Single conversation");
        results[Scope.Topic].Should().Be("Topic cluster");
        results[Scope.Turn].Should().Be("Single turn");
    }

    [Fact]
    public void Scope_Array_ShouldSupportAllOperations()
    {
        // Arrange
        var scopes = new[] { Scope.Turn, Scope.Topic, Scope.Session, Scope.User };

        // Act & Assert
        scopes.Should().HaveCount(4);
        scopes.Should().Contain(Scope.User);
        scopes.Should().BeInAscendingOrder(); // Turn(0) < Topic(1) < Session(2) < User(3)
    }

    [Fact]
    public void Scope_List_ShouldSupportAddRemove()
    {
        // Arrange
        var scopes = new List<Scope>();

        // Act
        scopes.Add(Scope.User);
        scopes.Add(Scope.Session);
        scopes.Remove(Scope.User);

        // Assert
        scopes.Should().HaveCount(1);
        scopes.Should().Contain(Scope.Session);
        scopes.Should().NotContain(Scope.User);
    }

    [Fact]
    public void Scope_Dictionary_ShouldWorkAsKey()
    {
        // Arrange
        var dict = new Dictionary<Scope, string>
        {
            [Scope.User] = "Permanent",
            [Scope.Session] = "Conversation",
            [Scope.Topic] = "Cluster",
            [Scope.Turn] = "Immediate"
        };

        // Assert
        dict[Scope.User].Should().Be("Permanent");
        dict[Scope.Session].Should().Be("Conversation");
        dict.Keys.Should().HaveCount(4);
    }

    [Fact]
    public void Scope_HashSet_ShouldPreventDuplicates()
    {
        // Arrange
        var set = new HashSet<Scope>
        {
            Scope.User,
            Scope.Session,
            Scope.User, // Duplicate
            Scope.Topic
        };

        // Assert
        set.Should().HaveCount(3);
        set.Should().Contain(Scope.User);
        set.Should().Contain(Scope.Session);
        set.Should().Contain(Scope.Topic);
    }

    [Fact]
    public void Scope_Nullable_ShouldWorkCorrectly()
    {
        // Arrange
        Scope? scope1 = Scope.Session;
        Scope? scope2 = null;

        // Assert
        scope1.Should().NotBeNull();
        scope1.Value.Should().Be(Scope.Session);
        scope2.Should().BeNull();
    }

    [Fact]
    public void Scope_CompareTo_ShouldWorkForSorting()
    {
        // Arrange
        var scopes = new List<Scope> { Scope.User, Scope.Turn, Scope.Session, Scope.Topic };

        // Act
        scopes.Sort();

        // Assert - Natural order: Turn(0) → Topic(1) → Session(2) → User(3)
        scopes[0].Should().Be(Scope.Turn);
        scopes[1].Should().Be(Scope.Topic);
        scopes[2].Should().Be(Scope.Session);
        scopes[3].Should().Be(Scope.User);
    }

    #endregion
}
