using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Content;

public sealed class GptTmplParserTests : IDisposable
{
    private readonly string _tempDir;

    public GptTmplParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-gpttmpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void Parse_SystemAccessSection_ExtractsAccountPolicies()
    {
        var infPath = WriteInf(@"
[Unicode]
Unicode=yes
[System Access]
MinimumPasswordLength = 14
PasswordComplexity = 1
LockoutBadCount = 3
MaximumPasswordAge = 60
");
        var result = GptTmplParser.Parse(infPath, "TestPack");

        result.Controls.Should().HaveCount(4);
        result.Settings.Should().HaveCount(4);

        var minPwdLen = result.Controls.First(c => c.ExternalIds.RuleId!.Contains("MinimumPasswordLength"));
        minPwdLen.Severity.Should().Be("high");
        minPwdLen.Title.Should().Contain("14");
    }

    [Fact]
    public void Parse_EventAuditSection_ExtractsAuditPolicies()
    {
        var infPath = WriteInf(@"
[Event Audit]
AuditLogonEvents = 3
AuditPrivilegeUse = 2
");
        var result = GptTmplParser.Parse(infPath, "TestPack");

        result.Controls.Should().HaveCount(2);

        var logon = result.Controls.First(c => c.ExternalIds.RuleId!.Contains("AuditLogonEvents"));
        logon.Title.Should().Contain("Success and Failure");
        logon.Severity.Should().Be("medium");
    }

    [Fact]
    public void Parse_PrivilegeRightsSection_ExtractsUserRights()
    {
        var infPath = WriteInf(@"
[Privilege Rights]
SeRemoteInteractiveLogonRight = *S-1-5-32-544
SeBatchLogonRight = *S-1-5-32-544,*S-1-5-32-559
");
        var result = GptTmplParser.Parse(infPath, "TestPack");

        result.Controls.Should().HaveCount(2);

        var remoteLogon = result.Controls.First(c => c.ExternalIds.RuleId!.Contains("SeRemoteInteractiveLogonRight"));
        remoteLogon.Severity.Should().Be("high");
    }

    [Fact]
    public void Parse_RegistryValuesSection_ExtractsRegistrySettings()
    {
        var infPath = WriteInf(@"
[Registry Values]
MACHINE\Software\Microsoft\Windows\CurrentVersion\Policies\System\InactivityTimeoutSecs=4,900
");
        var result = GptTmplParser.Parse(infPath, "TestPack");

        result.Controls.Should().HaveCount(1);
        result.Controls[0].ExternalIds.RuleId.Should().Contain("RegistryValues");
    }

    [Fact]
    public void Parse_WithOsTarget_SetsApplicability()
    {
        var infPath = WriteInf(@"
[System Access]
MinimumPasswordLength = 14
");
        var result = GptTmplParser.Parse(infPath, "TestPack", OsTarget.Server2022);

        result.Controls[0].Applicability.OsTarget.Should().Be(OsTarget.Server2022);
    }

    [Fact]
    public void Parse_IgnoresUnrecognizedSections()
    {
        var infPath = WriteInf(@"
[Version]
signature=""$CHICAGO$""
Revision=1
[SomeRandomSection]
Key = Value
[System Access]
PasswordComplexity = 1
");
        var result = GptTmplParser.Parse(infPath, "TestPack");

        result.Controls.Should().HaveCount(1);
        result.Controls[0].ExternalIds.RuleId.Should().Contain("PasswordComplexity");
    }

    [Fact]
    public void Parse_IgnoresCommentLines()
    {
        var infPath = WriteInf(@"
[System Access]
; This is a comment
MinimumPasswordLength = 14
");
        var result = GptTmplParser.Parse(infPath, "TestPack");
        result.Controls.Should().HaveCount(1);
    }

    private string WriteInf(string content)
    {
        var path = Path.Combine(_tempDir, "GptTmpl.inf");
        File.WriteAllText(path, content.TrimStart());
        return path;
    }
}
