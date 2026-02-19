namespace MemoryIndexer.Configuration;

/// <summary>
/// Validates Memory Indexer configuration at startup.
/// Phase v0.5.0: Configuration Validation.
/// </summary>
public sealed class ConfigurationValidator : IConfigurationValidator
{
    /// <inheritdoc/>
    public ConfigurationValidationResult Validate(MemoryIndexerOptions options)
    {
        var result = new ConfigurationValidationResult();

        ValidateStorageOptions(options.Storage, result);
        ValidateEmbeddingOptions(options.Embedding, result);
        ValidateCompletionOptions(options.Completion, result);
        ValidateScoringOptions(options.Scoring, result);
        ValidateSearchOptions(options.Search, result);
        ValidateSecurityOptions(options.Security, result);
        ValidateIntelligenceOptions(options.Intelligence, result);
        ValidateSensoryBufferOptions(options.SensoryBuffer, result);
        ValidateDeduplicationOptions(options.Deduplication, result);
        ValidateMemoryGrowthOptions(options.MemoryGrowth, result);
        ValidateLatencyOptions(options.Latency, result);
        ValidateTypeBalancerOptions(options.TypeBalancing, result);
        ValidateWorkingMemoryOptions(options.WorkingMemory, result);

        return result;
    }

    /// <inheritdoc/>
    public void ValidateAndThrow(MemoryIndexerOptions options)
    {
        var result = Validate(options);
        if (!result.IsValid)
        {
            throw new ConfigurationValidationException(result);
        }
    }

    private static void ValidateStorageOptions(StorageOptions options, ConfigurationValidationResult result)
    {
        if (options.VectorDimensions <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Storage.VectorDimensions",
                Message = "Vector dimensions must be positive",
                CurrentValue = options.VectorDimensions,
                ExpectedConstraint = "> 0"
            });
        }

        if (string.IsNullOrWhiteSpace(options.CollectionName))
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Storage.CollectionName",
                Message = "Collection name cannot be empty",
                CurrentValue = options.CollectionName,
                ExpectedConstraint = "non-empty string"
            });
        }

        // SQLite options are validated if configured (storage provider is determined by DI)
        ValidateSqliteOptions(options.Sqlite, result);
    }

    private static void ValidateSqliteOptions(SqliteOptions options, ConfigurationValidationResult result)
    {
        if (options.CacheSizeKb < 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Storage.Sqlite.CacheSizeKb",
                Message = "Cache size must be non-negative",
                CurrentValue = options.CacheSizeKb,
                ExpectedConstraint = ">= 0"
            });
        }

        if (options.BusyTimeoutMs < 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Storage.Sqlite.BusyTimeoutMs",
                Message = "Busy timeout must be non-negative",
                CurrentValue = options.BusyTimeoutMs,
                ExpectedConstraint = ">= 0"
            });
        }

        if (options.HnswM < 2)
        {
            result.Warnings.Add(new ConfigurationWarning
            {
                PropertyPath = "Storage.Sqlite.HnswM",
                Message = "HNSW M parameter is very low, may reduce search quality",
                Suggestion = "Recommended: 16 for balanced performance"
            });
        }
    }

    private static void ValidateEmbeddingOptions(EmbeddingOptions options, ConfigurationValidationResult result)
    {
        if (options.Dimensions <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Embedding.Dimensions",
                Message = "Embedding dimensions must be positive",
                CurrentValue = options.Dimensions,
                ExpectedConstraint = "> 0"
            });
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Embedding.Model",
                Message = "Embedding model must be specified",
                CurrentValue = options.Model,
                ExpectedConstraint = "non-empty string"
            });
        }

        if (options.Provider != EmbeddingProvider.Mock &&
            string.IsNullOrWhiteSpace(options.Endpoint))
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Embedding.Endpoint",
                Message = "Endpoint is required for non-mock embedding providers",
                CurrentValue = options.Endpoint,
                ExpectedConstraint = "valid URL"
            });
        }

        if (options.BatchSize <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Embedding.BatchSize",
                Message = "Batch size must be positive",
                CurrentValue = options.BatchSize,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.TimeoutSeconds <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Embedding.TimeoutSeconds",
                Message = "Timeout must be positive",
                CurrentValue = options.TimeoutSeconds,
                ExpectedConstraint = "> 0"
            });
        }

        // API key validation for cloud providers
        if ((options.Provider == EmbeddingProvider.OpenAI ||
             options.Provider == EmbeddingProvider.AzureOpenAI) &&
            string.IsNullOrWhiteSpace(options.ApiKey))
        {
            result.Warnings.Add(new ConfigurationWarning
            {
                PropertyPath = "Embedding.ApiKey",
                Message = "API key not configured for cloud embedding provider",
                Suggestion = "Set API key for OpenAI/AzureOpenAI providers"
            });
        }
    }

    private static void ValidateCompletionOptions(CompletionOptions options, ConfigurationValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Completion.Model",
                Message = "Completion model must be specified",
                CurrentValue = options.Model,
                ExpectedConstraint = "non-empty string"
            });
        }

        if (options.TimeoutSeconds <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Completion.TimeoutSeconds",
                Message = "Timeout must be positive",
                CurrentValue = options.TimeoutSeconds,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.DefaultTemperature < 0 || options.DefaultTemperature > 2)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Completion.DefaultTemperature",
                Message = "Temperature must be between 0 and 2",
                CurrentValue = options.DefaultTemperature,
                ExpectedConstraint = "[0, 2]"
            });
        }

        if (options.DefaultMaxTokens <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Completion.DefaultMaxTokens",
                Message = "Max tokens must be positive",
                CurrentValue = options.DefaultMaxTokens,
                ExpectedConstraint = "> 0"
            });
        }
    }

    private static void ValidateScoringOptions(ScoringOptions options, ConfigurationValidationResult result)
    {
        if (options.RecencyWeight < 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Scoring.RecencyWeight",
                Message = "Recency weight must be non-negative",
                CurrentValue = options.RecencyWeight,
                ExpectedConstraint = ">= 0"
            });
        }

        if (options.ImportanceWeight < 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Scoring.ImportanceWeight",
                Message = "Importance weight must be non-negative",
                CurrentValue = options.ImportanceWeight,
                ExpectedConstraint = ">= 0"
            });
        }

        if (options.RelevanceWeight < 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Scoring.RelevanceWeight",
                Message = "Relevance weight must be non-negative",
                CurrentValue = options.RelevanceWeight,
                ExpectedConstraint = ">= 0"
            });
        }

        if (options.DecayFactor <= 0 || options.DecayFactor > 1)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Scoring.DecayFactor",
                Message = "Decay factor must be between 0 and 1",
                CurrentValue = options.DecayFactor,
                ExpectedConstraint = "(0, 1]"
            });
        }

        if (options.RecencyBiasMitigation < 0 || options.RecencyBiasMitigation > 1)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Scoring.RecencyBiasMitigation",
                Message = "Recency bias mitigation must be between 0 and 1",
                CurrentValue = options.RecencyBiasMitigation,
                ExpectedConstraint = "[0, 1]"
            });
        }
    }

    private static void ValidateSearchOptions(SearchOptions options, ConfigurationValidationResult result)
    {
        if (options.DefaultLimit <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Search.DefaultLimit",
                Message = "Default limit must be positive",
                CurrentValue = options.DefaultLimit,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.MaxLimit < options.DefaultLimit)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Search.MaxLimit",
                Message = "Max limit must be >= default limit",
                CurrentValue = options.MaxLimit,
                ExpectedConstraint = $">= {options.DefaultLimit}"
            });
        }

        if (options.DenseWeight < 0 || options.DenseWeight > 1)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Search.DenseWeight",
                Message = "Dense weight must be between 0 and 1",
                CurrentValue = options.DenseWeight,
                ExpectedConstraint = "[0, 1]"
            });
        }

        if (options.SparseWeight < 0 || options.SparseWeight > 1)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Search.SparseWeight",
                Message = "Sparse weight must be between 0 and 1",
                CurrentValue = options.SparseWeight,
                ExpectedConstraint = "[0, 1]"
            });
        }

        if (options.MmrLambda < 0 || options.MmrLambda > 1)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Search.MmrLambda",
                Message = "MMR lambda must be between 0 and 1",
                CurrentValue = options.MmrLambda,
                ExpectedConstraint = "[0, 1]"
            });
        }

        if (options.DuplicateThreshold < 0 || options.DuplicateThreshold > 1)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Search.DuplicateThreshold",
                Message = "Duplicate threshold must be between 0 and 1",
                CurrentValue = options.DuplicateThreshold,
                ExpectedConstraint = "[0, 1]"
            });
        }

        if (options.RerankCandidateMultiplier < 1)
        {
            result.Warnings.Add(new ConfigurationWarning
            {
                PropertyPath = "Search.RerankCandidateMultiplier",
                Message = "Rerank candidate multiplier should be >= 1",
                Suggestion = "Recommended: 4 for good reranking quality"
            });
        }
    }

    private static void ValidateSecurityOptions(SecurityOptions options, ConfigurationValidationResult result)
    {
        if (options.PiiMinConfidence < 0 || options.PiiMinConfidence > 1)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Security.PiiMinConfidence",
                Message = "PII min confidence must be between 0 and 1",
                CurrentValue = options.PiiMinConfidence,
                ExpectedConstraint = "[0, 1]"
            });
        }

        if (options.StorePermitsPerMinute <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Security.StorePermitsPerMinute",
                Message = "Rate limit must be positive",
                CurrentValue = options.StorePermitsPerMinute,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.RecallPermitsPerMinute <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Security.RecallPermitsPerMinute",
                Message = "Rate limit must be positive",
                CurrentValue = options.RecallPermitsPerMinute,
                ExpectedConstraint = "> 0"
            });
        }
    }

    private static void ValidateIntelligenceOptions(IntelligenceOptions options, ConfigurationValidationResult result)
    {
        if (options.MaxGeneratorTokens <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Intelligence.MaxGeneratorTokens",
                Message = "Max generator tokens must be positive",
                CurrentValue = options.MaxGeneratorTokens,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.GeneratorTemperature < 0 || options.GeneratorTemperature > 2)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Intelligence.GeneratorTemperature",
                Message = "Generator temperature must be between 0 and 2",
                CurrentValue = options.GeneratorTemperature,
                ExpectedConstraint = "[0, 2]"
            });
        }
    }

    private static void ValidateSensoryBufferOptions(SensoryBufferOptions options, ConfigurationValidationResult result)
    {
        if (options.IdleTimeout <= TimeSpan.Zero)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "SensoryBuffer.IdleTimeout",
                Message = "Idle timeout must be positive",
                CurrentValue = options.IdleTimeout,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.TokenThreshold <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "SensoryBuffer.TokenThreshold",
                Message = "Token threshold must be positive",
                CurrentValue = options.TokenThreshold,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.TurnThreshold <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "SensoryBuffer.TurnThreshold",
                Message = "Turn threshold must be positive",
                CurrentValue = options.TurnThreshold,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.MaxBufferSize <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "SensoryBuffer.MaxBufferSize",
                Message = "Max buffer size must be positive",
                CurrentValue = options.MaxBufferSize,
                ExpectedConstraint = "> 0"
            });
        }
    }

    private static void ValidateDeduplicationOptions(DeduplicationOptions options, ConfigurationValidationResult result)
    {
        ValidateThreshold(options.DefaultSimilarityThreshold, "Deduplication.DefaultSimilarityThreshold", result);
        ValidateThreshold(options.ExactDuplicateThreshold, "Deduplication.ExactDuplicateThreshold", result);
        ValidateThreshold(options.HighSimilarityThreshold, "Deduplication.HighSimilarityThreshold", result);
        ValidateThreshold(options.MediumSimilarityThreshold, "Deduplication.MediumSimilarityThreshold", result);
        ValidateThreshold(options.LowSimilarityThreshold, "Deduplication.LowSimilarityThreshold", result);

        // Validate threshold ordering
        if (options.ExactDuplicateThreshold < options.HighSimilarityThreshold)
        {
            result.Warnings.Add(new ConfigurationWarning
            {
                PropertyPath = "Deduplication",
                Message = "Threshold ordering should be: Exact > High > Medium > Low",
                Suggestion = "Adjust thresholds to maintain proper ordering"
            });
        }
    }

    private static void ValidateMemoryGrowthOptions(MemoryGrowthOptions options, ConfigurationValidationResult result)
    {
        if (options.MaxGrowthRatePerRound <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "MemoryGrowth.MaxGrowthRatePerRound",
                Message = "Max growth rate must be positive",
                CurrentValue = options.MaxGrowthRatePerRound,
                ExpectedConstraint = "> 0"
            });
        }

        ValidateThreshold(options.MinImportanceForStorage, "MemoryGrowth.MinImportanceForStorage", result);

        if (options.LowPressureThresholdMultiplier <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "MemoryGrowth.LowPressureThresholdMultiplier",
                Message = "Multiplier must be positive",
                CurrentValue = options.LowPressureThresholdMultiplier,
                ExpectedConstraint = "> 0"
            });
        }
    }

    private static void ValidateLatencyOptions(LatencyOptions options, ConfigurationValidationResult result)
    {
        if (options.WorkingMemoryBudgetMs <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Latency.WorkingMemoryBudgetMs",
                Message = "Latency budget must be positive",
                CurrentValue = options.WorkingMemoryBudgetMs,
                ExpectedConstraint = "> 0"
            });
        }

        if (options.EmbeddingCacheSize < 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "Latency.EmbeddingCacheSize",
                Message = "Cache size must be non-negative",
                CurrentValue = options.EmbeddingCacheSize,
                ExpectedConstraint = ">= 0"
            });
        }

        ValidateThreshold(options.EarlyTerminationConfidence, "Latency.EarlyTerminationConfidence", result);
    }

    private static void ValidateTypeBalancerOptions(TypeBalancerOptions options, ConfigurationValidationResult result)
    {
        if (options.BoostSensitivity < 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "TypeBalancing.BoostSensitivity",
                Message = "Boost sensitivity must be non-negative",
                CurrentValue = options.BoostSensitivity,
                ExpectedConstraint = ">= 0"
            });
        }

        ValidateThreshold(options.MaxBoost, "TypeBalancing.MaxBoost", result);

        // Validate target distribution sums to approximately 1.0
        var sum = options.TargetDistribution.Values.Sum();
        if (sum < 0.95f || sum > 1.05f)
        {
            result.Warnings.Add(new ConfigurationWarning
            {
                PropertyPath = "TypeBalancing.TargetDistribution",
                Message = $"Target distribution sum is {sum:F2}, should be close to 1.0",
                Suggestion = "Adjust distribution values to sum to 1.0"
            });
        }
    }

    private static void ValidateWorkingMemoryOptions(WorkingMemoryOptions options, ConfigurationValidationResult result)
    {
        if (options.Capacity <= 0)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "WorkingMemory.Capacity",
                Message = "Capacity must be positive",
                CurrentValue = options.Capacity,
                ExpectedConstraint = "> 0"
            });
        }

        // Baddeley's 7±2 model validation
        if (options.Capacity < 5 || options.Capacity > 11)
        {
            result.Warnings.Add(new ConfigurationWarning
            {
                PropertyPath = "WorkingMemory.Capacity",
                Message = "Capacity outside Baddeley's 7±2 model range (5-11)",
                Suggestion = "Recommended: 7-9 for cognitive compliance"
            });
        }

        if (options.IdleTimeout <= TimeSpan.Zero)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = "WorkingMemory.IdleTimeout",
                Message = "Idle timeout must be positive",
                CurrentValue = options.IdleTimeout,
                ExpectedConstraint = "> 0"
            });
        }

        ValidateThreshold(options.TopicChangeSimilarityThreshold, "WorkingMemory.TopicChangeSimilarityThreshold", result);
    }

    private static void ValidateThreshold(float value, string propertyPath, ConfigurationValidationResult result)
    {
        if (value < 0 || value > 1)
        {
            result.Errors.Add(new ConfigurationError
            {
                PropertyPath = propertyPath,
                Message = "Threshold must be between 0 and 1",
                CurrentValue = value,
                ExpectedConstraint = "[0, 1]"
            });
        }
    }
}
