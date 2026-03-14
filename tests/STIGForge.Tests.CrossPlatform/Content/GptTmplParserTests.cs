using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class GptTmplParserTests
{
    // ── Fixture helpers ─────────────────────────────────────────────────────

    private static string WriteInf(TempDirectory tmp, string content, string name = "GptTmpl.inf")
    {
        var path = tmp.File(name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string FullInfContent = @"[Unicode]
Unicode=yes

[System Access]
MinimumPasswordLength = 14
MaximumPasswordAge = 60
PasswordComplexity = 1
LockoutBadCount = 3

[Event Audit]
AuditAccountLogon = 3
AuditLogonEvents = 3
AuditPolicyChange = 1
AuditSystemEvents = 2

[Privilege Rights]
SeRemoteInteractiveLogonRight = *S-1-5-32-544
SeDebugPrivilege = *S-1-5-32-544
SeTcbPrivilege =
SeNetworkLogonRight = *S-1-5-32-544,*S-1-5-32-545

[Registry Values]
MACHINE\System\CurrentControlSet\Control\Lsa\SCENoApplyLegacyAuditPolicy=4,1
MACHINE\Software\Microsoft\Windows\CurrentVersion\Policies\System\EnableSmartScreen=4,1
";

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_SystemAccessSection_ReturnsControls()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "TestPack");

        result.Controls.Should().Contain(c => c.Title.Contains("Minimum"));
        result.Settings.Should().Contain(s => s.Section == "System Access");
    }

    [Fact]
    public void Parse_EventAuditSection_ReturnsControls()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().Contain(c => c.Title.Contains("Audit Policy:"));
        result.Settings.Should().Contain(s => s.Section == "Event Audit");
    }

    [Fact]
    public void Parse_PrivilegeRightsSection_ReturnsControls()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().Contain(c => c.Title.Contains("User Right:"));
        result.Settings.Should().Contain(s => s.Section == "Privilege Rights");
    }

    [Fact]
    public void Parse_RegistryValuesSection_ReturnsControls()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().Contain(c => c.Title.Contains("Registry Security:"));
        result.Settings.Should().Contain(s => s.Section == "Registry Values");
    }

    [Fact]
    public void Parse_SetsPackName()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "MyPack");

        result.Controls.Should().AllSatisfy(c => c.Revision.PackName.Should().Be("MyPack"));
    }

    [Fact]
    public void Parse_SetsSourcePath()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack");

        result.SourcePath.Should().Be(path);
    }

    [Fact]
    public void Parse_OsTarget_IsPassedThrough()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack", OsTarget.Server2022);

        result.Controls.Should().AllSatisfy(c =>
            c.Applicability.OsTarget.Should().Be(OsTarget.Server2022));
    }

    [Fact]
    public void Parse_RuleIds_ContainSectionAndKey()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().Contain(c =>
            c.ExternalIds.RuleId!.Contains("SystemAccess") &&
            c.ExternalIds.RuleId.Contains("MinimumPasswordLength"));
    }

    // ── Severity inference ──────────────────────────────────────────────────

    [Theory]
    [InlineData("LockoutBadCount", "3", "high")]
    [InlineData("MinimumPasswordLength", "14", "high")]
    [InlineData("PasswordComplexity", "1", "high")]
    [InlineData("MaximumPasswordAge", "60", "medium")]
    public void Parse_SystemAccess_InfersSeverity(string key, string value, string expectedSeverity)
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, $"[System Access]\r\n{key} = {value}\r\n");

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls[0].Severity.Should().Be(expectedSeverity);
    }

    [Theory]
    [InlineData("SeRemoteInteractiveLogonRight", "*S-1-5-32-544", "high")]
    [InlineData("SeDebugPrivilege", "*S-1-5-32-544", "high")]
    [InlineData("SeTcbPrivilege", "", "high")]
    [InlineData("SeNetworkLogonRight", "*S-1-5-32-544", "medium")]
    public void Parse_PrivilegeRights_InfersSeverity(string key, string value, string expectedSeverity)
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, $"[Privilege Rights]\r\n{key} = {value}\r\n");

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls[0].Severity.Should().Be(expectedSeverity);
    }

    [Fact]
    public void Parse_EventAudit_IsAlwaysMediumSeverity()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, "[Event Audit]\r\nAuditLogonEvents = 3\r\n");

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls[0].Severity.Should().Be("medium");
    }

    // ── Audit value mapping ─────────────────────────────────────────────────

    [Theory]
    [InlineData("0", "No Auditing")]
    [InlineData("1", "Success")]
    [InlineData("2", "Failure")]
    [InlineData("3", "Success and Failure")]
    [InlineData("9", "9")]
    public void Parse_EventAudit_MapsAuditValues(string rawValue, string expectedLabel)
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, $"[Event Audit]\r\nAuditLogonEvents = {rawValue}\r\n");

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls[0].Title.Should().Contain(expectedLabel);
    }

    // ── Edge cases ──────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyFile_ReturnsEmptyResult()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, "");

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().BeEmpty();
        result.Settings.Should().BeEmpty();
    }

    [Fact]
    public void Parse_CommentsOnly_ReturnsEmptyResult()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, "; This is a comment\n; Another comment\n");

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().BeEmpty();
    }

    [Fact]
    public void Parse_UnknownSection_IsSkipped()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, "[Version]\r\nsignature=\"$CHICAGO$\"\r\nRevision=1\r\n");

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().BeEmpty();
    }

    [Fact]
    public void Parse_LineWithoutEquals_IsSkipped()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, "[System Access]\r\nInvalidLineWithoutEquals\r\nMinimumPasswordLength = 14\r\n");

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().HaveCount(1);
        result.Controls[0].Title.Should().Contain("Minimum");
    }

    [Fact]
    public void Parse_FileNotFound_ThrowsFileNotFoundException()
    {
        var act = () => GptTmplParser.Parse("/nonexistent/GptTmpl.inf", "Pack");

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Parse_ConfidenceIsHigh()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().AllSatisfy(c =>
            c.Applicability.Confidence.Should().Be(Confidence.High));
    }

    [Fact]
    public void Parse_IsManual_IsFalse_ForAllControls()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().AllSatisfy(c => c.IsManual.Should().BeFalse());
    }

    [Fact]
    public void Parse_TotalControlCount_MatchesSettingsCount()
    {
        using var tmp = new TempDirectory();
        var path = WriteInf(tmp, FullInfContent);

        var result = GptTmplParser.Parse(path, "Pack");

        result.Controls.Should().HaveSameCount(result.Settings);
    }
}
