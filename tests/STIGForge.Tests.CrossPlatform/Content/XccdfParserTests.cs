using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class XccdfParserTests
{
    // ── Fixture builders ────────────────────────────────────────────────────

    private static string MinimalBenchmark(
        string benchmarkId = "Test_Benchmark",
        string version = "1",
        string date = "2024-01-01",
        string platformIdref = "",
        string rearMatter = "",
        string rules = "") => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Benchmark xmlns=""http://checklists.nist.gov/xccdf/1.2"" id=""{benchmarkId}"">
  <status date=""{date}"">accepted</status>
  <version>{version}</version>
  {(string.IsNullOrEmpty(platformIdref) ? "" : $@"<platform idref=""{platformIdref}""/>")}
  {(string.IsNullOrEmpty(rearMatter) ? "" : $"<rear-matter>{rearMatter}</rear-matter>")}
  {rules}
</Benchmark>";

    private static string OneRule(
        string ruleId = "SV-1r1_rule",
        string severity = "medium",
        string title = "Test Rule Title V-100001",
        string description = "This is a description.",
        string fixText = "Apply the fix.",
        string checkSystem = "http://oval.mitre.org/XMLSchema/oval-definitions-5",
        string checkContent = "Verify the setting.") => $@"
<Rule xmlns=""http://checklists.nist.gov/xccdf/1.2"" id=""{ruleId}"" severity=""{severity}"">
  <title>{title}</title>
  <description>{description}</description>
  <fixtext>{fixText}</fixtext>
  <check system=""{checkSystem}"">
    <check-content>{checkContent}</check-content>
  </check>
</Rule>";

    private static string WriteXml(TempDirectory tmp, string xml, string name = "benchmark.xml")
    {
        var path = tmp.File(name);
        File.WriteAllText(path, xml);
        return path;
    }

    // ── Happy-path parsing ──────────────────────────────────────────────────

    [Fact]
    public void Parse_SingleRule_ReturnsSingleControl()
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "TestPack");

        controls.Should().HaveCount(1);
        controls[0].Title.Should().Contain("Test Rule Title");
    }

    [Fact]
    public void Parse_PopulatesBenchmarkIdFromAttribute()
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(benchmarkId: "MyBenchmark_V1", rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].ExternalIds.BenchmarkId.Should().Be("MyBenchmark_V1");
    }

    [Fact]
    public void Parse_ExtractsVulnIdFromRuleId()
    {
        using var tmp = new TempDirectory();
        var rule = OneRule(ruleId: "V-220012r1_rule", title: "Some Title");
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].ExternalIds.VulnId.Should().Be("V-220012");
    }

    [Fact]
    public void Parse_ExtractsVulnIdFromTitle_WhenNotInRuleId()
    {
        using var tmp = new TempDirectory();
        // ruleId has no "V-" substring at all; VulnId is read from the title
        var rule = OneRule(ruleId: "Policy001_rule", title: "Policy V-100042 must be set.");
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].ExternalIds.VulnId.Should().Be("V-100042");
    }

    [Fact]
    public void Parse_StoresPackName()
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "MyPack");

        controls[0].Revision.PackName.Should().Be("MyPack");
    }

    [Fact]
    public void Parse_SetsVersionAndDate()
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(version: "2", date: "2023-06-15", rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Revision.BenchmarkVersion.Should().Be("2");
        controls[0].Revision.BenchmarkDate.Should().NotBeNull();
        controls[0].Revision.BenchmarkDate!.Value.Year.Should().Be(2023);
    }

    [Fact]
    public void Parse_MultipleRules_ReturnsAll()
    {
        using var tmp = new TempDirectory();
        var rules = OneRule(ruleId: "SV-1r1_rule", title: "Title A") +
                    OneRule(ruleId: "SV-2r1_rule", title: "Title B") +
                    OneRule(ruleId: "SV-3r1_rule", title: "Title C");
        var xml = MinimalBenchmark(rules: rules);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls.Should().HaveCount(3);
    }

    // ── OS Target detection ─────────────────────────────────────────────────

    [Theory]
    [InlineData("cpe:/o:microsoft:windows_11", OsTarget.Win11)]
    [InlineData("cpe:/o:microsoft:windows_10", OsTarget.Win10)]
    [InlineData("cpe:/o:microsoft:windows_server_2019", OsTarget.Server2019)]
    [InlineData("cpe:/o:microsoft:windows_server_2022", OsTarget.Server2022)]
    [InlineData("cpe:/o:redhat:linux", OsTarget.Unknown)]
    public void Parse_MapsOsTargetFromPlatformCpe(string cpe, OsTarget expected)
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(platformIdref: cpe, rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Applicability.OsTarget.Should().Be(expected);
    }

    [Fact]
    public void Parse_OsTarget_FallsBackToUnknown_WhenNoPlatform()
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(platformIdref: "", rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Applicability.OsTarget.Should().Be(OsTarget.Unknown);
    }

    // ── Classification scope ────────────────────────────────────────────────

    [Theory]
    [InlineData("classification:--:UNCLASSIFIED", ScopeTag.UnclassifiedOnly)]
    [InlineData("classification:--:CLASSIFIED", ScopeTag.ClassifiedOnly)]
    [InlineData("classification:--:MIXED", ScopeTag.Both)]
    [InlineData("classification:--:BOTH", ScopeTag.Both)]
    [InlineData("classification:--:SOMETHING_ELSE", ScopeTag.Unknown)]
    public void Parse_MapsClassificationScope_FromRearMatter(string rearMatter, ScopeTag expected)
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(rearMatter: rearMatter, rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Applicability.ClassificationScope.Should().Be(expected);
    }

    // ── Confidence levels ───────────────────────────────────────────────────

    [Fact]
    public void Parse_HighConfidence_WhenVersionAndDatePresent()
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(version: "3", date: "2024-01-01", rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Applicability.Confidence.Should().Be(Confidence.High);
    }

    [Fact]
    public void Parse_MediumConfidence_WhenOnlyVersionPresent()
    {
        using var tmp = new TempDirectory();
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Benchmark xmlns=""http://checklists.nist.gov/xccdf/1.2"" id=""Bench"">
  <status>accepted</status>
  <version>2</version>
  {OneRule()}
</Benchmark>";
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Applicability.Confidence.Should().Be(Confidence.Medium);
    }

    [Fact]
    public void Parse_LowConfidence_WhenNoVersionOrDate()
    {
        using var tmp = new TempDirectory();
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Benchmark xmlns=""http://checklists.nist.gov/xccdf/1.2"" id=""Bench"">
  {OneRule()}
</Benchmark>";
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Applicability.Confidence.Should().Be(Confidence.Low);
    }

    // ── Manual check detection ──────────────────────────────────────────────

    [Fact]
    public void Parse_IsManual_True_WhenCheckSystemContainsManual()
    {
        using var tmp = new TempDirectory();
        var rule = OneRule(checkSystem: "http://example.com/manual", checkContent: "Do something.");
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].IsManual.Should().BeTrue();
        controls[0].WizardPrompt.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Parse_IsManual_False_WhenSccCheckSystem()
    {
        using var tmp = new TempDirectory();
        var rule = OneRule(checkSystem: "http://scap.nist.gov/schema/oval", checkContent: "manually review this.");
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        // SCC automated overrides keyword heuristic
        controls[0].IsManual.Should().BeFalse();
    }

    [Theory]
    [InlineData("manually verify the setting")]
    [InlineData("review the registry value")]
    [InlineData("inspect the output")]
    [InlineData("audit the configuration")]
    [InlineData("examine the file")]
    public void Parse_IsManual_True_WhenCheckContentContainsManualKeyword(string content)
    {
        using var tmp = new TempDirectory();
        var rule = OneRule(checkSystem: "http://oval.mitre.org/oval", checkContent: content);
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].IsManual.Should().BeTrue();
    }

    [Fact]
    public void Parse_IsManual_False_WhenNoManualKeywords()
    {
        using var tmp = new TempDirectory();
        var rule = OneRule(checkSystem: "http://oval.mitre.org/oval", checkContent: "Run the script.");
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].IsManual.Should().BeFalse();
        controls[0].WizardPrompt.Should().BeNull();
    }

    // ── Rear-matter extraction ──────────────────────────────────────────────

    [Fact]
    public void Parse_ExtractsReleaseInfoFromRearMatter()
    {
        using var tmp = new TempDirectory();
        var rear = "releaseinfo:--:Release 5\nclassification:--:UNCLASSIFIED";
        var xml = MinimalBenchmark(rearMatter: rear, rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Revision.BenchmarkRelease.Should().Be("Release 5");
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyBenchmark_ReturnsEmptyList()
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark();
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RuleWithNoTitle_UsesRuleIdAsFallback()
    {
        using var tmp = new TempDirectory();
        var rule = @"<Rule xmlns=""http://checklists.nist.gov/xccdf/1.2"" id=""SV-99r1_rule"" severity=""low"">
  <title></title>
</Rule>";
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].Title.Should().Be("SV-99r1_rule");
    }

    [Fact]
    public void Parse_MalformedXml_ThrowsException()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, "<notclosed");

        var act = () => XccdfParser.Parse(path, "Pack");

        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Parse_RuleWithNoCheckElement_HasNullCheckText()
    {
        using var tmp = new TempDirectory();
        var rule = @"<Rule xmlns=""http://checklists.nist.gov/xccdf/1.2"" id=""SV-100r1_rule"" severity=""high"">
  <title>No Check Rule</title>
  <fixtext>Apply fix.</fixtext>
</Rule>";
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].CheckText.Should().BeNull();
        controls[0].IsManual.Should().BeFalse();
    }

    [Fact]
    public void Parse_PopulatesRuleIdAndSeverity()
    {
        using var tmp = new TempDirectory();
        var rule = OneRule(ruleId: "SV-220001r1_rule", severity: "high");
        var xml = MinimalBenchmark(rules: rule);
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        controls[0].ExternalIds.RuleId.Should().Be("SV-220001r1_rule");
        controls[0].Severity.Should().Be("high");
    }

    [Fact]
    public void Parse_ControlId_IsNonEmptyGuid()
    {
        using var tmp = new TempDirectory();
        var xml = MinimalBenchmark(rules: OneRule());
        var path = WriteXml(tmp, xml);

        var controls = XccdfParser.Parse(path, "Pack");

        Guid.TryParse(controls[0].ControlId, out _).Should().BeTrue();
    }
}
