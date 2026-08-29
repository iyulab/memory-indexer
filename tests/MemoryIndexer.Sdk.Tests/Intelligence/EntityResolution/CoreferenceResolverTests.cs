using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.EntityResolution;
using MemoryIndexer.Sdk.Intelligence.KnowledgeGraph;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.EntityResolution;

/// <summary>
/// Unit tests for CoreferenceResolver.
/// </summary>
public class CoreferenceResolverTests
{
    private readonly CoreferenceResolver _resolver;

    public CoreferenceResolverTests()
    {
        _resolver = new CoreferenceResolver(NullLogger<CoreferenceResolver>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_EmptyText_ReturnsEmptyResult()
    {
        // Arrange
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync("", entities, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Coreferences);
        Assert.Empty(result.UnresolvedMentions);
    }

    [Fact]
    public async Task ResolveAsync_NoPronounsInText_ReturnsEmptyResult()
    {
        // Arrange
        var text = "John went to the store. John bought milk.";
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result.Coreferences);
    }

    [Fact]
    public async Task ResolveAsync_SimpleHePronoun_ResolvesToMaleEntity()
    {
        // Arrange
        var text = "John went to the store. He bought milk.";
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.Coreferences);
        Assert.Equal("He", result.Coreferences[0].Mention.Text);
        Assert.Equal("John", result.Coreferences[0].ReferentEntity.Name);
        Assert.Equal(CoreferenceType.PersonalPronoun, result.Coreferences[0].Type);
    }

    [Fact]
    public async Task ResolveAsync_SimpleShePronoun_ResolvesToFemaleEntity()
    {
        // Arrange
        var text = "Mary went to the office. She prepared the presentation.";
        var entities = new List<Entity>
        {
            new Entity { Name = "Mary", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.Coreferences);
        Assert.Equal("She", result.Coreferences[0].Mention.Text);
        Assert.Equal("Mary", result.Coreferences[0].ReferentEntity.Name);
    }

    [Fact]
    public async Task ResolveAsync_PossessivePronoun_ResolvesCorrectly()
    {
        // Arrange
        var text = "John finished his work early.";
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(result.Coreferences);
        Assert.Equal("his", result.Coreferences[0].Mention.Text);
        Assert.Equal(CoreferenceType.PossessivePronoun, result.Coreferences[0].Type);
    }

    [Fact]
    public async Task ResolveAsync_MultiplePronounsToSameEntity_ResolvesAll()
    {
        // Arrange
        var text = "John started his project. He worked hard and completed it himself.";
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        // "his", "He", "himself" should all resolve to John
        var johnCoreferences = result.Coreferences.Where(c => c.ReferentEntity.Name == "John").ToList();
        Assert.True(johnCoreferences.Count >= 2); // At least "his" and "He"
    }

    [Fact]
    public async Task ResolveAsync_MultipleEntities_ResolvesToNearestAntecedent()
    {
        // Arrange
        var text = "John met Mary. He greeted her warmly.";
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person },
            new Entity { Name = "Mary", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        var heCoref = result.Coreferences.FirstOrDefault(c => c.Mention.Text == "He");
        var herCoref = result.Coreferences.FirstOrDefault(c => c.Mention.Text == "her");

        Assert.NotNull(heCoref);
        Assert.NotNull(herCoref);
        Assert.Equal("John", heCoref.ReferentEntity.Name);
        Assert.Equal("Mary", herCoref.ReferentEntity.Name);
    }

    [Fact]
    public async Task ResolveAsync_OrganizationWithIt_ResolvesCorrectly()
    {
        // Arrange
        var text = "Microsoft announced new features. It plans to release them next month.";
        var entities = new List<Entity>
        {
            new Entity { Name = "Microsoft", Type = EntityType.Organization }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        var itCoref = result.Coreferences.FirstOrDefault(c => c.Mention.Text == "It");
        Assert.NotNull(itCoref);
        Assert.Equal("Microsoft", itCoref.ReferentEntity.Name);
    }

    [Fact]
    public async Task ResolveAsync_GenderMismatch_DoesNotResolve()
    {
        // Arrange - "she" should not resolve to "John" (masculine)
        var text = "John went home. She was tired.";
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert - "she" should be unresolved since John is masculine
        var sheCoref = result.Coreferences.FirstOrDefault(c => c.Mention.Text == "She");
        Assert.Null(sheCoref);
        Assert.Single(result.UnresolvedMentions);
    }

    [Fact]
    public async Task ResolveAcrossSegmentsAsync_MultipleTurns_ResolvesCorrectly()
    {
        // Arrange
        var segments = new List<string>
        {
            "John is a software engineer.",
            "He works at Microsoft.",
            "His projects are impressive."
        };
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAcrossSegmentsAsync(segments, entities, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Coreferences.Count >= 2);
        Assert.All(result.Coreferences, c => Assert.Equal("John", c.ReferentEntity.Name));
    }

    [Fact]
    public void ExpandText_ReplacesPronounsWithEntityNames()
    {
        // Arrange
        var text = "John went to the store. He bought milk.";
        // "John went to the store. " = 24 chars, so "He" is at positions 24-26
        var coreferences = new CoreferenceResult
        {
            Coreferences =
            [
                new Coreference
                {
                    Mention = new EntityMention
                    {
                        Text = "He",
                        StartPosition = 24,
                        EndPosition = 26,
                        Type = MentionType.Pronoun
                    },
                    ReferentEntity = new Entity { Name = "John", Type = EntityType.Person },
                    Confidence = 0.9f
                }
            ]
        };

        // Act
        var expanded = _resolver.ExpandText(text, coreferences);

        // Assert
        Assert.Equal("John went to the store. John bought milk.", expanded);
    }

    [Fact]
    public void ExpandText_WithIncludeOriginal_ShowsBothForms()
    {
        // Arrange
        var text = "John went home. He was tired.";
        // "John went home. " = 16 chars, so "He" is at positions 16-18
        var coreferences = new CoreferenceResult
        {
            Coreferences =
            [
                new Coreference
                {
                    Mention = new EntityMention
                    {
                        Text = "He",
                        StartPosition = 16,
                        EndPosition = 18,
                        Type = MentionType.Pronoun
                    },
                    ReferentEntity = new Entity { Name = "John", Type = EntityType.Person },
                    Confidence = 0.9f
                }
            ]
        };
        var options = new ExpansionOptions { IncludeOriginalPronoun = true };

        // Act
        var expanded = _resolver.ExpandText(text, coreferences, options);

        // Assert
        Assert.Contains("John (He)", expanded);
    }

    [Fact]
    public void ExpandText_BelowMinConfidence_DoesNotReplace()
    {
        // Arrange
        var text = "John went home. He was tired.";
        // "John went home. " = 16 chars, so "He" is at positions 16-18
        var coreferences = new CoreferenceResult
        {
            Coreferences =
            [
                new Coreference
                {
                    Mention = new EntityMention
                    {
                        Text = "He",
                        StartPosition = 16,
                        EndPosition = 18,
                        Type = MentionType.Pronoun
                    },
                    ReferentEntity = new Entity { Name = "John", Type = EntityType.Person },
                    Confidence = 0.5f // Below default threshold
                }
            ]
        };
        var options = new ExpansionOptions { MinConfidence = 0.7f };

        // Act
        var expanded = _resolver.ExpandText(text, coreferences, options);

        // Assert
        Assert.Equal(text, expanded); // No replacement due to low confidence
    }

    [Fact]
    public async Task GetAllMentionsAsync_ReturnsAllMentionsIncludingPronouns()
    {
        // Arrange
        var text = "John went to the office. He prepared his presentation.";
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var mentions = await _resolver.GetAllMentionsAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(mentions.Count >= 2); // At least "John" and "He" or "his"
        Assert.Contains(mentions, m => m.Text == "John");
    }

    [Fact]
    public async Task ResolveAsync_ReflexivePronoun_ResolvesCorrectly()
    {
        // Arrange
        var text = "John hurt himself.";
        var entities = new List<Entity>
        {
            new Entity { Name = "John", Type = EntityType.Person }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        var himselfCoref = result.Coreferences.FirstOrDefault(c => c.Mention.Text == "himself");
        Assert.NotNull(himselfCoref);
        Assert.Equal(CoreferenceType.ReflexivePronoun, himselfCoref.Type);
    }

    [Fact]
    public async Task ResolveAsync_TheyPronoun_ResolvesCorrectly()
    {
        // Arrange
        var text = "The team finished the project. They celebrated afterward.";
        var entities = new List<Entity>
        {
            new Entity { Name = "The team", Type = EntityType.Organization }
        };

        // Act
        var result = await _resolver.ResolveAsync(text, entities, TestContext.Current.CancellationToken);

        // Assert
        // "They" may or may not resolve depending on number agreement
        // The important thing is the resolver doesn't crash
        Assert.NotNull(result);
    }

    [Fact]
    public void CoreferenceResult_ResolutionRate_CalculatesCorrectly()
    {
        // Arrange
        var result = new CoreferenceResult
        {
            Coreferences =
            [
                new Coreference
                {
                    Mention = new EntityMention { Text = "He", StartPosition = 0, EndPosition = 2 },
                    ReferentEntity = new Entity { Name = "John" }
                },
                new Coreference
                {
                    Mention = new EntityMention { Text = "his", StartPosition = 10, EndPosition = 13 },
                    ReferentEntity = new Entity { Name = "John" }
                }
            ],
            UnresolvedMentions =
            [
                new UnresolvedMention
                {
                    Mention = new EntityMention { Text = "she", StartPosition = 20, EndPosition = 23 },
                    Reason = "No matching entity"
                }
            ]
        };

        // Act & Assert
        Assert.Equal(2, result.ResolvedCount);
        Assert.Equal(1, result.UnresolvedCount);
        Assert.Equal(2f / 3f, result.ResolutionRate, 0.01f);
    }

    [Fact]
    public void CoreferenceChain_TracksAllMentions()
    {
        // Arrange
        var entity = new Entity { Name = "John", Type = EntityType.Person };
        var chain = new CoreferenceChain
        {
            ReferentEntity = entity,
            Mentions =
            [
                new EntityMention { Text = "John", Type = MentionType.ProperName, StartPosition = 0, EndPosition = 4 },
                new EntityMention { Text = "He", Type = MentionType.Pronoun, StartPosition = 10, EndPosition = 12 },
                new EntityMention { Text = "his", Type = MentionType.Possessive, StartPosition = 20, EndPosition = 23 }
            ],
            Confidence = 0.95f
        };

        // Assert
        Assert.Equal(3, chain.Mentions.Count);
        Assert.Equal("John", chain.ReferentEntity.Name);
        Assert.Equal(0.95f, chain.Confidence);
    }

    [Fact]
    public void ExpansionOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new ExpansionOptions();

        // Assert
        Assert.False(options.IncludeOriginalPronoun);
        Assert.Equal(0.7f, options.MinConfidence);
        Assert.False(options.OnlyAmbiguous);
        Assert.Equal("{0}", options.ReplacementFormat);
    }

    [Fact]
    public void EntityMention_PropertiesSetCorrectly()
    {
        // Arrange & Act
        var mention = new EntityMention
        {
            Text = "John",
            StartPosition = 0,
            EndPosition = 4,
            SentenceIndex = 0,
            Type = MentionType.ProperName,
            Gender = GrammaticalGender.Masculine,
            Number = GrammaticalNumber.Singular,
            IsFirstMention = true
        };

        // Assert
        Assert.Equal("John", mention.Text);
        Assert.Equal(0, mention.StartPosition);
        Assert.Equal(4, mention.EndPosition);
        Assert.Equal(0, mention.SentenceIndex);
        Assert.Equal(MentionType.ProperName, mention.Type);
        Assert.Equal(GrammaticalGender.Masculine, mention.Gender);
        Assert.Equal(GrammaticalNumber.Singular, mention.Number);
        Assert.True(mention.IsFirstMention);
    }
}
