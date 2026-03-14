using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class DomainGpoBackupParserTests
{
    // ── Fixture helpers ─────────────────────────────────────────────────────

    private const string SampleBackupXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<GroupPolicyBackupScheme BackupSchemeVersion=""2.0"">
  <GroupPolicyObject>
    <GroupPolicyObjectId>{31B2F340-016D-11D2-945F-00C04FB984F9}</GroupPolicyObjectId>
    <DisplayName>DISA STIG - Windows Server 2022 Member Server</DisplayName>
    <BackupId>{AABBCCDD-1122-3344-5566-778899AABBCC}</BackupId>
  </GroupPolicyObject>
</GroupPolicyBackupScheme>";

    private const string SampleBkupInfoXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<BackupInformation>
  <GPOGuid>{DEADBEEF-CAFE-BABE-FEED-000000000001}</GPOGuid>
  <GPODisplayName>Domain Controller Security Policy</GPODisplayName>
</BackupInformation>";

    private static string MakeGpoDir(TempDirectory tmp, string? guidOverride = null)
    {
        var guid = guidOverride ?? "{" + Guid.NewGuid().ToString().ToUpper() + "}";
        var gposRoot = Directory.CreateDirectory(tmp.File("GPOs"));
        var gpoDir = Directory.CreateDirectory(Path.Combine(gposRoot.FullName, guid));
        return gpoDir.FullName;
    }

    // ── ParseBackups: no GPOs folder ────────────────────────────────────────

    [Fact]
    public void ParseBackups_NoGposDirectory_ReturnsEmptyResult()
    {
        using var tmp = new TempDirectory();

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Controls.Should().BeEmpty();
        result.Backups.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    // ── ParseBackups: Backup.xml ─────────────────────────────────────────────

    [Fact]
    public void ParseBackups_WithBackupXml_ParsesDisplayName()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups.Should().HaveCount(1);
        result.Backups[0].DisplayName.Should().Contain("DISA STIG");
    }

    [Fact]
    public void ParseBackups_WithBackupXml_ParsesGpoGuid()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups[0].GpoGuid.Should().Contain("31B2F340");
    }

    [Fact]
    public void ParseBackups_WithBackupXml_ParsesBackupId()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups[0].BackupId.Should().Contain("AABBCCDD");
    }

    // ── ParseBackups: bkupInfo.xml fallback ─────────────────────────────────

    [Fact]
    public void ParseBackups_WithBkupInfoXml_ParsesDisplayName()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "bkupInfo.xml"), SampleBkupInfoXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups.Should().HaveCount(1);
        result.Backups[0].DisplayName.Should().Contain("Domain Controller Security Policy");
    }

    [Fact]
    public void ParseBackups_WithBkupInfoXml_ParsesGpoGuid()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "bkupInfo.xml"), SampleBkupInfoXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups[0].GpoGuid.Should().Contain("DEADBEEF");
    }

    // ── ParseBackups: DomainSysvol fallback (no XML) ────────────────────────

    [Fact]
    public void ParseBackups_DomainSysvolOnly_ReturnsUnnamedBackup()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        Directory.CreateDirectory(Path.Combine(gpoDir, "DomainSysvol"));

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups.Should().HaveCount(1);
        result.Backups[0].DisplayName.Should().StartWith("GPO Backup");
    }

    [Fact]
    public void ParseBackups_NoXmlNoDomainSysvol_FolderIsSkipped()
    {
        using var tmp = new TempDirectory();
        MakeGpoDir(tmp); // empty GUID folder

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups.Should().BeEmpty();
    }

    // ── ParseBackups: non-GUID folder names ─────────────────────────────────

    [Fact]
    public void ParseBackups_NonGuidFolderName_IsSkipped()
    {
        using var tmp = new TempDirectory();
        var gposRoot = Directory.CreateDirectory(tmp.File("GPOs"));
        var notGuid = Directory.CreateDirectory(Path.Combine(gposRoot.FullName, "NotAGuid"));
        File.WriteAllText(Path.Combine(notGuid.FullName, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups.Should().BeEmpty();
    }

    // ── ParseBackups: control records ───────────────────────────────────────

    [Fact]
    public void ParseBackups_CreatesControlRecord_WithCorrectTitle()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Controls.Should().HaveCount(1);
        result.Controls[0].Title.Should().StartWith("Domain GPO:");
        result.Controls[0].Title.Should().Contain("DISA STIG");
    }

    [Fact]
    public void ParseBackups_ControlRecord_IsManual()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Controls[0].IsManual.Should().BeTrue();
    }

    [Fact]
    public void ParseBackups_ControlRecord_HasDomainControllerRoleTag()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Controls[0].Applicability.RoleTags.Should().Contain(RoleTemplate.DomainController);
    }

    [Fact]
    public void ParseBackups_ControlRecord_SetsPackName()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "MyPack");

        result.Controls[0].Revision.PackName.Should().Be("MyPack");
    }

    [Fact]
    public void ParseBackups_ControlRecord_HighConfidence()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), SampleBackupXml);

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Controls[0].Applicability.Confidence.Should().Be(Confidence.High);
    }

    // ── ParseBackups: multiple backups ──────────────────────────────────────

    [Fact]
    public void ParseBackups_MultipleGuidFolders_ParsesAll()
    {
        using var tmp = new TempDirectory();
        var gposRoot = Directory.CreateDirectory(tmp.File("GPOs"));

        for (int i = 0; i < 3; i++)
        {
            var guid = "{" + Guid.NewGuid().ToString().ToUpper() + "}";
            var gpoDir = Directory.CreateDirectory(Path.Combine(gposRoot.FullName, guid));
            Directory.CreateDirectory(Path.Combine(gpoDir.FullName, "DomainSysvol"));
        }

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Backups.Should().HaveCount(3);
        result.Controls.Should().HaveCount(3);
    }

    // ── ParseBackups: corrupt XML ────────────────────────────────────────────

    [Fact]
    public void ParseBackups_CorruptBackupXml_AddsWarning()
    {
        using var tmp = new TempDirectory();
        var gpoDir = MakeGpoDir(tmp);
        File.WriteAllText(Path.Combine(gpoDir, "Backup.xml"), "<bad xml");

        var result = DomainGpoBackupParser.ParseBackups(tmp.Path, "Pack");

        result.Warnings.Should().NotBeEmpty();
        result.Backups.Should().BeEmpty();
    }

    // ── GenerateGpmcImportScript ─────────────────────────────────────────────

    [Fact]
    public void GenerateGpmcImportScript_EmptyBackups_ReturnsScriptWithHeader()
    {
        var script = DomainGpoBackupParser.GenerateGpmcImportScript([], "/staged/path");

        script.Should().Contain("#Requires -Modules GroupPolicy");
        script.Should().Contain("Import-DomainGpos.ps1");
    }

    [Fact]
    public void GenerateGpmcImportScript_WithBackups_ContainsImportGpoCalls()
    {
        var backups = new List<DomainGpoBackupInfo>
        {
            new() { BackupId = "AABB-1122", DisplayName = "Test GPO Policy", BackupPath = "/staged" },
            new() { BackupId = "CCDD-3344", DisplayName = "Another GPO", BackupPath = "/staged" }
        };

        var script = DomainGpoBackupParser.GenerateGpmcImportScript(backups, "/staged/path");

        script.Should().Contain("Import-GPO");
        script.Should().Contain("AABB-1122");
        script.Should().Contain("Test GPO Policy");
        script.Should().Contain("CCDD-3344");
        script.Should().Contain("Another GPO");
    }

    [Fact]
    public void GenerateGpmcImportScript_EscapesSingleQuotes_InDisplayName()
    {
        var backups = new List<DomainGpoBackupInfo>
        {
            new() { BackupId = "1234", DisplayName = "It's a Policy", BackupPath = "/staged" }
        };

        var script = DomainGpoBackupParser.GenerateGpmcImportScript(backups, "/path");

        // Single quotes in display names must be escaped as '' for PowerShell
        script.Should().Contain("It''s a Policy");
    }

    [Fact]
    public void GenerateGpmcImportScript_ContainsWhatIfBlock()
    {
        var backups = new List<DomainGpoBackupInfo>
        {
            new() { BackupId = "GUID-001", DisplayName = "My GPO", BackupPath = "/staged" }
        };

        var script = DomainGpoBackupParser.GenerateGpmcImportScript(backups, "/path");

        script.Should().Contain("WhatIf");
        script.Should().Contain("[WhatIf]");
    }
}
