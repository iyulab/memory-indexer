namespace MemoryIndexer.Intelligence.Security;

/// <summary>
/// Service for detecting and preventing prompt injection attacks.
/// Analyzes input for instruction-like patterns that could manipulate LLM behavior.
/// </summary>
public interface IPromptInjectionDetector
{
    /// <summary>
    /// Analyzes text for potential prompt injection patterns.
    /// </summary>
    /// <param name="text">Text to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detection result with risk assessment.</returns>
    Task<InjectionDetectionResult> DetectAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sanitizes text by neutralizing detected injection patterns.
    /// </summary>
    /// <param name="text">Text to sanitize.</param>
    /// <param name="options">Sanitization options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sanitized text result.</returns>
    Task<SanitizationResult> SanitizeAsync(
        string text,
        SanitizationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if text is safe for processing (no high-risk injections detected).
    /// </summary>
    /// <param name="text">Text to check.</param>
    /// <param name="maxAllowedRisk">Maximum allowed risk level.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if text is considered safe.</returns>
    Task<bool> IsSafeAsync(
        string text,
        RiskLevel maxAllowedRisk = RiskLevel.Low,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of prompt injection detection.
/// </summary>
public sealed class InjectionDetectionResult
{
    /// <summary>
    /// Whether any injection patterns were detected.
    /// </summary>
    public bool IsDetected { get; set; }

    /// <summary>
    /// Overall risk level.
    /// </summary>
    public RiskLevel RiskLevel { get; set; }

    /// <summary>
    /// Risk score (0.0 to 1.0).
    /// </summary>
    public float RiskScore { get; set; }

    /// <summary>
    /// Detected injection patterns.
    /// </summary>
    public List<InjectionPattern> DetectedPatterns { get; set; } = [];

    /// <summary>
    /// Recommendations for handling.
    /// </summary>
    public List<string> Recommendations { get; set; } = [];
}

/// <summary>
/// Risk level classification.
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// No risk detected.
    /// </summary>
    None = 0,

    /// <summary>
    /// Low risk - may be false positive.
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium risk - suspicious patterns.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High risk - likely injection attempt.
    /// </summary>
    High = 3,

    /// <summary>
    /// Critical risk - clear malicious intent.
    /// </summary>
    Critical = 4
}

/// <summary>
/// A detected injection pattern.
/// </summary>
public sealed class InjectionPattern
{
    /// <summary>
    /// Type of injection detected.
    /// </summary>
    public InjectionType Type { get; set; }

    /// <summary>
    /// The matched text.
    /// </summary>
    public string MatchedText { get; set; } = string.Empty;

    /// <summary>
    /// Start position in text.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// End position in text.
    /// </summary>
    public int EndIndex { get; set; }

    /// <summary>
    /// Confidence score.
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Risk contribution.
    /// </summary>
    public float RiskContribution { get; set; }

    /// <summary>
    /// Description of the pattern.
    /// </summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Types of prompt injection attacks.
/// </summary>
public enum InjectionType
{
    /// <summary>
    /// Unknown pattern.
    /// </summary>
    Unknown,

    /// <summary>
    /// Attempts to override system instructions.
    /// </summary>
    InstructionOverride,

    /// <summary>
    /// Jailbreak attempts (ignore previous instructions).
    /// </summary>
    Jailbreak,

    /// <summary>
    /// Role-playing manipulation (pretend you are...).
    /// </summary>
    RoleManipulation,

    /// <summary>
    /// Data exfiltration attempts.
    /// </summary>
    DataExfiltration,

    /// <summary>
    /// Delimiter/escape sequence attacks.
    /// </summary>
    DelimiterAttack,

    /// <summary>
    /// Encoded/obfuscated payloads.
    /// </summary>
    EncodedPayload,

    /// <summary>
    /// System prompt extraction attempts.
    /// </summary>
    PromptLeakage,

    /// <summary>
    /// Recursive/nested instruction injection.
    /// </summary>
    RecursiveInjection,

    /// <summary>
    /// Context manipulation.
    /// </summary>
    ContextManipulation,

    /// <summary>
    /// Token smuggling attacks.
    /// </summary>
    TokenSmuggling
}

/// <summary>
/// Options for text sanitization.
/// </summary>
public sealed class SanitizationOptions
{
    /// <summary>
    /// Mode of sanitization.
    /// </summary>
    public SanitizationMode Mode { get; set; } = SanitizationMode.Neutralize;

    /// <summary>
    /// Minimum risk level to sanitize.
    /// </summary>
    public RiskLevel MinRiskToSanitize { get; set; } = RiskLevel.Medium;

    /// <summary>
    /// Whether to normalize unicode characters.
    /// </summary>
    public bool NormalizeUnicode { get; set; } = true;

    /// <summary>
    /// Whether to remove invisible characters.
    /// </summary>
    public bool RemoveInvisibleChars { get; set; } = true;

    /// <summary>
    /// Whether to escape potential delimiters.
    /// </summary>
    public bool EscapeDelimiters { get; set; } = true;

    /// <summary>
    /// Custom prefix to add for data separation.
    /// </summary>
    public string? DataPrefix { get; set; }

    /// <summary>
    /// Maximum allowed text length (0 = no limit).
    /// </summary>
    public int MaxLength { get; set; } = 0;
}

/// <summary>
/// Mode of sanitization.
/// </summary>
public enum SanitizationMode
{
    /// <summary>
    /// Neutralize injection patterns (make them ineffective).
    /// </summary>
    Neutralize,

    /// <summary>
    /// Remove detected patterns entirely.
    /// </summary>
    Remove,

    /// <summary>
    /// Block the entire input if injection detected.
    /// </summary>
    Block,

    /// <summary>
    /// Escape special characters and delimiters.
    /// </summary>
    Escape
}

/// <summary>
/// Result of sanitization.
/// </summary>
public sealed class SanitizationResult
{
    /// <summary>
    /// The sanitized text.
    /// </summary>
    public string SanitizedText { get; set; } = string.Empty;

    /// <summary>
    /// Original text.
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// Whether any changes were made.
    /// </summary>
    public bool WasModified { get; set; }

    /// <summary>
    /// Whether the input was blocked.
    /// </summary>
    public bool WasBlocked { get; set; }

    /// <summary>
    /// Detection result that triggered sanitization.
    /// </summary>
    public InjectionDetectionResult? DetectionResult { get; set; }

    /// <summary>
    /// List of modifications made.
    /// </summary>
    public List<SanitizationModification> Modifications { get; set; } = [];
}

/// <summary>
/// A modification made during sanitization.
/// </summary>
public sealed class SanitizationModification
{
    /// <summary>
    /// Type of modification.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Original content.
    /// </summary>
    public string Original { get; set; } = string.Empty;

    /// <summary>
    /// Replacement content.
    /// </summary>
    public string Replacement { get; set; } = string.Empty;

    /// <summary>
    /// Position in original text.
    /// </summary>
    public int Position { get; set; }
}
