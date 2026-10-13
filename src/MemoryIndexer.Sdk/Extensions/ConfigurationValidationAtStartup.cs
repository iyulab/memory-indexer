using MemoryIndexer.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// Runs the registered <see cref="IConfigurationValidator"/> when <see cref="MemoryIndexerOptions"/> are first built — at host
/// start, through the <c>ValidateOnStart()</c> that <c>AddMemoryIndexer()</c> registers. An error fails startup with
/// <see cref="OptionsValidationException"/> naming each setting; a warning is logged once.
/// </summary>
internal sealed partial class ConfigurationValidationAtStartup(
    IConfigurationValidator validator,
    ILogger<ConfigurationValidationAtStartup>? logger = null) : IValidateOptions<MemoryIndexerOptions>
{
    private readonly ILogger _logger = logger ?? NullLogger<ConfigurationValidationAtStartup>.Instance;

    public ValidateOptionsResult Validate(string? name, MemoryIndexerOptions options)
    {
        var result = validator.Validate(options);

        foreach (var warning in result.Warnings)
        {
            LogConfigurationWarning(_logger, warning.PropertyPath, warning.Message, warning.Suggestion ?? "");
        }

        return result.IsValid
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(result.Errors.Select(Describe));
    }

    private static string Describe(ConfigurationError error)
    {
        var detail = error.CurrentValue is null ? "" : $" (current: {error.CurrentValue}";
        if (detail.Length > 0)
        {
            detail += error.ExpectedConstraint is null ? ")" : $", expected: {error.ExpectedConstraint})";
        }

        return $"{MemoryIndexerOptions.SectionName}:{error.PropertyPath.Replace('.', ':')}: {error.Message}{detail}";
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Memory Indexer configuration {Setting}: {Message} {Suggestion}")]
    private static partial void LogConfigurationWarning(ILogger logger, string setting, string message, string suggestion);
}
