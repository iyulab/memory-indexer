namespace MemoryIndexer.Intelligence.Security;

/// <summary>
/// Service for detecting and handling Personally Identifiable Information (PII).
/// Supports detection of various PII types including names, SSN, credit cards, etc.
/// </summary>
public interface IPiiDetector
{
    /// <summary>
    /// Detects PII entities in the given text.
    /// </summary>
    /// <param name="text">Text to analyze.</param>
    /// <param name="minConfidence">Minimum confidence threshold (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of detected PII entities.</returns>
    Task<IReadOnlyList<PiiEntity>> DetectAsync(
        string text,
        float minConfidence = 0.5f,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts PII from the given text, replacing with placeholders.
    /// </summary>
    /// <param name="text">Text to redact.</param>
    /// <param name="options">Redaction options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Text with PII redacted and list of redactions made.</returns>
    Task<PiiRedactionResult> RedactAsync(
        string text,
        PiiRedactionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if text contains any PII above the confidence threshold.
    /// </summary>
    /// <param name="text">Text to check.</param>
    /// <param name="minConfidence">Minimum confidence threshold.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if PII is detected.</returns>
    Task<bool> ContainsPiiAsync(
        string text,
        float minConfidence = 0.5f,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a detected PII entity.
/// </summary>
public sealed class PiiEntity
{
    /// <summary>
    /// Type of PII detected.
    /// </summary>
    public PiiType Type { get; set; }

    /// <summary>
    /// The detected text value.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Start position in original text.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// End position in original text.
    /// </summary>
    public int EndIndex { get; set; }

    /// <summary>
    /// Confidence score (0.0 to 1.0).
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>
    /// Recognition model that detected this entity.
    /// </summary>
    public string? RecognizerName { get; set; }
}

/// <summary>
/// Types of PII that can be detected.
/// </summary>
public enum PiiType
{
    /// <summary>
    /// Unknown PII type.
    /// </summary>
    Unknown,

    /// <summary>
    /// Person's name.
    /// </summary>
    PersonName,

    /// <summary>
    /// Social Security Number.
    /// </summary>
    Ssn,

    /// <summary>
    /// Credit card number.
    /// </summary>
    CreditCard,

    /// <summary>
    /// Phone number.
    /// </summary>
    PhoneNumber,

    /// <summary>
    /// Email address.
    /// </summary>
    Email,

    /// <summary>
    /// Physical address.
    /// </summary>
    Address,

    /// <summary>
    /// Date of birth.
    /// </summary>
    DateOfBirth,

    /// <summary>
    /// Bank account number.
    /// </summary>
    BankAccount,

    /// <summary>
    /// Driver's license number.
    /// </summary>
    DriversLicense,

    /// <summary>
    /// Passport number.
    /// </summary>
    Passport,

    /// <summary>
    /// IP address.
    /// </summary>
    IpAddress,

    /// <summary>
    /// Medical record number.
    /// </summary>
    MedicalRecordNumber,

    /// <summary>
    /// National identification number (non-US).
    /// </summary>
    NationalId,

    /// <summary>
    /// Financial identifier (IBAN, routing number, etc.).
    /// </summary>
    FinancialId,

    /// <summary>
    /// Location/GPS coordinates.
    /// </summary>
    Location,

    /// <summary>
    /// URL or web address.
    /// </summary>
    Url,

    /// <summary>
    /// Age or age group.
    /// </summary>
    Age,

    /// <summary>
    /// Generic date (may be sensitive in context).
    /// </summary>
    Date,

    /// <summary>
    /// Custom or organization-specific PII type.
    /// </summary>
    Custom
}

/// <summary>
/// Options for PII redaction.
/// </summary>
public sealed class PiiRedactionOptions
{
    /// <summary>
    /// Minimum confidence threshold for redaction.
    /// </summary>
    public float MinConfidence { get; set; } = 0.5f;

    /// <summary>
    /// PII types to redact. If empty, all types are redacted.
    /// </summary>
    public HashSet<PiiType> TypesToRedact { get; set; } = [];

    /// <summary>
    /// PII types to ignore (not redact).
    /// </summary>
    public HashSet<PiiType> TypesToIgnore { get; set; } = [];

    /// <summary>
    /// Redaction mode.
    /// </summary>
    public RedactionMode Mode { get; set; } = RedactionMode.Replace;

    /// <summary>
    /// Custom replacement text (used when Mode is Replace).
    /// </summary>
    public string? ReplacementText { get; set; }

    /// <summary>
    /// Whether to include PII type in replacement (e.g., "[EMAIL]").
    /// </summary>
    public bool IncludeTypeInReplacement { get; set; } = true;

    /// <summary>
    /// Character to use for masking (used when Mode is Mask).
    /// </summary>
    public char MaskCharacter { get; set; } = '*';

    /// <summary>
    /// Number of characters to show at start when partially masking.
    /// </summary>
    public int PartialMaskShowStart { get; set; } = 0;

    /// <summary>
    /// Number of characters to show at end when partially masking.
    /// </summary>
    public int PartialMaskShowEnd { get; set; } = 0;
}

/// <summary>
/// Mode of PII redaction.
/// </summary>
public enum RedactionMode
{
    /// <summary>
    /// Replace with placeholder (e.g., "[EMAIL]").
    /// </summary>
    Replace,

    /// <summary>
    /// Fully mask with character (e.g., "****").
    /// </summary>
    FullMask,

    /// <summary>
    /// Partially mask, showing some characters.
    /// </summary>
    PartialMask,

    /// <summary>
    /// Hash the value (for deduplication while hiding).
    /// </summary>
    Hash,

    /// <summary>
    /// Remove entirely.
    /// </summary>
    Remove
}

/// <summary>
/// Result of PII redaction.
/// </summary>
public sealed class PiiRedactionResult
{
    /// <summary>
    /// The redacted text.
    /// </summary>
    public string RedactedText { get; set; } = string.Empty;

    /// <summary>
    /// Original text.
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

    /// <summary>
    /// List of redactions that were made.
    /// </summary>
    public List<RedactionInfo> Redactions { get; set; } = [];

    /// <summary>
    /// Number of PII entities that were redacted.
    /// </summary>
    public int RedactionCount => Redactions.Count;

    /// <summary>
    /// Whether any PII was found and redacted.
    /// </summary>
    public bool WasRedacted => Redactions.Count > 0;
}

/// <summary>
/// Information about a single redaction.
/// </summary>
public sealed class RedactionInfo
{
    /// <summary>
    /// The detected PII entity.
    /// </summary>
    public PiiEntity Entity { get; set; } = null!;

    /// <summary>
    /// The replacement text used.
    /// </summary>
    public string Replacement { get; set; } = string.Empty;

    /// <summary>
    /// Position of replacement in redacted text.
    /// </summary>
    public int ReplacementStartIndex { get; set; }
}
