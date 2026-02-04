using System.Xml;
using STIGForge.Core.Models;
using STIGForge.Content.Extensions;

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

        var results = new List<ControlRecord>();
        string? benchmarkId = null;

        using var reader = XmlReader.Create(xmlPath, settings);
        
        while (reader.Read())
        {
            // Skip non-element nodes
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            // Extract Benchmark ID
            if (reader.LocalName == "Benchmark" && reader.NamespaceURI == XccdfNamespace)
            {
                benchmarkId = reader.GetAttribute("id") ?? Path.GetFileNameWithoutExtension(xmlPath);
                continue;
            }

            // Parse Rule elements
            if (reader.LocalName == "Rule" && reader.NamespaceURI == XccdfNamespace)
            {
                var control = ParseRule(reader, benchmarkId ?? Path.GetFileNameWithoutExtension(xmlPath), packName);
                if (control != null)
                {
                    results.Add(control);
                }
            }
        }

        return results;
    }

    private static ControlRecord? ParseRule(XmlReader reader, string benchmarkId, string packName)
    {
        if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Rule")
            return null;

        // Extract Rule attributes
        var ruleId = reader.GetAttribute("id");
        var severity = reader.GetAttribute("severity") ?? "unknown";

        // Initialize fields
        string? title = ruleId ?? "Untitled";
        string? description = null;
        string? checkContent = null;
        string? checkSystem = null;
        string? fixText = null;

        var ruleDepth = reader.Depth;

        // Parse Rule children
        while (reader.Read())
        {
            // Stop when we exit the Rule element
            if (reader.NodeType == XmlNodeType.EndElement && 
                reader.LocalName == "Rule" && 
                reader.Depth == ruleDepth)
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element)
                continue;

            // Only process elements in XCCDF namespace
            if (reader.NamespaceURI != XccdfNamespace)
                continue;

            switch (reader.LocalName)
            {
                case "title":
                    var titleText = reader.ReadElementContent();
                    if (!string.IsNullOrWhiteSpace(titleText))
                        title = titleText;
                    break;

                case "description":
                    description = reader.ReadElementContent();
                    break;

                case "fixtext":
                    fixText = reader.ReadElementContent();
                    break;

                case "check":
                    checkContent = reader.ReadCheckContent(out checkSystem);
                    break;
            }
        }

        // Determine if manual check
        var isManual = IsManualCheck(checkSystem, checkContent);

        // Build external IDs
        var external = new ExternalIds
        {
            VulnId = ExtractVulnId(ruleId, title),
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
            Title = title,
            Severity = severity,
            Discussion = description,
            CheckText = checkContent,
            FixText = fixText,
            IsManual = isManual,
            WizardPrompt = isManual ? BuildPrompt(title, checkContent) : null,
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
            checkSystem.IndexOf("manual", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        
        // Heuristic 2: SCC automated check system (NOT manual)
        // SCAP Compliance Checker uses scap.nist.gov namespace
        if (!string.IsNullOrEmpty(checkSystem) && 
            checkSystem.IndexOf("scap.nist.gov", StringComparison.OrdinalIgnoreCase) >= 0)
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
