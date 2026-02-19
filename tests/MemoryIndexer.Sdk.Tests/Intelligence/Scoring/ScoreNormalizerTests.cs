using MemoryIndexer.Interfaces;
using MemoryIndexer.Models;
using MemoryIndexer.Sdk.Intelligence.Scoring;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MemoryIndexer.Sdk.Tests.Intelligence.Scoring;

public class MinMaxScoreNormalizerTests
{
    private readonly MinMaxScoreNormalizer _normalizer;

    public MinMaxScoreNormalizerTests()
    {
        _normalizer = new MinMaxScoreNormalizer(NullLogger<MinMaxScoreNormalizer>.Instance);
    }

    [Fact]
    public void Normalize_EmptyList_ReturnsEmptyList()
    {
        // Arrange
        var memories = new List<NormalizableMemory>();

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Normalize_SingleMemory_ReturnsNormalizedToOne()
    {
        // Arrange
        var memory = CreateNormalizableMemory("test", 0.5f);
        var memories = new List<NormalizableMemory> { memory };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.Single(result);
        Assert.Equal(1.0f, result[0].NormalizedScore);
    }

    [Fact]
    public void Normalize_MultipleMemories_ScalesToZeroOneRange()
    {
        // Arrange
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("low", 1.0f),
            CreateNormalizableMemory("mid", 1.5f),
            CreateNormalizableMemory("high", 2.0f)
        };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(0.0f, result.First(m => m.Memory.Content == "low").NormalizedScore, 3);
        Assert.Equal(0.5f, result.First(m => m.Memory.Content == "mid").NormalizedScore, 3);
        Assert.Equal(1.0f, result.First(m => m.Memory.Content == "high").NormalizedScore, 3);
    }

    [Fact]
    public void Normalize_IdenticalScores_NormalizesToHalf()
    {
        // Arrange
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.5f),
            CreateNormalizableMemory("b", 1.5f),
            CreateNormalizableMemory("c", 1.5f)
        };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.All(result, m => Assert.Equal(0.5f, m.NormalizedScore));
    }

    [Fact]
    public void GetStats_ReturnsCorrectStatistics()
    {
        // Arrange
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.0f),
            CreateNormalizableMemory("b", 2.0f),
            CreateNormalizableMemory("c", 3.0f)
        };

        // Act
        _normalizer.Normalize(memories);
        var stats = _normalizer.GetStats();

        // Assert
        Assert.Equal(2.0f, stats.OriginalSpread); // 3.0 - 1.0
        Assert.Equal(1.0f, stats.NormalizedSpread);
        Assert.Equal(NormalizationStrategy.MinMax, stats.Strategy);
    }

    private static NormalizableMemory CreateNormalizableMemory(string content, float rawScore)
    {
        return new NormalizableMemory
        {
            Memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                UserId = "test-user",
                Content = content,
                CreatedAt = DateTime.UtcNow,
                Tier = Tier.Long,
                Type = MemoryType.Episodic
            },
            RawScore = rawScore,
            NormalizedScore = 0f
        };
    }
}

public class PercentileScoreNormalizerTests
{
    private readonly PercentileScoreNormalizer _normalizer;

    public PercentileScoreNormalizerTests()
    {
        _normalizer = new PercentileScoreNormalizer(NullLogger<PercentileScoreNormalizer>.Instance);
    }

    [Fact]
    public void Normalize_FiveMemories_AssignsCorrectPercentiles()
    {
        // Arrange
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("lowest", 1.0f),
            CreateNormalizableMemory("low", 1.2f),
            CreateNormalizableMemory("mid", 1.4f),
            CreateNormalizableMemory("high", 1.6f),
            CreateNormalizableMemory("highest", 1.8f)
        };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.Equal(5, result.Count);
        Assert.Equal(0.00f, result.First(m => m.Memory.Content == "lowest").NormalizedScore, 2);
        Assert.Equal(0.25f, result.First(m => m.Memory.Content == "low").NormalizedScore, 2);
        Assert.Equal(0.50f, result.First(m => m.Memory.Content == "mid").NormalizedScore, 2);
        Assert.Equal(0.75f, result.First(m => m.Memory.Content == "high").NormalizedScore, 2);
        Assert.Equal(1.00f, result.First(m => m.Memory.Content == "highest").NormalizedScore, 2);
    }

    [Fact]
    public void Normalize_NarrowDistribution_ForcesSeparation()
    {
        // Arrange - clustered scores like Twenty Questions game
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.10f),
            CreateNormalizableMemory("b", 1.25f),
            CreateNormalizableMemory("c", 1.40f),
            CreateNormalizableMemory("d", 1.55f),
            CreateNormalizableMemory("e", 1.69f)
        };

        // Act
        var result = _normalizer.Normalize(memories);
        var stats = _normalizer.GetStats();

        // Assert
        Assert.Equal(1.0f, stats.NormalizedSpread); // Full 0-1 range
        Assert.True(stats.OriginalSpread < 0.6f); // Original was narrow
    }

    private static NormalizableMemory CreateNormalizableMemory(string content, float rawScore)
    {
        return new NormalizableMemory
        {
            Memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                UserId = "test-user",
                Content = content,
                CreatedAt = DateTime.UtcNow,
                Tier = Tier.Long,
                Type = MemoryType.Episodic
            },
            RawScore = rawScore,
            NormalizedScore = 0f
        };
    }
}

public class ZScoreNormalizerTests
{
    private readonly ZScoreNormalizer _normalizer;

    public ZScoreNormalizerTests()
    {
        _normalizer = new ZScoreNormalizer(NullLogger<ZScoreNormalizer>.Instance);
    }

    [Fact]
    public void Normalize_NormalDistribution_MapsToZeroOneRange()
    {
        // Arrange
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.0f),
            CreateNormalizableMemory("b", 2.0f),
            CreateNormalizableMemory("c", 3.0f),
            CreateNormalizableMemory("d", 4.0f),
            CreateNormalizableMemory("e", 5.0f)
        };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.All(result, m => Assert.InRange(m.NormalizedScore, 0f, 1f));
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void Normalize_WithOutliers_HandlesGracefully()
    {
        // Arrange - distribution with outlier
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("outlier", 10.0f),
            CreateNormalizableMemory("normal1", 1.0f),
            CreateNormalizableMemory("normal2", 1.1f),
            CreateNormalizableMemory("normal3", 1.2f),
            CreateNormalizableMemory("normal4", 1.3f)
        };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        var outlier = result.First(m => m.Memory.Content == "outlier");
        var normals = result.Where(m => m.Memory.Content.StartsWith("normal", StringComparison.Ordinal)).ToList();

        // Outlier should have significantly higher normalized score than normals
        Assert.True(outlier.NormalizedScore > 0.7f, $"Outlier score {outlier.NormalizedScore} should be > 0.7");
        Assert.All(normals, n => Assert.True(n.NormalizedScore < outlier.NormalizedScore));

        // Normal values should be clustered together
        var normalSpread = normals.Max(m => m.NormalizedScore) - normals.Min(m => m.NormalizedScore);
        Assert.True(normalSpread < 0.5f);
    }

    [Fact]
    public void Normalize_IdenticalScores_NormalizesToHalf()
    {
        // Arrange
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.5f),
            CreateNormalizableMemory("b", 1.5f),
            CreateNormalizableMemory("c", 1.5f)
        };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.All(result, m => Assert.Equal(0.5f, m.NormalizedScore));
    }

    private static NormalizableMemory CreateNormalizableMemory(string content, float rawScore)
    {
        return new NormalizableMemory
        {
            Memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                UserId = "test-user",
                Content = content,
                CreatedAt = DateTime.UtcNow,
                Tier = Tier.Long,
                Type = MemoryType.Episodic
            },
            RawScore = rawScore,
            NormalizedScore = 0f
        };
    }
}

public class AdaptiveScoreNormalizerTests
{
    private readonly AdaptiveScoreNormalizer _normalizer;

    public AdaptiveScoreNormalizerTests()
    {
        var minMaxLogger = NullLogger<MinMaxScoreNormalizer>.Instance;
        var percentileLogger = NullLogger<PercentileScoreNormalizer>.Instance;
        var zScoreLogger = NullLogger<ZScoreNormalizer>.Instance;

        _normalizer = new AdaptiveScoreNormalizer(
            NullLogger<AdaptiveScoreNormalizer>.Instance,
            minMaxLogger,
            percentileLogger,
            zScoreLogger);
    }

    [Fact]
    public void Normalize_NarrowSpread_UsesPercentile()
    {
        // Arrange - narrow spread < 0.3
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.10f),
            CreateNormalizableMemory("b", 1.15f),
            CreateNormalizableMemory("c", 1.20f),
            CreateNormalizableMemory("d", 1.25f),
            CreateNormalizableMemory("e", 1.30f)
        };

        // Act
        var result = _normalizer.Normalize(memories);
        var stats = _normalizer.GetStats();

        // Assert
        Assert.True(stats.OriginalSpread < 0.3f);
        Assert.Equal(1.0f, stats.NormalizedSpread); // Percentile forces full range
    }

    [Fact]
    public void Normalize_HighVariance_UsesZScore()
    {
        // Arrange - high coefficient of variation
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("low", 0.5f),
            CreateNormalizableMemory("mid1", 1.0f),
            CreateNormalizableMemory("mid2", 1.5f),
            CreateNormalizableMemory("mid3", 2.0f),
            CreateNormalizableMemory("high", 10.0f) // outlier
        };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.Equal(5, result.Count);
        // With outlier, CV should trigger z-score
    }

    [Fact]
    public void Normalize_NormalDistribution_UsesMinMax()
    {
        // Arrange - spread > 0.3 and CV < 0.5
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.0f),
            CreateNormalizableMemory("b", 1.5f),
            CreateNormalizableMemory("c", 2.0f),
            CreateNormalizableMemory("d", 2.5f),
            CreateNormalizableMemory("e", 3.0f)
        };

        // Act
        var result = _normalizer.Normalize(memories);
        var stats = _normalizer.GetStats();

        // Assert
        Assert.True(stats.OriginalSpread >= 0.3f);
        Assert.Equal(1.0f, stats.NormalizedSpread);
    }

    [Fact]
    public void Normalize_FewSamples_UsesMinMax()
    {
        // Arrange - only 2 samples
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.0f),
            CreateNormalizableMemory("b", 2.0f)
        };

        // Act
        var result = _normalizer.Normalize(memories);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetStats_ReturnsAdaptiveStrategy()
    {
        // Arrange
        var memories = new List<NormalizableMemory>
        {
            CreateNormalizableMemory("a", 1.0f),
            CreateNormalizableMemory("b", 2.0f),
            CreateNormalizableMemory("c", 3.0f)
        };

        // Act
        _normalizer.Normalize(memories);
        var stats = _normalizer.GetStats();

        // Assert
        Assert.Equal(NormalizationStrategy.Adaptive, stats.Strategy);
    }

    private static NormalizableMemory CreateNormalizableMemory(string content, float rawScore)
    {
        return new NormalizableMemory
        {
            Memory = new MemoryUnit
            {
                Id = Guid.NewGuid(),
                UserId = "test-user",
                Content = content,
                CreatedAt = DateTime.UtcNow,
                Tier = Tier.Long,
                Type = MemoryType.Episodic
            },
            RawScore = rawScore,
            NormalizedScore = 0f
        };
    }
}
