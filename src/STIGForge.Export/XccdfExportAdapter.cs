using System.Globalization;
using System.Xml.Linq;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Export adapter that generates XCCDF 1.2 XML from verify results.
/// Output is consumable by Tenable, ACAS, STIG Viewer, and OpenRMF.
/// Round-trip validated against ScapResultAdapter.
/// </summary>
public sealed class XccdfExportAdapter : IExportAdapter
{
    private static readonly XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2";

    public string FormatName => "XCCDF";
    public string[] SupportedExtensions => new[] { ".xml" };

    public async Task<ExportAdapterResult> ExportAsync(
        ExportAdapterRequest request, CancellationToken ct)
    {
        var fileName = (request.FileNameStem ?? "stigforge_xccdf_results") + ".xml";
        var outputPath = Path.Combine(request.OutputDirectory, fileName);
        var tempPath = outputPath + ".tmp";

        try
        {
            Directory.CreateDirectory(request.OutputDirectory);

            var doc = BuildXccdfDocument(request.Results);

            await Task.Run(() => doc.Save(tempPath), ct).ConfigureAwait(false);
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            File.Move(tempPath, outputPath);

            return new ExportAdapterResult
            {
                Success = true,
                OutputPaths = new[] { outputPath }
            };
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best-effort cleanup */ }
            throw;
        }
    }

    private static XDocument BuildXccdfDocument(IReadOnlyList<ControlResult> results)
    {
        var now = DateTimeOffset.UtcNow;

        DateTimeOffset startTime = now;
        DateTimeOffset endTime = now;

        if (results.Count > 0)
        {
            DateTimeOffset? earliest = null;
            DateTimeOffset? latest = null;

            foreach (var r in results)
            {
                if (r.VerifiedAt.HasValue)
                {
                    if (earliest == null || r.VerifiedAt.Value < earliest.Value)
                        earliest = r.VerifiedAt.Value;
                    if (latest == null || r.VerifiedAt.Value > latest.Value)
                        latest = r.VerifiedAt.Value;
                }
            }

            startTime = earliest ?? now;
            endTime = latest ?? now;
        }

        var testResult = new XElement(XccdfNs + "TestResult",
            new XAttribute("version", "1.0"),
            new XElement(XccdfNs + "start-time", startTime.ToString("o")),
            new XElement(XccdfNs + "end-time", endTime.ToString("o"))
        );

        foreach (var result in results)
        {
            testResult.Add(BuildRuleResult(result));
        }

        var benchmark = new XElement(XccdfNs + "Benchmark", testResult);

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            benchmark
        );
    }

    private static XElement BuildRuleResult(ControlResult result)
    {
        var ruleResult = new XElement(XccdfNs + "rule-result",
            new XAttribute("idref", result.RuleId ?? result.VulnId ?? "unknown")
        );

        if (result.VerifiedAt.HasValue)
            ruleResult.Add(new XAttribute("time", result.VerifiedAt.Value.ToString("o")));

        var weight = MapSeverityToWeight(result.Severity);
        if (weight != null)
            ruleResult.Add(new XAttribute("weight", weight));

        ruleResult.Add(new XElement(XccdfNs + "result", MapStatusToXccdf(result.Status)));

        if (!string.IsNullOrWhiteSpace(result.VulnId))
        {
            ruleResult.Add(new XElement(XccdfNs + "ident",
                new XAttribute("system", "http://cyber.mil/cci"),
                result.VulnId));
        }

        if (!string.IsNullOrWhiteSpace(result.FindingDetails))
        {
            ruleResult.Add(new XElement(XccdfNs + "message", result.FindingDetails));
        }

        return ruleResult;
    }

    private static string MapStatusToXccdf(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "unknown";

        var normalized = status!
            .Trim()
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();

        return normalized switch
        {
            "pass" => "pass",
            "notafinding" => "pass",
            "fail" => "fail",
            "open" => "fail",
            "notapplicable" => "notapplicable",
            "na" => "notapplicable",
            "notreviewed" => "notchecked",
            "notchecked" => "notchecked",
            "notselected" => "notchecked",
            "informational" => "informational",
            "error" => "error",
            "unknown" => "unknown",
            _ => "unknown"
        };
    }

    private static string? MapSeverityToWeight(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return null;

        return severity!.Trim().ToLowerInvariant() switch
        {
            "high" => "10.0",
            "medium" => "5.0",
            "low" => "1.0",
            _ => null
        };
    }
}
