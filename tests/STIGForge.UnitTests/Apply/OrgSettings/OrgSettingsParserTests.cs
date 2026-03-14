using System.Text;
using FluentAssertions;
using STIGForge.Apply.OrgSettings;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply.OrgSettings;

public sealed class OrgSettingsParserTests : IDisposable
{
    private readonly string _tempDir;

    public OrgSettingsParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "orgsettings-parser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── ParseOrgSettingsXml ──────────────────────────────────────────────────

    [Fact]
    public void ParseOrgSettingsXml_EmptyValueEntries_AreReturned()
    {
        var xmlPath = WriteXml(_tempDir, "test.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-001"" value="""" type=""RegistryRule""/>
  <OrganizationalSetting id=""SV-002"" value=""already-set"" type=""RegistryRule""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries.Should().ContainSingle(e => e.RuleId == "SV-001");
        entries.Should().NotContain(e => e.RuleId == "SV-002");
    }

    [Fact]
    public void ParseOrgSettingsXml_AllFilled_ReturnsEmpty()
    {
        var xmlPath = WriteXml(_tempDir, "filled.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-010"" value=""some-value"" type=""RegistryRule""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseOrgSettingsXml_MissingIdAttribute_EntrySkipped()
    {
        var xmlPath = WriteXml(_tempDir, "no-id.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting value="""" type=""RegistryRule""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseOrgSettingsXml_ServiceRuleType_CategoryIsService()
    {
        var xmlPath = WriteXml(_tempDir, "service.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-100"" value="""" type=""ServiceRule""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries.Should().ContainSingle();
        entries[0].Category.Should().Be("Service");
    }

    [Fact]
    public void ParseOrgSettingsXml_AuditPolicyType_CategoryIsAuditPolicy()
    {
        var xmlPath = WriteXml(_tempDir, "audit.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-200"" value="""" type=""AuditPolicy""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries.Should().ContainSingle();
        entries[0].Category.Should().Be("Audit Policy");
    }

    [Fact]
    public void ParseOrgSettingsXml_UnknownType_CategoryIsOther()
    {
        var xmlPath = WriteXml(_tempDir, "unknown.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-300"" value="""" type=""SomeFutureType""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries.Should().ContainSingle();
        entries[0].Category.Should().Be("Other");
    }

    [Fact]
    public void ParseOrgSettingsXml_HighRuleType_SeverityIsHigh()
    {
        var xmlPath = WriteXml(_tempDir, "high.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-400"" value="""" type=""HighRegistryRule""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries.Should().ContainSingle();
        entries[0].Severity.Should().Be("high");
    }

    [Fact]
    public void ParseOrgSettingsXml_LowRuleType_SeverityIsLow()
    {
        var xmlPath = WriteXml(_tempDir, "low.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-401"" value="""" type=""LowRegistryRule""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries.Should().ContainSingle();
        entries[0].Severity.Should().Be("low");
    }

    [Fact]
    public void ParseOrgSettingsXml_MediumType_SeverityIsMedium()
    {
        var xmlPath = WriteXml(_tempDir, "medium.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-402"" value="""" type=""RegistryRule""/>
</OrganizationalSettings>");

        var entries = OrgSettingsParser.ParseOrgSettingsXml(xmlPath);

        entries[0].Severity.Should().Be("medium");
    }

    // ── GenerateOrgSettingsXml ───────────────────────────────────────────────

    [Fact]
    public void GenerateOrgSettingsXml_FillsEmptyValues()
    {
        var xmlPath = WriteXml(_tempDir, "template.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-500"" value="""" type=""RegistryRule""/>
</OrganizationalSettings>");

        var profile = new OrgSettingsProfile
        {
            Entries =
            [
                new OrgSettingEntry { RuleId = "SV-500", Value = "my-answer" }
            ]
        };

        var xml = OrgSettingsParser.GenerateOrgSettingsXml(xmlPath, profile);

        xml.Should().Contain("my-answer");
    }

    [Fact]
    public void GenerateOrgSettingsXml_DoesNotOverwriteExistingValue_WhenNotInProfile()
    {
        var xmlPath = WriteXml(_tempDir, "keep.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-600"" value=""existing"" type=""RegistryRule""/>
</OrganizationalSettings>");

        var profile = new OrgSettingsProfile { Entries = [] };

        var xml = OrgSettingsParser.GenerateOrgSettingsXml(xmlPath, profile);

        xml.Should().Contain("existing");
    }

    // ── WriteOrgSettingsXml ──────────────────────────────────────────────────

    [Fact]
    public void WriteOrgSettingsXml_CreatesFileOnDisk()
    {
        var xmlPath = WriteXml(_tempDir, "write-template.xml", @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-700"" value="""" type=""RegistryRule""/>
</OrganizationalSettings>");

        var outDir = Path.Combine(_tempDir, "output");
        var profile = new OrgSettingsProfile
        {
            Entries = [new OrgSettingEntry { RuleId = "SV-700", Value = "written" }]
        };

        var outputPath = OrgSettingsParser.WriteOrgSettingsXml(xmlPath, profile, outDir);

        File.Exists(outputPath).Should().BeTrue();
        File.ReadAllText(outputPath).Should().Contain("written");
    }

    // ── DiscoverEmptySettings ────────────────────────────────────────────────

    [Fact]
    public void DiscoverEmptySettings_NonExistentPath_ReturnsEmpty()
    {
        var results = OrgSettingsParser.DiscoverEmptySettings(
            Path.Combine(_tempDir, "doesNotExist"),
            OsTarget.Server2022);

        results.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverEmptySettings_NoMatchingXmlFiles_ReturnsEmpty()
    {
        // Module path with StigData/Processed but no matching XML
        var versionDir = Path.Combine(_tempDir, "4.22.0");
        var processedDir = Path.Combine(versionDir, "StigData", "Processed");
        Directory.CreateDirectory(processedDir);
        File.WriteAllText(Path.Combine(processedDir, "unrelated.xml"), "<root/>");

        var results = OrgSettingsParser.DiscoverEmptySettings(_tempDir, OsTarget.Server2022);

        results.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverEmptySettings_MatchingOrgSettingsXml_ReturnsEntries()
    {
        var versionDir = Path.Combine(_tempDir, "4.22.0");
        var processedDir = Path.Combine(versionDir, "StigData", "Processed");
        Directory.CreateDirectory(processedDir);

        // File matching WindowsServer-2022 pattern and OrganizationalSettings
        var xmlName = "WindowsServer-2022-OrganizationalSettings-1.xml";
        WriteXml(processedDir, xmlName, @"<?xml version=""1.0""?>
<OrganizationalSettings>
  <OrganizationalSetting id=""SV-800"" value="""" type=""RegistryRule""/>
</OrganizationalSettings>");

        var results = OrgSettingsParser.DiscoverEmptySettings(_tempDir, OsTarget.Server2022);

        results.Should().Contain(e => e.RuleId == "SV-800");
    }

    [Fact]
    public void DiscoverEmptySettings_UnsupportedOsTarget_ReturnsEmpty()
    {
        var versionDir = Path.Combine(_tempDir, "4.22.0");
        var processedDir = Path.Combine(versionDir, "StigData", "Processed");
        Directory.CreateDirectory(processedDir);

        var results = OrgSettingsParser.DiscoverEmptySettings(_tempDir, (OsTarget)999);

        results.Should().BeEmpty();
    }

    private static string WriteXml(string dir, string name, string content)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }
}
