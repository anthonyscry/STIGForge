using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

/// <summary>
/// Filters controls by rule ID, severity (CAT I/II/III mapped to high/medium/low), and category.
/// Filters are conjunctive (AND) across filter types, disjunctive (OR) within a filter type.
/// </summary>
public sealed class ControlFilterService
{
    /// <summary>
    /// Filters controls based on provided criteria. Returns all controls if all filters are null.
    /// </summary>
    public IReadOnlyList<ControlRecord> Filter(
        IReadOnlyList<ControlRecord> controls,
        IReadOnlyList<string>? ruleIds,
        IReadOnlyList<string>? severities,
        IReadOnlyList<string>? categories)
    {
        if (controls is null) throw new ArgumentNullException(nameof(controls));

        if ((ruleIds is null || ruleIds.Count == 0) &&
            (severities is null || severities.Count == 0) &&
            (categories is null || categories.Count == 0))
        {
            return controls;
        }

        var result = new List<ControlRecord>(controls.Count);

        var ruleIdSet = ruleIds is { Count: > 0 }
            ? new HashSet<string>(ruleIds, StringComparer.OrdinalIgnoreCase)
            : null;

        var severitySet = severities is { Count: > 0 }
            ? NormalizeSeverities(severities)
            : null;

        var categorySet = categories is { Count: > 0 }
            ? new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var control in controls)
        {
            if (ruleIdSet != null && !MatchesRuleId(control, ruleIdSet))
                continue;
            if (severitySet != null && !MatchesSeverity(control, severitySet))
                continue;
            if (categorySet != null && !MatchesCategory(control, categorySet))
                continue;

            result.Add(control);
        }

        return result;
    }

    private static bool MatchesRuleId(ControlRecord control, HashSet<string> ruleIds)
    {
        if (!string.IsNullOrWhiteSpace(control.ExternalIds.RuleId) && ruleIds.Contains(control.ExternalIds.RuleId))
            return true;
        if (!string.IsNullOrWhiteSpace(control.ExternalIds.VulnId) && ruleIds.Contains(control.ExternalIds.VulnId))
            return true;
        if (!string.IsNullOrWhiteSpace(control.ControlId) && ruleIds.Contains(control.ControlId))
            return true;
        return false;
    }

    private static bool MatchesSeverity(ControlRecord control, HashSet<string> severities)
    {
        var normalized = NormalizeSeverity(control.Severity);
        return severities.Contains(normalized);
    }

    private static bool MatchesCategory(ControlRecord control, HashSet<string> categories)
    {
        // Category matches against BenchmarkId or SrgId from ExternalIds
        if (!string.IsNullOrWhiteSpace(control.ExternalIds.BenchmarkId) && categories.Contains(control.ExternalIds.BenchmarkId))
            return true;
        if (!string.IsNullOrWhiteSpace(control.ExternalIds.SrgId) && categories.Contains(control.ExternalIds.SrgId))
            return true;
        return false;
    }

    private static HashSet<string> NormalizeSeverities(IReadOnlyList<string> severities)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in severities)
        {
            normalized.Add(NormalizeSeverity(s));
        }
        return normalized;
    }

    /// <summary>
    /// Normalizes severity values. Accepts: high/medium/low, CAT I/CAT II/CAT III, cat1/cat2/cat3, I/II/III.
    /// </summary>
    public static string NormalizeSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return "unknown";

        var s = severity.Trim();

        // Already normalized
        if (s.Equals("high", StringComparison.OrdinalIgnoreCase)) return "high";
        if (s.Equals("medium", StringComparison.OrdinalIgnoreCase)) return "medium";
        if (s.Equals("low", StringComparison.OrdinalIgnoreCase)) return "low";

        // CAT I/II/III format
        if (s.Equals("CAT I", StringComparison.OrdinalIgnoreCase) || s.Equals("cat1", StringComparison.OrdinalIgnoreCase) || s.Equals("I", StringComparison.Ordinal))
            return "high";
        if (s.Equals("CAT II", StringComparison.OrdinalIgnoreCase) || s.Equals("cat2", StringComparison.OrdinalIgnoreCase) || s.Equals("II", StringComparison.Ordinal))
            return "medium";
        if (s.Equals("CAT III", StringComparison.OrdinalIgnoreCase) || s.Equals("cat3", StringComparison.OrdinalIgnoreCase) || s.Equals("III", StringComparison.Ordinal))
            return "low";

        return s.ToLowerInvariant();
    }
}
