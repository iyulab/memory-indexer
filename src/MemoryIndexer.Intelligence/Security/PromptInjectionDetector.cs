using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MemoryIndexer.Intelligence.Security;

/// <summary>
/// Pattern-based prompt injection detector.
/// Identifies common attack vectors in user input.
/// </summary>
public sealed partial class PromptInjectionDetector : IPromptInjectionDetector
{
    private readonly ILogger<PromptInjectionDetector> _logger;
    private readonly List<InjectionRule> _rules;

    // Invisible/homoglyph characters to detect
    private static readonly HashSet<char> InvisibleChars =
    [
        '\u200B', // Zero-width space
        '\u200C', // Zero-width non-joiner
        '\u200D', // Zero-width joiner
        '\uFEFF', // Byte order mark
        '\u00AD', // Soft hyphen
        '\u2060', // Word joiner
        '\u2061', // Function application
        '\u2062', // Invisible times
        '\u2063', // Invisible separator
        '\u2064', // Invisible plus
        '\u180E', // Mongolian vowel separator
    ];

    public PromptInjectionDetector(ILogger<PromptInjectionDetector> logger)
    {
        _logger = logger;
        _rules = InitializeRules();
    }

    /// <inheritdoc />
    public Task<InjectionDetectionResult> DetectAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var result = new InjectionDetectionResult();

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(result);
        }

        // Normalize text for detection
        var normalizedText = NormalizeText(text);

        // Check for invisible characters (potential smuggling)
        if (HasInvisibleChars(text))
        {
            result.DetectedPatterns.Add(new InjectionPattern
            {
                Type = InjectionType.TokenSmuggling,
                MatchedText = "[invisible characters detected]",
                Confidence = 0.7f,
                RiskContribution = 0.3f,
                Description = "Text contains invisible/zero-width characters that may hide payloads"
            });
        }

        // Apply detection rules
        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matches = rule.Pattern.Matches(normalizedText);
            foreach (Match match in matches)
            {
                if (!match.Success) continue;

                result.DetectedPatterns.Add(new InjectionPattern
                {
                    Type = rule.Type,
                    MatchedText = match.Value.Length > 100 ? match.Value[..100] + "..." : match.Value,
                    StartIndex = match.Index,
                    EndIndex = match.Index + match.Length,
                    Confidence = rule.Confidence,
                    RiskContribution = rule.RiskWeight,
                    Description = rule.Description
                });
            }
        }

        // Calculate overall risk
        if (result.DetectedPatterns.Count > 0)
        {
            result.IsDetected = true;
            result.RiskScore = Math.Min(1.0f, result.DetectedPatterns.Sum(p => p.RiskContribution));
            result.RiskLevel = ClassifyRisk(result.RiskScore);
            result.Recommendations = GenerateRecommendations(result);
        }

        _logger.LogDebug(
            "Injection detection complete: {Detected}, Risk: {Level} ({Score:P0})",
            result.IsDetected,
            result.RiskLevel,
            result.RiskScore);

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<SanitizationResult> SanitizeAsync(
        string text,
        SanitizationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new SanitizationOptions();

        var result = new SanitizationResult
        {
            OriginalText = text,
            SanitizedText = text
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        // Detect first
        var detection = await DetectAsync(text, cancellationToken);
        result.DetectionResult = detection;

        // Check if we should sanitize based on risk level
        if ((int)detection.RiskLevel < (int)options.MinRiskToSanitize)
        {
            return result;
        }

        // Handle block mode
        if (options.Mode == SanitizationMode.Block && detection.IsDetected)
        {
            result.WasBlocked = true;
            result.SanitizedText = string.Empty;
            result.WasModified = true;
            return result;
        }

        var sanitized = text;

        // Remove invisible characters
        if (options.RemoveInvisibleChars)
        {
            var before = sanitized;
            sanitized = RemoveInvisibleCharacters(sanitized);
            if (before != sanitized)
            {
                result.Modifications.Add(new SanitizationModification
                {
                    Type = "RemoveInvisible",
                    Original = "[invisible chars]",
                    Replacement = ""
                });
            }
        }

        // Normalize unicode
        if (options.NormalizeUnicode)
        {
            var before = sanitized;
            sanitized = NormalizeUnicode(sanitized);
            if (before != sanitized)
            {
                result.Modifications.Add(new SanitizationModification
                {
                    Type = "NormalizeUnicode",
                    Original = before,
                    Replacement = sanitized
                });
            }
        }

        // Escape delimiters
        if (options.EscapeDelimiters)
        {
            var before = sanitized;
            sanitized = EscapeDelimiters(sanitized);
            if (before != sanitized)
            {
                result.Modifications.Add(new SanitizationModification
                {
                    Type = "EscapeDelimiters",
                    Original = before,
                    Replacement = sanitized
                });
            }
        }

        // Apply mode-specific sanitization
        sanitized = options.Mode switch
        {
            SanitizationMode.Neutralize => NeutralizePatterns(sanitized, detection.DetectedPatterns, result.Modifications),
            SanitizationMode.Remove => RemovePatterns(sanitized, detection.DetectedPatterns, result.Modifications),
            SanitizationMode.Escape => EscapePatterns(sanitized),
            _ => sanitized
        };

        // Add data prefix if specified
        if (!string.IsNullOrEmpty(options.DataPrefix))
        {
            sanitized = $"{options.DataPrefix}\n{sanitized}";
        }

        // Apply max length
        if (options.MaxLength > 0 && sanitized.Length > options.MaxLength)
        {
            sanitized = sanitized[..options.MaxLength];
            result.Modifications.Add(new SanitizationModification
            {
                Type = "Truncate",
                Original = $"[{text.Length} chars]",
                Replacement = $"[{sanitized.Length} chars]"
            });
        }

        result.SanitizedText = sanitized;
        result.WasModified = result.Modifications.Count > 0 || text != sanitized;

        _logger.LogInformation(
            "Sanitization complete: {Modified}, {ModCount} modifications",
            result.WasModified,
            result.Modifications.Count);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> IsSafeAsync(
        string text,
        RiskLevel maxAllowedRisk = RiskLevel.Low,
        CancellationToken cancellationToken = default)
    {
        var detection = await DetectAsync(text, cancellationToken);
        return (int)detection.RiskLevel <= (int)maxAllowedRisk;
    }

    #region Private Methods

    private static string NormalizeText(string text)
    {
        // Normalize to Form C (composed) for consistent matching
        return text.Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    private static bool HasInvisibleChars(string text)
    {
        return text.Any(c => InvisibleChars.Contains(c));
    }

    private static string RemoveInvisibleCharacters(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!InvisibleChars.Contains(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string NormalizeUnicode(string text)
    {
        // Convert common homoglyphs to ASCII
        var replacements = new Dictionary<char, char>
        {
            {'а', 'a'}, {'е', 'e'}, {'о', 'o'}, {'р', 'p'}, {'с', 'c'}, {'х', 'x'}, {'у', 'y'},
            {'А', 'A'}, {'В', 'B'}, {'Е', 'E'}, {'К', 'K'}, {'М', 'M'}, {'Н', 'H'}, {'О', 'O'},
            {'Р', 'P'}, {'С', 'C'}, {'Т', 'T'}, {'Х', 'X'},
            {'ı', 'i'}, {'ℓ', 'l'}, {'ℐ', 'I'}, {'ℑ', 'I'},
            {'０', '0'}, {'１', '1'}, {'２', '2'}, {'３', '3'}, {'４', '4'},
            {'５', '5'}, {'６', '6'}, {'７', '7'}, {'８', '8'}, {'９', '9'},
        };

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(replacements.GetValueOrDefault(c, c));
        }
        return sb.ToString();
    }

    private static string EscapeDelimiters(string text)
    {
        // Escape potential instruction delimiters
        return text
            .Replace("```", "'''")
            .Replace("---", "___")
            .Replace("###", "===")
            .Replace("<<<", "((( ")
            .Replace(">>>", " )))")
            .Replace("[INST]", "[inst]")
            .Replace("[/INST]", "[/inst]")
            .Replace("<|", "< |")
            .Replace("|>", "| >");
    }

    private static string NeutralizePatterns(
        string text,
        List<InjectionPattern> patterns,
        List<SanitizationModification> modifications)
    {
        var result = text;

        // Sort by position descending to maintain indices
        foreach (var pattern in patterns.OrderByDescending(p => p.StartIndex))
        {
            if (pattern.StartIndex < 0 || pattern.EndIndex > result.Length)
                continue;

            var original = result[pattern.StartIndex..pattern.EndIndex];
            var neutralized = $"[user_input: {pattern.Type}]";

            result = result[..pattern.StartIndex] + neutralized + result[pattern.EndIndex..];

            modifications.Add(new SanitizationModification
            {
                Type = "Neutralize",
                Original = original.Length > 50 ? original[..50] + "..." : original,
                Replacement = neutralized,
                Position = pattern.StartIndex
            });
        }

        return result;
    }

    private static string RemovePatterns(
        string text,
        List<InjectionPattern> patterns,
        List<SanitizationModification> modifications)
    {
        var result = text;

        foreach (var pattern in patterns.OrderByDescending(p => p.StartIndex))
        {
            if (pattern.StartIndex < 0 || pattern.EndIndex > result.Length)
                continue;

            var original = result[pattern.StartIndex..pattern.EndIndex];
            result = result[..pattern.StartIndex] + result[pattern.EndIndex..];

            modifications.Add(new SanitizationModification
            {
                Type = "Remove",
                Original = original.Length > 50 ? original[..50] + "..." : original,
                Replacement = "",
                Position = pattern.StartIndex
            });
        }

        return result;
    }

    private static string EscapePatterns(string text)
    {
        // Wrap user input in clear data markers
        return $"<user_data>\n{text}\n</user_data>";
    }

    private static RiskLevel ClassifyRisk(float score)
    {
        return score switch
        {
            >= 0.8f => RiskLevel.Critical,
            >= 0.6f => RiskLevel.High,
            >= 0.3f => RiskLevel.Medium,
            > 0 => RiskLevel.Low,
            _ => RiskLevel.None
        };
    }

    private static List<string> GenerateRecommendations(InjectionDetectionResult result)
    {
        var recommendations = new List<string>();

        if (result.RiskLevel >= RiskLevel.High)
        {
            recommendations.Add("Consider rejecting this input entirely");
            recommendations.Add("Log this attempt for security review");
        }

        if (result.DetectedPatterns.Any(p => p.Type == InjectionType.Jailbreak))
        {
            recommendations.Add("Do not process instructions from this input");
        }

        if (result.DetectedPatterns.Any(p => p.Type == InjectionType.DataExfiltration))
        {
            recommendations.Add("Do not expose system information in responses");
        }

        if (result.DetectedPatterns.Any(p => p.Type == InjectionType.TokenSmuggling))
        {
            recommendations.Add("Normalize text before processing");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("Consider sanitizing input before use");
        }

        return recommendations;
    }

    private List<InjectionRule> InitializeRules()
    {
        return
        [
            // Jailbreak attempts
            new InjectionRule(
                InjectionType.Jailbreak,
                JailbreakIgnoreRegex(),
                0.9f, 0.4f,
                "Attempts to make the model ignore previous instructions"),

            new InjectionRule(
                InjectionType.Jailbreak,
                JailbreakForgetRegex(),
                0.85f, 0.35f,
                "Attempts to make the model forget or discard instructions"),

            // Instruction override
            new InjectionRule(
                InjectionType.InstructionOverride,
                InstructionNewRoleRegex(),
                0.85f, 0.35f,
                "Attempts to assign a new role or identity"),

            new InjectionRule(
                InjectionType.InstructionOverride,
                InstructionSystemRegex(),
                0.9f, 0.4f,
                "Contains system-level instruction markers"),

            // Role manipulation
            new InjectionRule(
                InjectionType.RoleManipulation,
                RolePretendRegex(),
                0.7f, 0.25f,
                "Attempts to manipulate model identity through role-play"),

            new InjectionRule(
                InjectionType.RoleManipulation,
                RoleActAsRegex(),
                0.65f, 0.2f,
                "Requests model to act as different entity"),

            // Data exfiltration
            new InjectionRule(
                InjectionType.DataExfiltration,
                ExfilSystemPromptRegex(),
                0.95f, 0.45f,
                "Attempts to extract system prompt or instructions"),

            new InjectionRule(
                InjectionType.DataExfiltration,
                ExfilRepeatRegex(),
                0.8f, 0.3f,
                "Requests repetition of system-level content"),

            // Delimiter attacks
            new InjectionRule(
                InjectionType.DelimiterAttack,
                DelimiterMarkdownRegex(),
                0.6f, 0.2f,
                "Contains potential instruction delimiters"),

            new InjectionRule(
                InjectionType.DelimiterAttack,
                DelimiterXmlRegex(),
                0.7f, 0.25f,
                "Contains XML-like instruction markers"),

            // Context manipulation
            new InjectionRule(
                InjectionType.ContextManipulation,
                ContextEndRegex(),
                0.75f, 0.3f,
                "Attempts to end or reset conversation context"),

            // Prompt leakage
            new InjectionRule(
                InjectionType.PromptLeakage,
                LeakageInstructionsRegex(),
                0.85f, 0.35f,
                "Requests disclosure of system instructions"),

            // Encoded payloads
            new InjectionRule(
                InjectionType.EncodedPayload,
                EncodedBase64Regex(),
                0.5f, 0.15f,
                "Contains potential base64 encoded content"),
        ];
    }

    #endregion

    #region Regex Patterns

    [GeneratedRegex(@"(?:ignore|disregard|forget|skip|bypass|override)\s+(?:all\s+)?(?:previous|prior|above|earlier|initial|system)\s+(?:instructions?|prompts?|rules?|guidelines?|constraints?)", RegexOptions.IgnoreCase)]
    private static partial Regex JailbreakIgnoreRegex();

    [GeneratedRegex(@"(?:do\s+not|don't)\s+(?:follow|obey|listen|adhere)\s+(?:to\s+)?(?:previous|prior|above|earlier)\s+(?:instructions?|rules?)", RegexOptions.IgnoreCase)]
    private static partial Regex JailbreakForgetRegex();

    [GeneratedRegex(@"(?:you\s+are\s+now|from\s+now\s+on|henceforth|starting\s+now|as\s+of\s+now)\s+(?:a|an|the)?\s*\w+", RegexOptions.IgnoreCase)]
    private static partial Regex InstructionNewRoleRegex();

    [GeneratedRegex(@"\[\s*(?:system|inst(?:ruction)?|assistant|user)\s*\]|\<\|\s*(?:system|im_start|im_end)\s*\|\>|```\s*(?:system|instructions?)", RegexOptions.IgnoreCase)]
    private static partial Regex InstructionSystemRegex();

    [GeneratedRegex(@"(?:pretend|imagine|suppose|assume|act\s+like)\s+(?:you\s+are|you're|that\s+you)\s+(?:not\s+)?(?:a|an|the)?\s*\w+", RegexOptions.IgnoreCase)]
    private static partial Regex RolePretendRegex();

    [GeneratedRegex(@"(?:act|behave|respond|roleplay|function)\s+(?:as|like)\s+(?:a|an|the)?\s*(?:different|new|unrestricted|unfiltered)\s*\w*", RegexOptions.IgnoreCase)]
    private static partial Regex RoleActAsRegex();

    [GeneratedRegex(@"(?:reveal|show|display|tell|repeat|print|output|echo)\s+(?:me\s+)?(?:your|the)?\s*(?:system\s+)?(?:prompt|instructions?|rules?|guidelines?|configuration|settings?)", RegexOptions.IgnoreCase)]
    private static partial Regex ExfilSystemPromptRegex();

    [GeneratedRegex(@"(?:repeat|say|echo|print|output)\s+(?:everything|all|the\s+text)\s+(?:above|before|previously|that\s+came\s+before)", RegexOptions.IgnoreCase)]
    private static partial Regex ExfilRepeatRegex();

    [GeneratedRegex(@"```\s*(?:system|instructions?|prompt)\s*\n|\-{3,}\s*(?:system|new\s+instructions?)|\#{3,}\s*(?:new\s+)?(?:system|instructions?)", RegexOptions.IgnoreCase)]
    private static partial Regex DelimiterMarkdownRegex();

    [GeneratedRegex(@"<(?:system|instruction|prompt|assistant|user|context)>|<\/(?:system|instruction|prompt|assistant|user|context)>", RegexOptions.IgnoreCase)]
    private static partial Regex DelimiterXmlRegex();

    [GeneratedRegex(@"(?:end|stop|reset|clear|terminate)\s+(?:of\s+)?(?:conversation|context|chat|session|previous)", RegexOptions.IgnoreCase)]
    private static partial Regex ContextEndRegex();

    [GeneratedRegex(@"(?:what|show|reveal|tell)\s+(?:are|is|me)?\s*(?:your)?\s*(?:initial|original|hidden|secret|real|actual)\s*(?:instructions?|prompt|rules?)", RegexOptions.IgnoreCase)]
    private static partial Regex LeakageInstructionsRegex();

    [GeneratedRegex(@"[A-Za-z0-9+/]{50,}={0,2}")]
    private static partial Regex EncodedBase64Regex();

    #endregion
}

/// <summary>
/// Rule for detecting injection patterns.
/// </summary>
internal sealed class InjectionRule
{
    public InjectionType Type { get; }
    public Regex Pattern { get; }
    public float Confidence { get; }
    public float RiskWeight { get; }
    public string Description { get; }

    public InjectionRule(InjectionType type, Regex pattern, float confidence, float riskWeight, string description)
    {
        Type = type;
        Pattern = pattern;
        Confidence = confidence;
        RiskWeight = riskWeight;
        Description = description;
    }
}
