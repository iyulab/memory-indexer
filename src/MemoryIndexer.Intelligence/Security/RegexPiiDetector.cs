using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Security;

/// <summary>
/// Regex-based PII detector for local detection without external dependencies.
/// Provides pattern matching for common PII types as a fallback when Presidio is unavailable.
/// </summary>
public sealed partial class RegexPiiDetector : IPiiDetector
{
    private readonly ILogger<RegexPiiDetector> _logger;
    private readonly List<PiiPattern> _patterns;

    public RegexPiiDetector(ILogger<RegexPiiDetector> logger)
    {
        _logger = logger;
        _patterns = InitializePatterns();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PiiEntity>> DetectAsync(
        string text,
        float minConfidence = 0.5f,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult<IReadOnlyList<PiiEntity>>([]);
        }

        var entities = new List<PiiEntity>();

        foreach (var pattern in _patterns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matches = pattern.Regex.Matches(text);
            foreach (Match match in matches)
            {
                if (!match.Success) continue;

                var confidence = CalculateConfidence(pattern, match.Value);
                if (confidence < minConfidence) continue;

                entities.Add(new PiiEntity
                {
                    Type = pattern.Type,
                    Text = match.Value,
                    StartIndex = match.Index,
                    EndIndex = match.Index + match.Length,
                    Confidence = confidence,
                    RecognizerName = "RegexPiiDetector"
                });
            }
        }

        // Remove overlapping entities, keeping highest confidence
        var deduplicated = RemoveOverlaps(entities);

        _logger.LogDebug("Detected {Count} PII entities in text", deduplicated.Count);

        return Task.FromResult<IReadOnlyList<PiiEntity>>(deduplicated);
    }

    /// <inheritdoc />
    public async Task<PiiRedactionResult> RedactAsync(
        string text,
        PiiRedactionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PiiRedactionOptions();

        var entities = await DetectAsync(text, options.MinConfidence, cancellationToken);

        // Filter entities based on options
        var toRedact = entities
            .Where(e => ShouldRedact(e, options))
            .OrderByDescending(e => e.StartIndex)
            .ToList();

        var result = new PiiRedactionResult
        {
            OriginalText = text,
            RedactedText = text
        };

        // Apply redactions in reverse order to preserve indices
        foreach (var entity in toRedact)
        {
            var replacement = GetReplacement(entity, options);
            var before = result.RedactedText[..entity.StartIndex];
            var after = result.RedactedText[entity.EndIndex..];
            result.RedactedText = before + replacement + after;

            result.Redactions.Add(new RedactionInfo
            {
                Entity = entity,
                Replacement = replacement,
                ReplacementStartIndex = entity.StartIndex
            });
        }

        // Reverse to get chronological order
        result.Redactions.Reverse();

        _logger.LogInformation("Redacted {Count} PII entities from text", result.RedactionCount);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> ContainsPiiAsync(
        string text,
        float minConfidence = 0.5f,
        CancellationToken cancellationToken = default)
    {
        var entities = await DetectAsync(text, minConfidence, cancellationToken);
        return entities.Count > 0;
    }

    private static bool ShouldRedact(PiiEntity entity, PiiRedactionOptions options)
    {
        // Check explicit ignore list
        if (options.TypesToIgnore.Contains(entity.Type))
            return false;

        // Check explicit include list
        if (options.TypesToRedact.Count > 0)
            return options.TypesToRedact.Contains(entity.Type);

        // Default: redact all
        return true;
    }

    private static string GetReplacement(PiiEntity entity, PiiRedactionOptions options)
    {
        return options.Mode switch
        {
            RedactionMode.Replace => options.IncludeTypeInReplacement
                ? $"[{entity.Type.ToString().ToUpperInvariant()}]"
                : options.ReplacementText ?? "[REDACTED]",

            RedactionMode.FullMask => new string(options.MaskCharacter, entity.Text.Length),

            RedactionMode.PartialMask => CreatePartialMask(entity.Text, options),

            RedactionMode.Hash => $"[HASH:{ComputeHash(entity.Text)}]",

            RedactionMode.Remove => string.Empty,

            _ => "[REDACTED]"
        };
    }

    private static string CreatePartialMask(string text, PiiRedactionOptions options)
    {
        var showStart = Math.Min(options.PartialMaskShowStart, text.Length / 2);
        var showEnd = Math.Min(options.PartialMaskShowEnd, text.Length / 2);
        var maskLength = Math.Max(0, text.Length - showStart - showEnd);

        var sb = new StringBuilder();
        if (showStart > 0)
            sb.Append(text[..showStart]);
        sb.Append(options.MaskCharacter, maskLength);
        if (showEnd > 0)
            sb.Append(text[^showEnd..]);

        return sb.ToString();
    }

    private static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes)[..8];
    }

    private static float CalculateConfidence(PiiPattern pattern, string value)
    {
        var baseConfidence = pattern.BaseConfidence;

        // Apply validators if any
        if (pattern.Validator != null)
        {
            if (pattern.Validator(value))
                baseConfidence = Math.Min(1.0f, baseConfidence + 0.2f);
            else
                baseConfidence = Math.Max(0.0f, baseConfidence - 0.3f);
        }

        return baseConfidence;
    }

    private static List<PiiEntity> RemoveOverlaps(List<PiiEntity> entities)
    {
        if (entities.Count <= 1)
            return entities;

        var sorted = entities.OrderBy(e => e.StartIndex).ThenByDescending(e => e.Confidence).ToList();
        var result = new List<PiiEntity>();

        foreach (var entity in sorted)
        {
            var overlaps = result.Any(e =>
                (entity.StartIndex >= e.StartIndex && entity.StartIndex < e.EndIndex) ||
                (entity.EndIndex > e.StartIndex && entity.EndIndex <= e.EndIndex));

            if (!overlaps)
                result.Add(entity);
        }

        return result;
    }

    private List<PiiPattern> InitializePatterns()
    {
        return
        [
            // Email - high confidence
            new PiiPattern(PiiType.Email, EmailRegex(), 0.95f),

            // SSN - US Social Security Number
            new PiiPattern(PiiType.Ssn, SsnRegex(), 0.9f, ValidateSsn),

            // Credit Card - Luhn validation
            new PiiPattern(PiiType.CreditCard, CreditCardRegex(), 0.8f, ValidateLuhn),

            // Phone Numbers - various formats
            new PiiPattern(PiiType.PhoneNumber, PhoneUsRegex(), 0.85f),
            new PiiPattern(PiiType.PhoneNumber, PhoneIntlRegex(), 0.75f),

            // IP Addresses
            new PiiPattern(PiiType.IpAddress, IpV4Regex(), 0.9f, ValidateIpV4),
            new PiiPattern(PiiType.IpAddress, IpV6Regex(), 0.85f),

            // URLs (may contain sensitive paths)
            new PiiPattern(PiiType.Url, UrlRegex(), 0.7f),

            // Dates (potential DOB)
            new PiiPattern(PiiType.Date, DateIsoRegex(), 0.6f),
            new PiiPattern(PiiType.Date, DateUsRegex(), 0.5f),

            // Bank Account / Routing Numbers
            new PiiPattern(PiiType.BankAccount, BankAccountRegex(), 0.7f),
            new PiiPattern(PiiType.FinancialId, IbanRegex(), 0.85f, ValidateIban),
            new PiiPattern(PiiType.FinancialId, RoutingNumberRegex(), 0.75f, ValidateRoutingNumber),

            // Passport Numbers (simplified patterns)
            new PiiPattern(PiiType.Passport, PassportUsRegex(), 0.7f),

            // Driver's License (US patterns - simplified)
            new PiiPattern(PiiType.DriversLicense, DriversLicenseRegex(), 0.6f),

            // Person Names (capitalized words - lower confidence)
            new PiiPattern(PiiType.PersonName, PersonNameRegex(), 0.4f)
        ];
    }

    #region Validators

    private static bool ValidateSsn(string ssn)
    {
        // Remove non-digits
        var digits = new string(ssn.Where(char.IsDigit).ToArray());
        if (digits.Length != 9) return false;

        // Invalid SSNs
        if (digits.StartsWith("000") || digits.StartsWith("666"))
            return false;
        if (digits[..3] == "900" || int.Parse(digits[..3]) > 899)
            return false;
        if (digits.Substring(3, 2) == "00")
            return false;
        if (digits[5..] == "0000")
            return false;

        return true;
    }

    private static bool ValidateLuhn(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (digits.Length < 13 || digits.Length > 19)
            return false;

        var sum = 0;
        var alternate = false;

        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var n = digits[i] - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                    n -= 9;
            }
            sum += n;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    private static bool ValidateIpV4(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4) return false;

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var num))
                return false;
            if (num < 0 || num > 255)
                return false;
        }

        return true;
    }

    private static bool ValidateIban(string iban)
    {
        // Basic IBAN validation
        var cleaned = iban.Replace(" ", "").ToUpperInvariant();
        if (cleaned.Length < 15 || cleaned.Length > 34)
            return false;

        // Move first 4 chars to end
        var rearranged = cleaned[4..] + cleaned[..4];

        // Convert letters to numbers (A=10, B=11, etc.)
        var numeric = new StringBuilder();
        foreach (var c in rearranged)
        {
            if (char.IsDigit(c))
                numeric.Append(c);
            else if (char.IsLetter(c))
                numeric.Append(c - 'A' + 10);
            else
                return false;
        }

        // Check mod 97
        var numStr = numeric.ToString();
        var remainder = 0;
        foreach (var c in numStr)
        {
            remainder = (remainder * 10 + (c - '0')) % 97;
        }

        return remainder == 1;
    }

    private static bool ValidateRoutingNumber(string routing)
    {
        var digits = new string(routing.Where(char.IsDigit).ToArray());
        if (digits.Length != 9) return false;

        // Checksum validation
        var sum = 0;
        int[] weights = [3, 7, 1, 3, 7, 1, 3, 7, 1];
        for (var i = 0; i < 9; i++)
        {
            sum += (digits[i] - '0') * weights[i];
        }

        return sum % 10 == 0;
    }

    #endregion

    #region Regex Patterns

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b\d{3}[-.\s]?\d{2}[-.\s]?\d{4}\b")]
    private static partial Regex SsnRegex();

    [GeneratedRegex(@"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13}|6(?:011|5[0-9]{2})[0-9]{12}|(?:2131|1800|35\d{3})\d{11})\b")]
    private static partial Regex CreditCardRegex();

    [GeneratedRegex(@"\b(?:\+?1[-.\s]?)?\(?[0-9]{3}\)?[-.\s]?[0-9]{3}[-.\s]?[0-9]{4}\b")]
    private static partial Regex PhoneUsRegex();

    [GeneratedRegex(@"\+[0-9]{1,3}[-.\s]?[0-9]{1,14}")]
    private static partial Regex PhoneIntlRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b")]
    private static partial Regex IpV4Regex();

    [GeneratedRegex(@"(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}|(?:[0-9a-fA-F]{1,4}:){1,7}:|(?:[0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}")]
    private static partial Regex IpV6Regex();

    [GeneratedRegex(@"https?://[^\s<>\""]+")]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"\b\d{4}[-/]\d{2}[-/]\d{2}\b")]
    private static partial Regex DateIsoRegex();

    [GeneratedRegex(@"\b\d{1,2}[-/]\d{1,2}[-/]\d{2,4}\b")]
    private static partial Regex DateUsRegex();

    [GeneratedRegex(@"\b[0-9]{8,17}\b")]
    private static partial Regex BankAccountRegex();

    [GeneratedRegex(@"\b[A-Z]{2}[0-9]{2}[A-Z0-9]{4}[0-9]{7}(?:[A-Z0-9]?){0,16}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"\b[0-9]{9}\b")]
    private static partial Regex RoutingNumberRegex();

    [GeneratedRegex(@"\b[A-Z][0-9]{8}\b", RegexOptions.IgnoreCase)]
    private static partial Regex PassportUsRegex();

    [GeneratedRegex(@"\b[A-Z][0-9]{7,12}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DriversLicenseRegex();

    [GeneratedRegex(@"\b(?:[A-Z][a-z]+\s+){1,2}[A-Z][a-z]+\b")]
    private static partial Regex PersonNameRegex();

    #endregion
}

/// <summary>
/// Pattern definition for PII detection.
/// </summary>
internal sealed class PiiPattern
{
    public PiiType Type { get; }
    public Regex Regex { get; }
    public float BaseConfidence { get; }
    public Func<string, bool>? Validator { get; }

    public PiiPattern(PiiType type, Regex regex, float baseConfidence, Func<string, bool>? validator = null)
    {
        Type = type;
        Regex = regex;
        BaseConfidence = baseConfidence;
        Validator = validator;
    }
}
