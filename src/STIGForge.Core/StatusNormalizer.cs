namespace STIGForge.Core;

public static class StatusNormalizer
{
    public static string Normalize(string? status)
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
            "notreviewed" or "notchecked" or "nr" => "notreviewed",
            _ => s
        };
    }
}
