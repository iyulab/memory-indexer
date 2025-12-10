using System.ComponentModel;
using MemoryIndexer.Intelligence.Security;
using ModelContextProtocol.Server;

namespace MemoryIndexer.Mcp.Tools;

/// <summary>
/// MCP tools for security operations.
/// Provides PII detection, redaction, and prompt injection defense.
/// </summary>
[McpServerToolType]
public sealed class SecurityTools
{
    private readonly IPiiDetector _piiDetector;
    private readonly IPromptInjectionDetector _injectionDetector;

    public SecurityTools(
        IPiiDetector piiDetector,
        IPromptInjectionDetector injectionDetector)
    {
        _piiDetector = piiDetector;
        _injectionDetector = injectionDetector;
    }

    /// <summary>
    /// Detect PII (Personally Identifiable Information) in text.
    /// Identifies names, SSN, credit cards, emails, phone numbers, and more.
    /// </summary>
    /// <param name="text">Text to analyze for PII.</param>
    /// <param name="minConfidence">Minimum confidence threshold (0.0 to 1.0).</param>
    /// <returns>List of detected PII entities.</returns>
    [McpServerTool, Description("Detect PII (names, SSN, credit cards, etc.) in text")]
    public async Task<PiiDetectionToolResult> DetectPii(
        [Description("Text to analyze for PII")] string text,
        [Description("Minimum confidence (0.0-1.0, default 0.5)")] float minConfidence = 0.5f)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PiiDetectionToolResult
            {
                Success = false,
                Message = "Text cannot be empty"
            };
        }

        var entities = await _piiDetector.DetectAsync(text, minConfidence);

        return new PiiDetectionToolResult
        {
            Success = true,
            ContainsPii = entities.Count > 0,
            Entities = entities.Select(e => new PiiEntityInfo
            {
                Type = e.Type.ToString(),
                Text = MaskSensitive(e.Text, e.Type),
                StartIndex = e.StartIndex,
                EndIndex = e.EndIndex,
                Confidence = e.Confidence
            }).ToList(),
            Message = entities.Count > 0
                ? $"Detected {entities.Count} PII entities"
                : "No PII detected"
        };
    }

    /// <summary>
    /// Redact PII from text by replacing with placeholders.
    /// </summary>
    /// <param name="text">Text to redact.</param>
    /// <param name="minConfidence">Minimum confidence threshold.</param>
    /// <param name="mode">Redaction mode: Replace, FullMask, PartialMask, Hash, or Remove.</param>
    /// <returns>Redacted text and redaction details.</returns>
    [McpServerTool, Description("Redact PII from text by replacing with placeholders")]
    public async Task<PiiRedactionToolResult> RedactPii(
        [Description("Text to redact")] string text,
        [Description("Minimum confidence (0.0-1.0, default 0.5)")] float minConfidence = 0.5f,
        [Description("Redaction mode: Replace (default), FullMask, PartialMask, Hash, Remove")] string mode = "Replace")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new PiiRedactionToolResult
            {
                Success = false,
                Message = "Text cannot be empty"
            };
        }

        var redactionMode = mode.ToLowerInvariant() switch
        {
            "fullmask" => RedactionMode.FullMask,
            "partialmask" => RedactionMode.PartialMask,
            "hash" => RedactionMode.Hash,
            "remove" => RedactionMode.Remove,
            _ => RedactionMode.Replace
        };

        var options = new PiiRedactionOptions
        {
            MinConfidence = minConfidence,
            Mode = redactionMode,
            IncludeTypeInReplacement = true
        };

        var result = await _piiDetector.RedactAsync(text, options);

        return new PiiRedactionToolResult
        {
            Success = true,
            RedactedText = result.RedactedText,
            WasRedacted = result.WasRedacted,
            RedactionCount = result.RedactionCount,
            Redactions = result.Redactions.Select(r => new RedactionDetail
            {
                Type = r.Entity.Type.ToString(),
                OriginalLength = r.Entity.Text.Length,
                Replacement = r.Replacement
            }).ToList(),
            Message = result.WasRedacted
                ? $"Redacted {result.RedactionCount} PII entities"
                : "No PII found to redact"
        };
    }

    /// <summary>
    /// Check if text is safe from prompt injection attacks.
    /// </summary>
    /// <param name="text">Text to analyze.</param>
    /// <returns>Safety assessment with detected patterns.</returns>
    [McpServerTool, Description("Check text for prompt injection attacks")]
    public async Task<InjectionDetectionToolResult> DetectPromptInjection(
        [Description("Text to analyze for injection attacks")] string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new InjectionDetectionToolResult
            {
                Success = false,
                Message = "Text cannot be empty"
            };
        }

        var result = await _injectionDetector.DetectAsync(text);

        return new InjectionDetectionToolResult
        {
            Success = true,
            IsDetected = result.IsDetected,
            RiskLevel = result.RiskLevel.ToString(),
            RiskScore = result.RiskScore,
            DetectedPatterns = result.DetectedPatterns.Select(p => new InjectionPatternInfo
            {
                Type = p.Type.ToString(),
                Description = p.Description,
                Confidence = p.Confidence,
                RiskContribution = p.RiskContribution
            }).ToList(),
            Recommendations = result.Recommendations,
            Message = result.IsDetected
                ? $"Detected {result.DetectedPatterns.Count} injection patterns (Risk: {result.RiskLevel})"
                : "No injection patterns detected"
        };
    }

    /// <summary>
    /// Sanitize text to neutralize potential prompt injection attacks.
    /// </summary>
    /// <param name="text">Text to sanitize.</param>
    /// <param name="mode">Sanitization mode: Neutralize, Remove, Block, or Escape.</param>
    /// <param name="minRiskToSanitize">Minimum risk level to trigger sanitization.</param>
    /// <returns>Sanitized text.</returns>
    [McpServerTool, Description("Sanitize text to neutralize prompt injection attacks")]
    public async Task<SanitizationToolResult> SanitizeInput(
        [Description("Text to sanitize")] string text,
        [Description("Mode: Neutralize (default), Remove, Block, Escape")] string mode = "Neutralize",
        [Description("Min risk level: None, Low, Medium, High, Critical")] string minRiskToSanitize = "Medium")
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SanitizationToolResult
            {
                Success = false,
                Message = "Text cannot be empty"
            };
        }

        var sanitizationMode = mode.ToLowerInvariant() switch
        {
            "remove" => SanitizationMode.Remove,
            "block" => SanitizationMode.Block,
            "escape" => SanitizationMode.Escape,
            _ => SanitizationMode.Neutralize
        };

        var riskLevel = minRiskToSanitize.ToLowerInvariant() switch
        {
            "none" => RiskLevel.None,
            "low" => RiskLevel.Low,
            "high" => RiskLevel.High,
            "critical" => RiskLevel.Critical,
            _ => RiskLevel.Medium
        };

        var options = new SanitizationOptions
        {
            Mode = sanitizationMode,
            MinRiskToSanitize = riskLevel,
            NormalizeUnicode = true,
            RemoveInvisibleChars = true,
            EscapeDelimiters = true
        };

        var result = await _injectionDetector.SanitizeAsync(text, options);

        return new SanitizationToolResult
        {
            Success = true,
            SanitizedText = result.SanitizedText,
            WasModified = result.WasModified,
            WasBlocked = result.WasBlocked,
            ModificationCount = result.Modifications.Count,
            OriginalRiskLevel = result.DetectionResult?.RiskLevel.ToString() ?? "None",
            Message = result.WasBlocked
                ? "Input blocked due to high-risk injection patterns"
                : result.WasModified
                    ? $"Applied {result.Modifications.Count} sanitization modifications"
                    : "No sanitization needed"
        };
    }

    /// <summary>
    /// Validate content before storing in memory.
    /// Combines PII detection and injection defense.
    /// </summary>
    /// <param name="content">Content to validate.</param>
    /// <param name="allowPii">Whether to allow PII (will warn but not block).</param>
    /// <param name="maxRiskLevel">Maximum allowed injection risk level.</param>
    /// <returns>Validation result with recommendations.</returns>
    [McpServerTool, Description("Validate content for both PII and injection risks before storage")]
    public async Task<ContentValidationResult> ValidateContent(
        [Description("Content to validate")] string content,
        [Description("Allow PII (warns but doesn't block)")] bool allowPii = false,
        [Description("Max allowed risk: None, Low, Medium (default: Low)")] string maxRiskLevel = "Low")
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ContentValidationResult
            {
                IsValid = false,
                Message = "Content cannot be empty"
            };
        }

        var maxRisk = maxRiskLevel.ToLowerInvariant() switch
        {
            "none" => RiskLevel.None,
            "medium" => RiskLevel.Medium,
            _ => RiskLevel.Low
        };

        // Check for PII
        var piiEntities = await _piiDetector.DetectAsync(content);
        var hasPii = piiEntities.Count > 0;

        // Check for injection
        var injectionResult = await _injectionDetector.DetectAsync(content);
        var hasInjection = injectionResult.IsDetected && (int)injectionResult.RiskLevel > (int)maxRisk;

        var warnings = new List<string>();
        var blockers = new List<string>();

        if (hasPii)
        {
            var piiWarning = $"Contains {piiEntities.Count} PII entities: {string.Join(", ", piiEntities.Select(e => e.Type).Distinct())}";
            if (allowPii)
                warnings.Add(piiWarning);
            else
                blockers.Add(piiWarning);
        }

        if (hasInjection)
        {
            blockers.Add($"Injection risk ({injectionResult.RiskLevel}) exceeds allowed level ({maxRisk})");
        }

        var isValid = blockers.Count == 0;

        return new ContentValidationResult
        {
            IsValid = isValid,
            HasPii = hasPii,
            PiiCount = piiEntities.Count,
            HasInjectionRisk = injectionResult.IsDetected,
            InjectionRiskLevel = injectionResult.RiskLevel.ToString(),
            Warnings = warnings,
            Blockers = blockers,
            Recommendations = isValid
                ? ["Content is safe to store"]
                : injectionResult.Recommendations.Concat(
                    hasPii && !allowPii ? ["Consider redacting PII before storage"] : []).ToList(),
            Message = isValid
                ? "Content validation passed"
                : $"Validation failed: {string.Join("; ", blockers)}"
        };
    }

    private static string MaskSensitive(string text, PiiType type)
    {
        // Show only type indicator, not actual sensitive data
        return type switch
        {
            PiiType.Email => text.Length > 5 ? $"{text[..2]}***@***" : "***@***",
            PiiType.CreditCard => text.Length > 4 ? $"****{text[^4..]}" : "****",
            PiiType.Ssn => "***-**-****",
            PiiType.PhoneNumber => text.Length > 4 ? $"***-***-{text[^4..]}" : "***-***-****",
            _ => text.Length > 4 ? $"{text[..2]}***" : "***"
        };
    }
}

#region Result Types

public sealed class PiiDetectionToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public bool ContainsPii { get; set; }
    public List<PiiEntityInfo> Entities { get; set; } = [];
}

public sealed class PiiEntityInfo
{
    public string Type { get; set; } = default!;
    public string Text { get; set; } = default!;
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public float Confidence { get; set; }
}

public sealed class PiiRedactionToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string RedactedText { get; set; } = default!;
    public bool WasRedacted { get; set; }
    public int RedactionCount { get; set; }
    public List<RedactionDetail> Redactions { get; set; } = [];
}

public sealed class RedactionDetail
{
    public string Type { get; set; } = default!;
    public int OriginalLength { get; set; }
    public string Replacement { get; set; } = default!;
}

public sealed class InjectionDetectionToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public bool IsDetected { get; set; }
    public string RiskLevel { get; set; } = default!;
    public float RiskScore { get; set; }
    public List<InjectionPatternInfo> DetectedPatterns { get; set; } = [];
    public List<string> Recommendations { get; set; } = [];
}

public sealed class InjectionPatternInfo
{
    public string Type { get; set; } = default!;
    public string Description { get; set; } = default!;
    public float Confidence { get; set; }
    public float RiskContribution { get; set; }
}

public sealed class SanitizationToolResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string SanitizedText { get; set; } = default!;
    public bool WasModified { get; set; }
    public bool WasBlocked { get; set; }
    public int ModificationCount { get; set; }
    public string OriginalRiskLevel { get; set; } = default!;
}

public sealed class ContentValidationResult
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
    public bool HasPii { get; set; }
    public int PiiCount { get; set; }
    public bool HasInjectionRisk { get; set; }
    public string InjectionRiskLevel { get; set; } = default!;
    public List<string> Warnings { get; set; } = [];
    public List<string> Blockers { get; set; } = [];
    public List<string> Recommendations { get; set; } = [];
}

#endregion
