using MemoryIndexer.Configuration;
using MemoryIndexer.Models;
using Xunit;

namespace MemoryIndexer.Tests.Configuration;

public class ConfigurationValidatorTests
{
    private readonly ConfigurationValidator _validator = new();

    [Fact]
    public void Validate_DefaultOptions_ShouldBeValid()
    {
        // Arrange
        var options = new MemoryIndexerOptions();

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_NegativeVectorDimensions_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Storage = new StorageOptions { VectorDimensions = -1 }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Storage.VectorDimensions");
    }

    [Fact]
    public void Validate_EmptyCollectionName_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Storage = new StorageOptions { CollectionName = "" }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Storage.CollectionName");
    }

    [Fact]
    public void Validate_NegativeEmbeddingDimensions_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions { Dimensions = 0 }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Embedding.Dimensions");
    }

    [Fact]
    public void Validate_EmptyEmbeddingModel_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions { Model = "" }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Embedding.Model");
    }

    [Fact]
    public void Validate_CustomProviderWithoutApiKey_ShouldReturnWarning()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions
            {
                Provider = EmbeddingProvider.Custom,
                ApiKey = null
            }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.True(result.IsValid); // Warning, not error
        Assert.Contains(result.Warnings, w => w.PropertyPath == "Embedding.ApiKey");
    }

    [Fact]
    public void Validate_InvalidDecayFactor_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Scoring = new ScoringOptions { DecayFactor = 1.5f }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Scoring.DecayFactor");
    }

    [Fact]
    public void Validate_NegativeScoringWeight_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Scoring = new ScoringOptions { RecencyWeight = -1.0f }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Scoring.RecencyWeight");
    }

    [Fact]
    public void Validate_MaxLimitLessThanDefault_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Search = new SearchOptions { DefaultLimit = 10, MaxLimit = 5 }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Search.MaxLimit");
    }

    [Fact]
    public void Validate_InvalidMmrLambda_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Search = new SearchOptions { MmrLambda = 1.5f }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Search.MmrLambda");
    }

    [Fact]
    public void Validate_InvalidTemperature_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Completion = new CompletionOptions { DefaultTemperature = 3.0f }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Completion.DefaultTemperature");
    }

    [Fact]
    public void Validate_InvalidPiiConfidence_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Security = new SecurityOptions { PiiMinConfidence = 1.5f }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Security.PiiMinConfidence");
    }

    [Fact]
    public void Validate_ZeroRateLimit_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Security = new SecurityOptions { StorePermitsPerMinute = 0 }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Security.StorePermitsPerMinute");
    }

    [Fact]
    public void Validate_NegativeSensoryBufferTokenThreshold_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            SensoryBuffer = new SensoryBufferOptions { TokenThreshold = -100 }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "SensoryBuffer.TokenThreshold");
    }

    [Fact]
    public void Validate_WorkingMemoryCapacityOutsideBaddeley_ShouldReturnWarning()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            WorkingMemory = new WorkingMemoryOptions { Capacity = 20 }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.True(result.IsValid); // Warning, not error
        Assert.Contains(result.Warnings, w => w.PropertyPath == "WorkingMemory.Capacity");
    }

    [Fact]
    public void Validate_TypeBalancerDistributionNotSumTo1_ShouldReturnWarning()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            TypeBalancing = new TypeBalancerOptions
            {
                TargetDistribution = new()
                {
                    [MemoryType.Episodic] = 0.5f,
                    [MemoryType.Semantic] = 0.5f,
                    [MemoryType.Procedural] = 0.5f // Sum = 1.5
                }
            }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.True(result.IsValid); // Warning, not error
        Assert.Contains(result.Warnings, w => w.PropertyPath == "TypeBalancing.TargetDistribution");
    }

    [Fact]
    public void ValidateAndThrow_InvalidOptions_ShouldThrowException()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions { Dimensions = -1 }
        };

        // Act & Assert
        var ex = Assert.Throws<ConfigurationValidationException>(() =>
            _validator.ValidateAndThrow(options));

        Assert.Contains("Embedding.Dimensions", ex.Message);
    }

    [Fact]
    public void ValidateAndThrow_ValidOptions_ShouldNotThrow()
    {
        // Arrange
        var options = new MemoryIndexerOptions();

        // Act & Assert (should not throw)
        _validator.ValidateAndThrow(options);
    }

    [Fact]
    public void ConfigurationValidationResult_MultipleErrors_ShouldCollectAll()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Embedding = new EmbeddingOptions { Dimensions = -1, BatchSize = 0 },
            Scoring = new ScoringOptions { DecayFactor = 2.0f }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void Validate_InvalidDeduplicationThreshold_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Deduplication = new DeduplicationOptions { DefaultSimilarityThreshold = 1.5f }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Deduplication.DefaultSimilarityThreshold");
    }

    [Fact]
    public void Validate_InvalidLatencyConfidence_ShouldReturnError()
    {
        // Arrange
        var options = new MemoryIndexerOptions
        {
            Latency = new LatencyOptions { EarlyTerminationConfidence = -0.5f }
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyPath == "Latency.EarlyTerminationConfidence");
    }

    [Fact]
    public void DefaultUserId_DefaultValue_IsDefault()
    {
        var options = new MemoryIndexerOptions();

        Assert.Equal("default", options.DefaultUserId);
    }

    [Fact]
    public void DefaultUserId_CanBeOverridden()
    {
        var options = new MemoryIndexerOptions
        {
            DefaultUserId = "custom-user"
        };

        Assert.Equal("custom-user", options.DefaultUserId);
    }
}
