using FluentAssertions;
using STIGForge.Apply.OrgSettings;
using STIGForge.Core.Models;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class OrgSettingsParserTests
{
    // ── Fixture helpers ─────────────────────────────────────────────────────

    private static string WriteXml(TempDirectory tmp, string content, string name = "OrgSettings.xml")
    {
        var path = tmp.File(name);
        File.WriteAllText(path, content);
        return path;
    }

    // Produces an OrgSettings XML with a mix of empty and filled values
    private const string MixedOrgSettingsXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<OrganizationalSettings fullversion=""2.x.1.0"">
  <OrganizationalSetting id=""V-205648.a"" value="""" type=""RootCertificateRule"" />
  <OrganizationalSetting id=""V-205648.b"" value="""" type=""RootCertificateRule"" />
  <OrganizationalSetting id=""V-205649"" value=""AlreadyFilledThumbprint"" type=""RootCertificateRule"" />
  <OrganizationalSetting id=""V-205612"" value="""" type=""SecurityOptionRule"" />
  <OrganizationalSetting id=""V-100001"" value="""" type=""RegistryRule"" />
  <OrganizationalSetting id=""V-100002"" value="""" type=""AccountPolicy-high"" />
</OrganizationalSettings>";

    private const string AllFilledOrgSettingsXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<OrganizationalSettings fullversion=""1.0.0"">
  <OrganizationalSetting id=""V-100001"" value=""SomeValue"" type=""RegistryRule"" />
  <OrganizationalSetting id=""V-100002"" value=""AnotherValue"" type=""ServiceRule"" />
</OrganizationalSettings>";

    private const string NoIdOrgSettingsXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<OrganizationalSettings>
  <OrganizationalSetting value="""" type=""RegistryRule"" />
  <OrganizationalSetting id="""" value="""" type=""RegistryRule"" />
</OrganizationalSettings>";

    private const string EmptyOrgSettingsXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<OrganizationalSettings fullversion=""1.0.0"">
</OrganizationalSettings>";

    // ── ParseOrgSettingsXml ─────────────────────────────────────────────────

    [Fact]
    public void ParseOrgSettingsXml_ReturnsOnlyEmptyValueEntries()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, MixedOrgSettingsXml);

        var entries = OrgSettingsParser.ParseOrgSettingsXml(path);

        // V-205649 has a filled value and must be excluded
        entries.Should().NotContain(e => e.RuleId == "V-205649");
    }

    [Fact]
    public void ParseOrgSettingsXml_EmptyValues_AreReturned()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, MixedOrgSettingsXml);

        var entries = OrgSettingsParser.ParseOrgSettingsXml(path);

        entries.Should().Contain(e => e.RuleId == "V-205612");
        entries.Should().Contain(e => e.RuleId == "V-100001");
    }

    [Fact]
    public void ParseOrgSettingsXml_AllFilled_ReturnsEmpty()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, AllFilledOrgSettingsXml);

        var entries = OrgSettingsParser.ParseOrgSettingsXml(path);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseOrgSettingsXml_EmptyXml_ReturnsEmpty()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, EmptyOrgSettingsXml);

        var entries = OrgSettingsParser.ParseOrgSettingsXml(path);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseOrgSettingsXml_MissingOrEmptyId_EntriesAreSkipped()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, NoIdOrgSettingsXml);

        var entries = OrgSettingsParser.ParseOrgSettingsXml(path);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void ParseOrgSettingsXml_KnownDefaults_AreApplied()
    {
        using var tmp = new TempDirectory();
        // V-205648.a is a known default (DoD Root CA 3 thumbprint)
        var path = WriteXml(tmp, MixedOrgSettingsXml);

        var entries = OrgSettingsParser.ParseOrgSettingsXml(path);

        var knownEntry = entries.FirstOrDefault(e => e.RuleId == "V-205648.a");
        knownEntry.Should().NotBeNull();
        knownEntry!.Value.Should().NotBeNullOrEmpty("known defaults should be pre-filled");
        knownEntry.Category.Should().Be("Certificate");
    }

    [Fact]
    public void ParseOrgSettingsXml_UnknownEntry_GetsOtherCategory()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, MixedOrgSettingsXml);

        var entries = OrgSettingsParser.ParseOrgSettingsXml(path);

        var unknown = entries.FirstOrDefault(e => e.RuleId == "V-100001");
        unknown.Should().NotBeNull();
        // RegistryRule maps to "Registry" category
        unknown!.Category.Should().Be("Registry");
    }

    [Fact]
    public void ParseOrgSettingsXml_HighSeverityRule_IsMarkedRequired()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, MixedOrgSettingsXml);

        var entries = OrgSettingsParser.ParseOrgSettingsXml(path);

        // V-100002 has type "AccountPolicy-high"
        var highEntry = entries.FirstOrDefault(e => e.RuleId == "V-100002");
        highEntry.Should().NotBeNull();
        highEntry!.IsRequired.Should().BeTrue();
        highEntry.Severity.Should().Be("high");
    }

    // ── GenerateOrgSettingsXml ──────────────────────────────────────────────

    [Fact]
    public void GenerateOrgSettingsXml_MergesAnswersIntoTemplate()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, MixedOrgSettingsXml);

        var profile = new OrgSettingsProfile
        {
            ProfileName = "Test",
            Entries =
            [
                new OrgSettingEntry { RuleId = "V-205612", Value = "MyAnswer" }
            ]
        };

        var output = OrgSettingsParser.GenerateOrgSettingsXml(path, profile);

        output.Should().Contain("MyAnswer");
    }

    [Fact]
    public void GenerateOrgSettingsXml_DoesNotOverwriteExistingValues_WhenNoAnswer()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, AllFilledOrgSettingsXml);

        var profile = new OrgSettingsProfile { ProfileName = "Empty", Entries = [] };

        var output = OrgSettingsParser.GenerateOrgSettingsXml(path, profile);

        output.Should().Contain("SomeValue");
        output.Should().Contain("AnotherValue");
    }

    [Fact]
    public void GenerateOrgSettingsXml_ReturnsValidXml()
    {
        using var tmp = new TempDirectory();
        var path = WriteXml(tmp, MixedOrgSettingsXml);

        var profile = new OrgSettingsProfile { ProfileName = "X", Entries = [] };
        var output = OrgSettingsParser.GenerateOrgSettingsXml(path, profile);

        var act = () => System.Xml.Linq.XDocument.Parse(output);
        act.Should().NotThrow();
    }

    // ── WriteOrgSettingsXml ─────────────────────────────────────────────────

    [Fact]
    public void WriteOrgSettingsXml_WritesFileToDisk()
    {
        using var tmp = new TempDirectory();
        var templatePath = WriteXml(tmp, MixedOrgSettingsXml);
        var outputDir = tmp.File("output");

        var profile = new OrgSettingsProfile { ProfileName = "Test", Entries = [] };
        var outPath = OrgSettingsParser.WriteOrgSettingsXml(templatePath, profile, outputDir);

        File.Exists(outPath).Should().BeTrue();
        outPath.Should().EndWith("OrgSettings.xml");
    }

    [Fact]
    public void WriteOrgSettingsXml_Overwrites_ExistingFile()
    {
        using var tmp = new TempDirectory();
        var templatePath = WriteXml(tmp, MixedOrgSettingsXml);
        var outputDir = tmp.File("out2");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "OrgSettings.xml"), "old content");

        var profile = new OrgSettingsProfile
        {
            ProfileName = "Test",
            Entries = [new OrgSettingEntry { RuleId = "V-205612", Value = "NewVal" }]
        };

        OrgSettingsParser.WriteOrgSettingsXml(templatePath, profile, outputDir);

        var content = File.ReadAllText(Path.Combine(outputDir, "OrgSettings.xml"));
        content.Should().NotBe("old content");
        content.Should().Contain("NewVal");
    }

    // ── DiscoverEmptySettings ───────────────────────────────────────────────

    [Fact]
    public void DiscoverEmptySettings_NonexistentPath_ReturnsEmpty()
    {
        var entries = OrgSettingsParser.DiscoverEmptySettings("/nonexistent/path", OsTarget.Server2022);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverEmptySettings_UnknownOsTarget_ReturnsEmpty()
    {
        using var tmp = new TempDirectory();

        var entries = OrgSettingsParser.DiscoverEmptySettings(tmp.Path, OsTarget.Unknown);

        entries.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverEmptySettings_ValidLayout_FindsOrgSettingsXmlFiles()
    {
        using var tmp = new TempDirectory();
        // Mimic PowerSTIG layout: modulePath/1.0.0/StigData/Processed/
        var processedDir = Directory.CreateDirectory(
            Path.Combine(tmp.Path, "1.0.0", "StigData", "Processed"));
        File.WriteAllText(
            Path.Combine(processedDir.FullName, "WindowsServer-2022-MS.OrganizationalSettings.xml"),
            MixedOrgSettingsXml);

        var entries = OrgSettingsParser.DiscoverEmptySettings(tmp.Path, OsTarget.Server2022);

        entries.Should().NotBeEmpty();
    }
}
