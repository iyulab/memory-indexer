namespace MemoryIndexer.Configuration;

/// <summary>
/// Interface for validating Memory Indexer configuration.
/// Phase v0.5.0: Configuration Validation.
/// </summary>
public interface IConfigurationValidator
{
    /// <summary>
    /// Validates the configuration and returns validation results.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <returns>Validation result with any errors or warnings.</returns>
    ConfigurationValidationResult Validate(MemoryIndexerOptions options);

    /// <summary>
    /// Validates and throws if configuration is invalid.
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <exception cref="ConfigurationValidationException">Thrown when configuration is invalid.</exception>
    void ValidateAndThrow(MemoryIndexerOptions options);
}

/// <summary>
/// Result of configuration validation.
/// </summary>
public sealed class ConfigurationValidationResult
{
    /// <summary>
    /// Whether the configuration is valid.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Critical errors that prevent operation.
    /// </summary>
    public IList<ConfigurationError> Errors { get; } = new List<ConfigurationError>();

    /// <summary>
    /// Non-critical warnings.
    /// </summary>
    public IList<ConfigurationWarning> Warnings { get; } = new List<ConfigurationWarning>();
}

/// <summary>
/// Configuration error details.
/// </summary>
public sealed class ConfigurationError
{
    /// <summary>
    /// Property path (e.g., "Embedding.Dimensions").
    /// </summary>
    public required string PropertyPath { get; init; }

    /// <summary>
    /// Error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Current value.
    /// </summary>
    public object? CurrentValue { get; init; }

    /// <summary>
    /// Expected constraint.
    /// </summary>
    public string? ExpectedConstraint { get; init; }
}

/// <summary>
/// Configuration warning details.
/// </summary>
public sealed class ConfigurationWarning
{
    /// <summary>
    /// Property path.
    /// </summary>
    public required string PropertyPath { get; init; }

    /// <summary>
    /// Warning message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Suggested value or action.
    /// </summary>
    public string? Suggestion { get; init; }
}

/// <summary>
/// Exception thrown when configuration validation fails.
/// </summary>
public sealed class ConfigurationValidationException : Exception
{
    /// <summary>
    /// Validation result containing all errors.
    /// </summary>
    public ConfigurationValidationResult ValidationResult { get; }

    public ConfigurationValidationException(ConfigurationValidationResult result)
        : base(FormatMessage(result))
    {
        ValidationResult = result;
    }

    private static string FormatMessage(ConfigurationValidationResult result)
    {
        var errors = string.Join("; ", result.Errors.Select(e => $"{e.PropertyPath}: {e.Message}"));
        return $"Memory Indexer configuration is invalid: {errors}";
    }
}
