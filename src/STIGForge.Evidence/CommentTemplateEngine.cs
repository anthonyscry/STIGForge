using System.Text;

namespace STIGForge.Evidence;

/// <summary>
/// Generates human-readable COMMENTS text for CKL export based on
/// control status, evidence, and verification context.
/// Pure function  -  no I/O, no side effects.
/// </summary>
public static class CommentTemplateEngine
{
    private const string StiGForgeSentinel = "--- STIGForge Evidence ---";

    public static string Generate(
        string? status,
        string? keyEvidence,
        string? toolName,
        DateTimeOffset? verifiedAt,
        IReadOnlyList<string> artifactFileNames)
    {
        var sb = new StringBuilder();

        // Embed marker so ContainsSentinel detects this on re-export (idempotency).
        sb.AppendLine("[STIGForge Evidence Report]");

        // Status rationale
        var normalizedStatus = NormalizeStatus(status);
        sb.Append(normalizedStatus switch
        {
            "pass" => "Verified compliant.",
            "fail" => "Open finding.",
            "notapplicable" => "Not applicable.",
            "notreviewed" => "Awaiting review. No automated check available for this control.",
            _ => "Status: " + (status ?? "unknown") + "."
        });

        // Key evidence point
        if (!string.IsNullOrWhiteSpace(keyEvidence))
        {
            sb.Append(' ');
            sb.Append(keyEvidence.Trim());
            if (!keyEvidence.TrimEnd().EndsWith('.'))
                sb.Append('.');
        }

        // Verification context
        if (!string.IsNullOrWhiteSpace(toolName) && verifiedAt.HasValue)
        {
            sb.Append(' ');
            if (string.Equals(toolName, "Merged", StringComparison.OrdinalIgnoreCase))
                sb.Append("Scan verified by multiple tools on ");
            else
                sb.AppendFormat("Scan verified by {0} on ", toolName);
            sb.Append(verifiedAt.Value.ToString("yyyy-MM-dd"));
            sb.Append('.');
        }

        // Artifact references
        if (artifactFileNames.Count > 0)
        {
            sb.Append(" Evidence artifacts: ");
            sb.Append(string.Join(", ", artifactFileNames));
            sb.Append('.');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check if text already contains STIGForge evidence markers.
    /// Matches both the separator sentinel and the evidence report header.
    /// Used for idempotency  -  prevent double-appending on re-export.
    /// </summary>
    public static bool ContainsSentinel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.Contains(StiGForgeSentinel, StringComparison.Ordinal)
            || text.Contains("STIGForge Evidence Report", StringComparison.Ordinal);
    }

    /// <summary>The separator used when appending evidence to existing content.</summary>
    public static string Separator => "\n\n" + StiGForgeSentinel + "\n";

    private static string NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "unknown";

        var s = status.Trim().ToLowerInvariant()
            .Replace("_", "").Replace("-", "").Replace(" ", "");

        return s switch
        {
            "pass" or "notafinding" or "compliant" => "pass",
            "fail" or "open" or "noncompliant" or "error" => "fail",
            "notapplicable" or "na" => "notapplicable",
            "notreviewed" or "notchecked" or "informational" => "notreviewed",
            _ => s
        };
    }
}
