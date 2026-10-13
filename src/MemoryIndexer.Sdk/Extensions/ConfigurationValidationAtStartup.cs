using MemoryIndexer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MemoryIndexer.Sdk.Extensions;

/// <summary>
/// Runs the registered <see cref="IConfigurationValidator"/> when <see cref="MemoryIndexerOptions"/> are first built — at host
/// start, through the <c>ValidateOnStart()</c> that <c>AddMemoryIndexer()</c> registers. An error fails startup with
/// <see cref="OptionsValidationException"/> naming each setting; a warning is logged once. A setting that moved is an error
/// naming where it lives now, so a configuration written for the old place is not silently ignored.
/// </summary>
internal sealed partial class ConfigurationValidationAtStartup(
    IConfigurationValidator validator,
    ILogger<ConfigurationValidationAtStartup>? logger = null,
    IConfiguration? configuration = null) : IValidateOptions<MemoryIndexerOptions>
{
    private readonly ILogger _logger = logger ?? NullLogger<ConfigurationValidationAtStartup>.Instance;

    // Settings 0.35.0 moved (one option per concept). TODO: drop an entry two minor releases after its move.
    private static readonly (string Old, string New)[] Moved =
    [
        ("MemoryIndexer:VCM:WorkingMemory:Capacity", "MemoryIndexer:WorkingMemory:Capacity"),
        ("MemoryIndexer:VCM:SemanticStore:MinConfirmationCount", "MemoryIndexer:ArchivePromotion:MinConfirmationCount"),
        ("MemoryIndexer:VCM:SemanticStore:MinConfidenceThreshold", "MemoryIndexer:ArchivePromotion:MinConfidenceThreshold"),
    ];

    public ValidateOptionsResult Validate(string? name, MemoryIndexerOptions options)
    {
        var result = validator.Validate(options);
        List<string> moved = configuration is null
            ? []
            : Moved.Where(m => configuration[m.Old] is not null)
                .Select(m => $"{m.Old} is no longer read: set {m.New} instead")
                .ToList();

        foreach (var warning in result.Warnings)
        {
            LogConfigurationWarning(_logger, warning.PropertyPath, warning.Message, warning.Suggestion ?? "");
        }

        return result.IsValid && moved.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail([.. result.Errors.Select(Describe), .. moved]);
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
