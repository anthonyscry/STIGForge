using System.Text;

namespace STIGForge.Core;

/// <summary>
/// Centralized status normalization for STIG compliance statuses.
/// Replaces duplicated NormalizeToken/NormalizeStatus methods across 5 services.
///
/// Four output formats for different contexts:
///   ToCanonical  → "Pass" / "Fail" / "NotApplicable" / "Open" (display, storage)
///   ToCklFormat  → "NotAFinding" / "Open" / "Not_Applicable" / "Not_Reviewed" (CKL XML)
///   ToAbbreviated → "Pass" / "Fail" / "NA" / "NR" (CSV, matrix)
///   ToLowerToken  → "pass" / "fail" / "notapplicable" / "notreviewed" (internal matching)
/// </summary>
public static class StatusNormalizer
{
    /// <summary>
    /// Strip all non-alphanumeric characters, lowercase. Shared by all normalizers.
    /// </summary>
    public static string NormalizeToken(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return string.Empty;

        var source = status.Trim().ToLowerInvariant();
        var sb = new StringBuilder(source.Length);
        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Canonical PascalCase format: "Pass", "Fail", "NotApplicable", "Open".
    /// Used by ManualAnswerService, DriftDetectionService, BundleMissionSummaryService.
    /// </summary>
    public static string ToCanonical(string? status)
    {
        var token = NormalizeToken(status);

        return token switch
        {
            "pass" or "notafinding" or "compliant" or "closed" => "Pass",
            "fail" or "noncompliant" => "Fail",
            "notapplicable" or "na" => "NotApplicable",
            "open" or "notreviewed" or "notchecked" or "informational" or "error" or "" => "Open",
            _ => "Open"
        };
    }

    /// <summary>
    /// CKL XML format: "NotAFinding", "Open", "Not_Applicable", "Not_Reviewed".
    /// Used by CklMergeService.
    /// </summary>
    public static string ToCklFormat(string? status)
    {
        var token = NormalizeToken(status);

        return token switch
        {
            "pass" or "notafinding" or "compliant" or "closed" => "NotAFinding",
            "fail" or "open" or "noncompliant" or "error" => "Open",
            "notapplicable" or "na" => "Not_Applicable",
            "notreviewed" or "notchecked" or "informational" or "" => "Not_Reviewed",
            _ => "Not_Reviewed"
        };
    }

    /// <summary>
    /// Abbreviated format: "Pass", "Fail", "NA", "NR".
    /// Used by FleetSummaryService for CSV/matrix output.
    /// </summary>
    public static string ToAbbreviated(string? status)
    {
        var token = NormalizeToken(status);

        return token switch
        {
            "pass" or "notafinding" or "compliant" or "closed" => "Pass",
            "fail" or "open" or "noncompliant" or "error" => "Fail",
            "notapplicable" or "na" => "NA",
            "notreviewed" or "notchecked" or "informational" or "" => "NR",
            _ => "NR"
        };
    }

    /// <summary>
    /// Lowercase token format: "pass", "fail", "notapplicable", "notreviewed", "unknown".
    /// Used by CommentTemplateEngine for prose generation.
    /// </summary>
    public static string ToLowerToken(string? status)
    {
        var token = NormalizeToken(status);

        return token switch
        {
            "pass" or "notafinding" or "compliant" or "closed" => "pass",
            "fail" or "open" or "noncompliant" or "error" => "fail",
            "notapplicable" or "na" => "notapplicable",
            "notreviewed" or "notchecked" => "notreviewed",
            "" => "unknown",
            _ => token
        };
    }
}
