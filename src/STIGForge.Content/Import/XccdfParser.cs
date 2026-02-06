using System.Xml;
using System.Xml.Linq;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public static class XccdfParser
{
    private const string XccdfNamespace = "http://checklists.nist.gov/xccdf/1.2";

    public static IReadOnlyList<ControlRecord> Parse(string xmlPath, string packName)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true,
            Async = false
        };

        using var reader = XmlReader.Create(xmlPath, settings);
        var doc = XDocument.Load(reader, LoadOptions.None);
        var ns = (XNamespace)XccdfNamespace;

        var benchmarkId = doc.Root?.Attribute("id")?.Value ?? Path.GetFileNameWithoutExtension(xmlPath);
        var results = new List<ControlRecord>();

        foreach (var rule in doc.Descendants(ns + "Rule"))
        {
            var control = ParseRule(rule, benchmarkId, packName, ns);
            if (control != null) results.Add(control);
        }

        return results;
    }

    private static ControlRecord? ParseRule(XElement rule, string benchmarkId, string packName, XNamespace ns)
    {
        var ruleId = rule.Attribute("id")?.Value;
        var severity = rule.Attribute("severity")?.Value ?? "unknown";
        var title = rule.Element(ns + "title")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(title)) title = ruleId ?? "Untitled";

        var description = rule.Element(ns + "description")?.Value?.Trim();
        var fixText = rule.Element(ns + "fixtext")?.Value?.Trim();

        var check = rule.Element(ns + "check");
        var checkSystem = check?.Attribute("system")?.Value;
        string? checkContent = null;
        if (check != null)
        {
            var parts = check.Elements(ns + "check-content")
                .Select(e => (e.Value ?? string.Empty).Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            if (parts.Count > 0)
                checkContent = string.Join(Environment.NewLine, parts);
        }

        // Determine if manual check
        var isManual = IsManualCheck(checkSystem, checkContent);

        // Build external IDs
        var external = new ExternalIds
        {
            VulnId = ExtractVulnId(ruleId, title ?? string.Empty),
            RuleId = ruleId,
            SrgId = null,
            BenchmarkId = benchmarkId
        };

        // Build applicability (placeholder values)
        var app = new Applicability
        {
            OsTarget = OsTarget.Win11,
            RoleTags = Array.Empty<RoleTemplate>(),
            ClassificationScope = ScopeTag.Unknown,
            Confidence = Confidence.Low
        };

        // Build revision info
        var rev = new RevisionInfo
        {
            PackName = packName,
            BenchmarkVersion = null,
            BenchmarkRelease = null,
            BenchmarkDate = null
        };

        return new ControlRecord
        {
            ControlId = Guid.NewGuid().ToString("n"),
            ExternalIds = external,
            Title = title ?? string.Empty,
            Severity = severity,
            Discussion = description,
            CheckText = checkContent,
            FixText = fixText,
            IsManual = isManual,
            WizardPrompt = isManual ? BuildPrompt(title ?? string.Empty, checkContent) : null,
            Applicability = app,
            Revision = rev
        };
    }

    private static string? ExtractVulnId(string? ruleId, string title)
    {
        var text = (ruleId ?? string.Empty) + " " + title;
        var idx = text.IndexOf("V-", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        int end = idx + 2;
        while (end < text.Length && char.IsDigit(text[end])) end++;
        var candidate = text.Substring(idx, end - idx);
        return candidate.Length >= 4 ? candidate : null;
    }

    private static bool IsManualCheck(string? checkSystem, string? checkContent)
    {
        // Heuristic 1: Explicit manual marker in system attribute
        if (!string.IsNullOrEmpty(checkSystem) && 
            checkSystem!.IndexOf("manual", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        
        // Heuristic 2: SCC automated check system (NOT manual)
        // SCAP Compliance Checker uses scap.nist.gov namespace
        if (!string.IsNullOrEmpty(checkSystem) && 
            checkSystem!.IndexOf("scap.nist.gov", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;  // SCC = automated, even if content has manual keywords
        
        // Heuristic 3: Keywords in check content
        var text = checkContent ?? string.Empty;
        string[] manualKeywords = { "manually", "manual", "review", "examine", "inspect", "audit" };
        
        foreach (var keyword in manualKeywords)
        {
            if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        
        return false;  // Default to automated
    }

    private static string BuildPrompt(string title, string? checkContent)
    {
        var prompt = "Manual check: " + title;
        if (!string.IsNullOrWhiteSpace(checkContent))
            prompt += "\n" + checkContent!.Trim();
        return prompt;
    }
}
