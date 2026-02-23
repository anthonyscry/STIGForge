using System.Text;
using System.Text.Json;
using STIGForge.Core.Models;

namespace STIGForge.Benchmarks.TestData;

/// <summary>
/// Generates synthetic STIG test bundles with configurable rule counts for benchmarking.
/// Creates realistic XCCDF content and bundle structures without requiring actual STIG data.
/// </summary>
public static class GenerateTestBundle
{
    private static readonly string[] SeverityLevels = { "high", "medium", "low" };
    private static readonly string[] RulePrefixes = { "SV-", "V-", "WN" };
    private static readonly string[] DiscussionTemplates = {
        "This setting controls the {0} behavior of the system.",
        "Configuring {0} is critical for security compliance.",
        "The {0} setting must be properly configured to meet security requirements.",
        "Failure to configure {0} may result in security vulnerabilities."
    };
    private static readonly string[] CheckTemplates = {
        "Verify the {0} setting is configured correctly by examining the registry.",
        "Check that {0} is set to the required value in the configuration file.",
        "Open Administrative Tools and verify {0} is configured as required.",
        "Run the following command to verify {0}: Get-ItemProperty -Path HKLM:\\System\\CurrentControlSet\\Services\\{0}"
    };
    private static readonly string[] FixTemplates = {
        "Configure the {0} setting to the required value.",
        "Set {0} to the recommended configuration.",
        "Navigate to Administrative Tools and configure {0} appropriately.",
        "Run the following command: Set-ItemProperty -Path HKLM:\\System\\CurrentControlSet\\Services\\{0} -Name Value -Data 1"
    };

    /// <summary>
    /// Generates XCCDF XML content with the specified number of rules.
    /// </summary>
    /// <param name="ruleCount">Number of rules to generate.</param>
    /// <returns>XCCDF XML content string.</returns>
    public static string GenerateXccdfContent(int ruleCount)
    {
        var sb = new StringBuilder(ruleCount * 500);

        sb.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8""?>");
        sb.AppendLine(@"<Benchmark xmlns=""http://checklists.nist.gov/xccdf/1.2"" id=""benchmark-synthetic-test"">");
        sb.AppendLine(@"  <title>Synthetic STIG Benchmark for Performance Testing</title>");
        sb.AppendLine(@"  <description>This is a synthetic benchmark generated for performance baseline measurements.</description>");
        sb.AppendLine(@"  <version>1.0</version>");

        for (int i = 1; i <= ruleCount; i++)
        {
            var severity = SeverityLevels[i % SeverityLevels.Length];
            var ruleId = $"SV-{100000 + i}r1_rule";
            var vulnId = $"V-{100000 + i}";
            var srgId = $"SRG-OS-{10000 + i}-GPO";
            var title = GenerateTitle(i);
            var discussion = string.Format(DiscussionTemplates[i % DiscussionTemplates.Length], title);
            var checkText = string.Format(CheckTemplates[i % CheckTemplates.Length], title);
            var fixText = string.Format(FixTemplates[i % FixTemplates.Length], title);

            sb.AppendLine($@"  <Rule id=""{ruleId}"" severity=""{severity}"">");
            sb.AppendLine($@"    <title>{EscapeXml(title)}</title>");
            sb.AppendLine($@"    <description>{EscapeXml(discussion)}</description>");
            sb.AppendLine($@"    <ident system=""http://iase.disa.mil/stigs/srgpages"">{srgId}</ident>");
            sb.AppendLine($@"    <check system=""manual"">");
            sb.AppendLine($@"      <check-content>{EscapeXml(checkText)}</check-content>");
            sb.AppendLine($@"    </check>");
            sb.AppendLine($@"    <fixtext>{EscapeXml(fixText)}</fixtext>");
            sb.AppendLine($@"  </Rule>");
        }

        sb.AppendLine(@"</Benchmark>");

        return sb.ToString();
    }

    /// <summary>
    /// Creates a complete test bundle directory structure with the specified number of controls.
    /// </summary>
    /// <param name="ruleCount">Number of control records to generate.</param>
    /// <param name="outputDir">Output directory path for the bundle.</param>
    /// <returns>Path to the created bundle directory.</returns>
    public static string CreateTestBundle(int ruleCount, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var controls = GenerateControlRecords(ruleCount);

        // Write controls JSON
        var controlsPath = Path.Combine(outputDir, "controls.json");
        var controlsJson = JsonSerializer.Serialize(controls, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(controlsPath, controlsJson, Encoding.UTF8);

        // Write manifest
        var manifest = new TestBundleManifest
        {
            BundleId = Guid.NewGuid().ToString("n"),
            RuleCount = ruleCount,
            GeneratedAt = DateTimeOffset.UtcNow,
            Description = $"Synthetic test bundle with {ruleCount} rules for performance benchmarking"
        };

        var manifestPath = Path.Combine(outputDir, "manifest.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);

        // Write XCCDF content
        var xccdfPath = Path.Combine(outputDir, "benchmark.xccdf.xml");
        File.WriteAllText(xccdfPath, GenerateXccdfContent(ruleCount), Encoding.UTF8);

        // Create minimal bundle structure
        Directory.CreateDirectory(Path.Combine(outputDir, "Apply"));
        Directory.CreateDirectory(Path.Combine(outputDir, "Verify"));
        Directory.CreateDirectory(Path.Combine(outputDir, "Manual"));
        Directory.CreateDirectory(Path.Combine(outputDir, "Evidence"));
        Directory.CreateDirectory(Path.Combine(outputDir, "Manifest"));

        return outputDir;
    }

    /// <summary>
    /// Generates ControlRecord objects with realistic content.
    /// </summary>
    /// <param name="count">Number of records to generate.</param>
    /// <returns>List of ControlRecord objects.</returns>
    public static IReadOnlyList<ControlRecord> GenerateControlRecords(int count)
    {
        var controls = new List<ControlRecord>(count);

        for (int i = 1; i <= count; i++)
        {
            var title = GenerateTitle(i);
            var control = new ControlRecord
            {
                ControlId = Guid.NewGuid().ToString("n"),
                SourcePackId = "synthetic-test-pack",
                Title = title,
                Severity = SeverityLevels[i % SeverityLevels.Length],
                Discussion = string.Format(DiscussionTemplates[i % DiscussionTemplates.Length], title),
                CheckText = string.Format(CheckTemplates[i % CheckTemplates.Length], title),
                FixText = string.Format(FixTemplates[i % FixTemplates.Length], title),
                IsManual = i % 10 == 0, // 10% manual controls
                ExternalIds = new ExternalIds
                {
                    VulnId = $"V-{100000 + i}",
                    RuleId = $"SV-{100000 + i}r1_rule",
                    SrgId = $"SRG-OS-{10000 + i}-GPO",
                    BenchmarkId = "benchmark-synthetic-test"
                },
                Applicability = new Applicability
                {
                    OsTarget = (OsTarget)(i % 5),
                    ClassificationScope = (ScopeTag)(i % 8),
                    Confidence = (Confidence)(i % 3),
                    RoleTags = Array.Empty<RoleTemplate>()
                },
                Revision = new RevisionInfo
                {
                    PackName = "Synthetic Test Pack",
                    BenchmarkVersion = "1.0",
                    BenchmarkRelease = "R1",
                    BenchmarkDate = DateTimeOffset.UtcNow
                }
            };

            controls.Add(control);
        }

        return controls;
    }

    /// <summary>
    /// Loads XCCDF rules into memory for scale testing.
    /// </summary>
    /// <param name="ruleCount">Number of rules to generate and parse.</param>
    /// <returns>List of parsed rule information.</returns>
    public static IReadOnlyList<ParsedRule> LoadRulesIntoMemory(int ruleCount)
    {
        var rules = new List<ParsedRule>(ruleCount);
        var xccdfContent = GenerateXccdfContent(ruleCount);

        // Simple in-memory parsing simulation (actual XML parsing would be more expensive)
        using var reader = new StringReader(xccdfContent);
        string? line;
        int ruleIndex = 0;

        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains("<Rule id="))
            {
                var ruleIdStart = line.IndexOf("id=\"", StringComparison.Ordinal) + 4;
                var ruleIdEnd = line.IndexOf("\"", ruleIdStart, StringComparison.Ordinal);
                var ruleId = line.Substring(ruleIdStart, ruleIdEnd - ruleIdStart);

                rules.Add(new ParsedRule
                {
                    RuleId = ruleId,
                    Index = ruleIndex++,
                    ParsedAt = DateTimeOffset.UtcNow.Ticks
                });
            }
        }

        return rules;
    }

    private static string GenerateTitle(int index)
    {
        var categories = new[] { "Registry", "Service", "Policy", "Firewall", "Audit", "Encryption", "Authentication", "Logging", "Network", "Account" };
        var actions = new[] { "must be configured", "must be enabled", "must be disabled", "settings must be reviewed", "must be set to required value" };

        var category = categories[index % categories.Length];
        var action = actions[index % actions.Length];

        return $"{category} configuration {action}";
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}

/// <summary>
/// Manifest for test bundles.
/// </summary>
public sealed class TestBundleManifest
{
    public string BundleId { get; set; } = string.Empty;
    public int RuleCount { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Represents a parsed XCCDF rule for scale testing.
/// </summary>
public sealed class ParsedRule
{
    public string RuleId { get; set; } = string.Empty;
    public int Index { get; set; }
    public long ParsedAt { get; set; }
}
