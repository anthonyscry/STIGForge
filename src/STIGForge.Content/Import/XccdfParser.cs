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
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true,
            Async = false,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = 40_000_000
        };

        using var reader = XmlReader.Create(xmlPath, settings);

        var benchmarkId = Path.GetFileNameWithoutExtension(xmlPath);
        string? benchmarkVersion = null;
        DateTimeOffset? benchmarkDate = null;
        string? rearMatter = null;
        string? platformCpe = null;

        var results = new List<ControlRecord>();

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (string.Equals(reader.LocalName, "Benchmark", StringComparison.OrdinalIgnoreCase))
            {
                var candidateId = reader.GetAttribute("id");
                if (!string.IsNullOrWhiteSpace(candidateId))
                    benchmarkId = candidateId;
                continue;
            }

            if (string.Equals(reader.LocalName, "version", StringComparison.OrdinalIgnoreCase))
            {
                var value = reader.ReadElementContentAsString().Trim();
                benchmarkVersion = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (string.Equals(reader.LocalName, "status", StringComparison.OrdinalIgnoreCase))
            {
                var dateText = reader.GetAttribute("date");
                if (!string.IsNullOrWhiteSpace(dateText) && DateTimeOffset.TryParse(dateText, out var parsedBenchmarkDate))
                    benchmarkDate = parsedBenchmarkDate;
                continue;
            }

            if (string.Equals(reader.LocalName, "platform", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(platformCpe))
            {
                var candidate = reader.GetAttribute("idref")?.Trim();
                if (!string.IsNullOrWhiteSpace(candidate))
                    platformCpe = candidate;
                continue;
            }

            if (string.Equals(reader.LocalName, "rear-matter", StringComparison.OrdinalIgnoreCase))
            {
                var value = reader.ReadElementContentAsString();
                rearMatter = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (!string.Equals(reader.LocalName, "Rule", StringComparison.OrdinalIgnoreCase))
                continue;

            using var ruleReader = reader.ReadSubtree();
            var rule = XElement.Load(ruleReader, LoadOptions.None);
            var ns = rule.Name.Namespace;
            var control = ParseRule(
                rule,
                benchmarkId,
                packName,
                ns,
                benchmarkVersion,
                null,
                benchmarkDate,
                OsTarget.Unknown,
                ScopeTag.Unknown,
                Confidence.Low);
            if (control != null)
                results.Add(control);
        }

        var benchmarkRelease = ExtractRearMatterField(rearMatter, "releaseinfo");
        var classificationText = ExtractRearMatterField(rearMatter, "classification");
        var osTarget = MapOsTarget(platformCpe);
        var classificationScope = MapClassificationScope(classificationText);
        var confidence = GetConfidence(benchmarkVersion, benchmarkDate);

        foreach (var control in results)
        {
            control.ExternalIds.BenchmarkId = benchmarkId;
            control.Applicability.OsTarget = osTarget;
            control.Applicability.ClassificationScope = classificationScope;
            control.Applicability.Confidence = confidence;
            control.Revision.BenchmarkVersion = benchmarkVersion;
            control.Revision.BenchmarkRelease = benchmarkRelease;
            control.Revision.BenchmarkDate = benchmarkDate;
        }

        return results;
    }

    private static ControlRecord? ParseRule(
        XElement rule,
        string benchmarkId,
        string packName,
        XNamespace ns,
        string? benchmarkVersion,
        string? benchmarkRelease,
        DateTimeOffset? benchmarkDate,
        OsTarget osTarget,
        ScopeTag classificationScope,
        Confidence confidence)
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

        var app = new Applicability
        {
            OsTarget = osTarget,
            // XCCDF 1.2 has no standard role element - parse custom metadata in v1.2
            RoleTags = Array.Empty<RoleTemplate>(),
            ClassificationScope = classificationScope,
            Confidence = confidence
        };

        var rev = new RevisionInfo
        {
            PackName = packName,
            BenchmarkVersion = benchmarkVersion,
            BenchmarkRelease = benchmarkRelease,
            BenchmarkDate = benchmarkDate
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

    private static string? ExtractRearMatterField(string? rearMatterText, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(rearMatterText)) return null;

        var lines = rearMatterText!
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim());

        foreach (var line in lines)
        {
            var separatorIndex = line.IndexOf(":--:", StringComparison.Ordinal);
            if (separatorIndex <= 0) continue;

            var key = line.Substring(0, separatorIndex).Trim();
            if (!key.Equals(fieldName, StringComparison.OrdinalIgnoreCase)) continue;

            var value = line.Substring(separatorIndex + 4).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static OsTarget MapOsTarget(string? platformCpe)
    {
        if (string.IsNullOrWhiteSpace(platformCpe)) return OsTarget.Unknown;

        if (platformCpe!.IndexOf("windows_11", StringComparison.OrdinalIgnoreCase) >= 0 ||
            platformCpe.IndexOf("win11", StringComparison.OrdinalIgnoreCase) >= 0)
            return OsTarget.Win11;

        if (platformCpe.IndexOf("windows_server_2019", StringComparison.OrdinalIgnoreCase) >= 0 ||
            platformCpe.IndexOf("2019", StringComparison.OrdinalIgnoreCase) >= 0)
            return OsTarget.Server2019;

        if (platformCpe.IndexOf("windows_server_2022", StringComparison.OrdinalIgnoreCase) >= 0 ||
            platformCpe.IndexOf("2022", StringComparison.OrdinalIgnoreCase) >= 0)
            return OsTarget.Server2022;

        if (platformCpe.IndexOf("windows_10", StringComparison.OrdinalIgnoreCase) >= 0 ||
            platformCpe.IndexOf("win10", StringComparison.OrdinalIgnoreCase) >= 0)
            return OsTarget.Win10;

        return OsTarget.Unknown;
    }

    private static ScopeTag MapClassificationScope(string? classification)
    {
        if (string.IsNullOrWhiteSpace(classification)) return ScopeTag.Unknown;

        var normalized = classification.Trim();

        var separatorIndex = normalized.IndexOf(":--:", StringComparison.Ordinal);
        if (separatorIndex >= 0)
            normalized = normalized.Substring(separatorIndex + 4).Trim();

        var tokenDelimiters = new[] { ' ', '\t', '\r', '\n', '/', '\\' };
        var firstToken = normalized
            .Split(tokenDelimiters, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstToken))
            return ScopeTag.Unknown;

        if (firstToken.Equals("CLASSIFIED", StringComparison.OrdinalIgnoreCase))
            return ScopeTag.ClassifiedOnly;

        if (firstToken.Equals("UNCLASSIFIED", StringComparison.OrdinalIgnoreCase))
            return ScopeTag.UnclassifiedOnly;

        if (firstToken.Equals("MIXED", StringComparison.OrdinalIgnoreCase) ||
            firstToken.Equals("BOTH", StringComparison.OrdinalIgnoreCase))
            return ScopeTag.Both;

        return ScopeTag.Unknown;
    }

    private static Confidence GetConfidence(string? benchmarkVersion, DateTimeOffset? benchmarkDate)
    {
        var hasVersion = !string.IsNullOrWhiteSpace(benchmarkVersion);
        var hasDate = benchmarkDate != null;

        if (hasVersion && hasDate) return Confidence.High;
        if (hasVersion || hasDate) return Confidence.Medium;
        return Confidence.Low;
    }
}
