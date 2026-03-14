using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class GpoParserTests
{
    // ── ADMX fixture ────────────────────────────────────────────────────────

    private const string SimpleAdmx = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<policyDefinitions revision=""1.0"" schemaVersion=""1.0"">
  <policyNamespaces>
    <target prefix=""test"" namespace=""Microsoft.Policies.Test""/>
  </policyNamespaces>
  <policies>
    <policy name=""EnableAuditPolicy""
            displayName=""Enable Audit Policy""
            key=""Software\Policies\Audit""
            valueName=""AuditEnabled""
            class=""Machine"">
    </policy>
    <policy name=""RequireSmartCard""
            displayName=""Require Smart Card""
            key=""Software\Policies\SmartCard""
            valueName=""Enabled""
            class=""Machine"">
    </policy>
  </policies>
</policyDefinitions>";

    private const string NoNamespaceAdmx = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<policyDefinitions revision=""1.0"" schemaVersion=""1.0"">
  <policies>
    <policy name=""Policy1"" displayName=""Policy One"" key=""Software\Test"" valueName=""Val"" class=""Machine""/>
  </policies>
</policyDefinitions>";

    private const string EmptyPoliciesAdmx = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<policyDefinitions revision=""1.0"" schemaVersion=""1.0"">
  <policyNamespaces>
    <target prefix=""t"" namespace=""NS.Test""/>
  </policyNamespaces>
  <policies/>
</policyDefinitions>";

    private const string PolicyNoNameAdmx = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<policyDefinitions revision=""1.0"" schemaVersion=""1.0"">
  <policies>
    <policy displayName=""No Name Policy"" key=""Software\Test"" valueName=""Val"" class=""Machine""/>
  </policies>
</policyDefinitions>";

    private static string WriteFile(TempDirectory tmp, string name, string content)
    {
        var path = tmp.File(name);
        File.WriteAllText(path, content);
        return path;
    }

    // ── Parse (single ADMX) ─────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidAdmx_ReturnsPolicies()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "test.admx", SimpleAdmx);

        var controls = GpoParser.Parse(path, "TestPack");

        controls.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_SetsRuleId_FromPolicyName()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "test.admx", SimpleAdmx);

        var controls = GpoParser.Parse(path, "Pack");

        controls.Should().Contain(c => c.ExternalIds.RuleId == "EnableAuditPolicy");
        controls.Should().Contain(c => c.ExternalIds.RuleId == "RequireSmartCard");
    }

    [Fact]
    public void Parse_SetsBenchmarkId_FromNamespace()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "test.admx", SimpleAdmx);

        var controls = GpoParser.Parse(path, "Pack");

        controls.Should().AllSatisfy(c =>
            c.ExternalIds.BenchmarkId.Should().Be("Microsoft.Policies.Test"));
    }

    [Fact]
    public void Parse_SetsPackName()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "test.admx", SimpleAdmx);

        var controls = GpoParser.Parse(path, "MyPack");

        controls.Should().AllSatisfy(c => c.Revision.PackName.Should().Be("MyPack"));
    }

    [Fact]
    public void Parse_SetsDiscussion_ContainingRegistryKey()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "test.admx", SimpleAdmx);

        var controls = GpoParser.Parse(path, "Pack");

        controls.Should().AllSatisfy(c => c.Discussion.Should().Contain("Registry Key:"));
    }

    [Fact]
    public void Parse_SetsCheckText_ContainingValueName()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "test.admx", SimpleAdmx);

        var controls = GpoParser.Parse(path, "Pack");

        controls.Should().Contain(c => c.CheckText!.Contains("AuditEnabled"));
    }

    [Fact]
    public void Parse_EmptyPolicies_ReturnsEmptyList()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "empty.admx", EmptyPoliciesAdmx);

        var controls = GpoParser.Parse(path, "Pack");

        controls.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PolicyWithNoName_IsSkipped()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "noname.admx", PolicyNoNameAdmx);

        var controls = GpoParser.Parse(path, "Pack");

        controls.Should().BeEmpty();
    }

    [Fact]
    public void Parse_NoNamespaceAdmx_BenchmarkIdIsNull()
    {
        using var tmp = new TempDirectory();
        var path = WriteFile(tmp, "nons.admx", NoNamespaceAdmx);

        var controls = GpoParser.Parse(path, "Pack");

        controls.Should().HaveCount(1);
        controls[0].ExternalIds.BenchmarkId.Should().BeNull();
    }

    [Fact]
    public void Parse_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => GpoParser.Parse("/nonexistent/path/test.admx", "Pack");

        act.Should().Throw<FileNotFoundException>();
    }

    // ── ParsePackage ────────────────────────────────────────────────────────

    [Fact]
    public void ParsePackage_EmptyDirectory_ReturnsEmptyResult()
    {
        using var tmp = new TempDirectory();

        var result = GpoParser.ParsePackage(tmp.Path, "Pack");

        result.Controls.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
        result.AdmxFileCount.Should().Be(0);
        result.PolFileCount.Should().Be(0);
        result.InfFileCount.Should().Be(0);
    }

    [Fact]
    public void ParsePackage_WithAdmxFile_ParsesAdmx()
    {
        using var tmp = new TempDirectory();
        WriteFile(tmp, "policy.admx", SimpleAdmx);

        var result = GpoParser.ParsePackage(tmp.Path, "Pack");

        result.AdmxFileCount.Should().Be(1);
        result.Controls.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void ParsePackage_WithInvalidAdmxFile_AddsWarning()
    {
        using var tmp = new TempDirectory();
        WriteFile(tmp, "broken.admx", "<broken xml");

        var result = GpoParser.ParsePackage(tmp.Path, "Pack");

        result.Warnings.Should().Contain(w => w.Contains("ADMX parse failed") || w.Contains("broken.admx"));
    }

    [Fact]
    public void ParsePackage_WithGptTmplInf_ParsesInfFile()
    {
        using var tmp = new TempDirectory();
        var infDir = Directory.CreateDirectory(tmp.File("LocalPolicies"));
        var infContent = "[System Access]\r\nMinimumPasswordLength = 14\r\n";
        File.WriteAllText(Path.Combine(infDir.FullName, "GptTmpl.inf"), infContent);

        var result = GpoParser.ParsePackage(tmp.Path, "Pack");

        result.InfFileCount.Should().Be(1);
        result.Controls.Should().Contain(c => c.Title.Contains("Minimum"));
    }

    [Fact]
    public void ParsePackage_OsTargetInferredFromFolderName()
    {
        using var tmp = new TempDirectory();
        var winDir = Directory.CreateDirectory(tmp.File("Windows 11"));
        File.WriteAllText(Path.Combine(winDir.FullName, "policy.admx"), SimpleAdmx);

        var result = GpoParser.ParsePackage(tmp.Path, "Pack");

        result.Controls.Should().Contain(c => c.Applicability.OsTarget == OsTarget.Win11);
    }

    [Fact]
    public void ParsePackage_OsScopes_AreMappedFromAdmxTemplatesFolder()
    {
        using var tmp = new TempDirectory();
        var admxRoot = Directory.CreateDirectory(tmp.File("ADMX Templates"));
        var win11Dir = Directory.CreateDirectory(Path.Combine(admxRoot.FullName, "Windows 11"));
        File.WriteAllText(Path.Combine(win11Dir.FullName, "test.admx"), SimpleAdmx);

        var result = GpoParser.ParsePackage(tmp.Path, "Pack");

        result.OsScopes.Should().Contain(s => s.OsTarget == OsTarget.Win11);
    }

    [Fact]
    public void ParsePackage_ControlId_IsUniquePerControl()
    {
        using var tmp = new TempDirectory();
        WriteFile(tmp, "policy.admx", SimpleAdmx);

        var result = GpoParser.ParsePackage(tmp.Path, "Pack");

        var ids = result.Controls.Select(c => c.ControlId).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }
}
