using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Content;

public sealed class DomainGpoBackupParserTests : IDisposable
{
    private readonly string _tempDir;

    public DomainGpoBackupParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-domaingpo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public void ParseBackups_WithBackupXml_ExtractsDisplayNameAndGuid()
    {
        var root = CreatePackageWithDomainGpos(new (string, string, string?)[]
        {
            ("{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}", "DoD Windows 11 - Computer", "{11111111-2222-3333-4444-555555555555}")
        });

        var result = DomainGpoBackupParser.ParseBackups(root, "TestPack");

        result.Backups.Should().HaveCount(1);
        result.Backups[0].DisplayName.Should().Be("DoD Windows 11 - Computer");
        result.Backups[0].GpoGuid.Should().Be("{11111111-2222-3333-4444-555555555555}");
        result.Controls.Should().HaveCount(1);
    }

    [Fact]
    public void ParseBackups_ControlsAreMarkedDcOnlyAndManual()
    {
        var root = CreatePackageWithDomainGpos(new (string, string, string?)[]
        {
            ("{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}", "DoD Server 2022 - Domain Security", null)
        });

        var result = DomainGpoBackupParser.ParseBackups(root, "TestPack");

        var control = result.Controls[0];
        control.IsManual.Should().BeTrue();
        control.Applicability.RoleTags.Should().Contain(RoleTemplate.DomainController);
        control.Applicability.Confidence.Should().Be(Confidence.High);
        control.ExternalIds.BenchmarkId.Should().Be("domain-gpo-backup");
        control.Title.Should().Contain("DoD Server 2022 - Domain Security");
    }

    [Fact]
    public void ParseBackups_MultipleBackups_ParsesAll()
    {
        var root = CreatePackageWithDomainGpos(new (string, string, string?)[]
        {
            ("{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}", "DoD Windows 11 - Computer", null),
            ("{B2C3D4E5-F6A7-8901-BCDE-F12345678901}", "DoD Windows 11 - User", null)
        });

        var result = DomainGpoBackupParser.ParseBackups(root, "TestPack");

        result.Backups.Should().HaveCount(2);
        result.Controls.Should().HaveCount(2);
    }

    [Fact]
    public void ParseBackups_NoGposFolder_ReturnsEmpty()
    {
        var root = Path.Combine(_tempDir, "empty-package");
        Directory.CreateDirectory(root);

        var result = DomainGpoBackupParser.ParseBackups(root, "TestPack");

        result.Backups.Should().BeEmpty();
        result.Controls.Should().BeEmpty();
    }

    [Fact]
    public void ParseBackups_BackupWithDomainSysvolOnly_CreatesUnnamedBackup()
    {
        var root = Path.Combine(_tempDir, "package");
        var gposDir = Path.Combine(root, "GPOs");
        var backupDir = Path.Combine(gposDir, "{C3D4E5F6-A7B8-9012-CDEF-123456789012}");
        var sysvolDir = Path.Combine(backupDir, "DomainSysvol", "GPO", "Machine");
        Directory.CreateDirectory(sysvolDir);
        File.WriteAllText(Path.Combine(sysvolDir, "Registry.pol"), "dummy");

        var result = DomainGpoBackupParser.ParseBackups(root, "TestPack");

        result.Backups.Should().HaveCount(1);
        result.Backups[0].DisplayName.Should().Contain("GPO Backup");
    }

    [Fact]
    public void GenerateGpmcImportScript_ContainsImportGpoCommands()
    {
        var backups = new[]
        {
            new DomainGpoBackupInfo
            {
                BackupId = "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}",
                DisplayName = "DoD Windows 11 - Computer",
                BackupPath = "/tmp/test"
            },
            new DomainGpoBackupInfo
            {
                BackupId = "{B2C3D4E5-F6A7-8901-BCDE-F12345678901}",
                DisplayName = "DoD Windows 11 - User",
                BackupPath = "/tmp/test2"
            }
        };

        var script = DomainGpoBackupParser.GenerateGpmcImportScript(backups, "/staged/DomainGPOs");

        script.Should().Contain("#Requires -Modules GroupPolicy");
        script.Should().Contain("Import-GPO");
        script.Should().Contain("DoD Windows 11 - Computer");
        script.Should().Contain("DoD Windows 11 - User");
        script.Should().Contain("-BackupId");
        script.Should().Contain("-CreateIfNeeded");
    }

    [Fact]
    public void ParseBackups_WithBkupInfoXml_ExtractsDisplayName()
    {
        var root = Path.Combine(_tempDir, "package");
        var gposDir = Path.Combine(root, "GPOs");
        var backupDir = Path.Combine(gposDir, "{D4E5F6A7-B8C9-0123-DEF0-123456789ABC}");
        Directory.CreateDirectory(backupDir);

        // Write bkupInfo.xml instead of Backup.xml
        File.WriteAllText(Path.Combine(backupDir, "bkupInfo.xml"),
            "<?xml version=\"1.0\"?><BackupInst><GPODisplayName>AD Domain Security</GPODisplayName><GPOGuid>{99999999-8888-7777-6666-555544443333}</GPOGuid></BackupInst>");

        // Need DomainSysvol to be recognized
        Directory.CreateDirectory(Path.Combine(backupDir, "DomainSysvol"));

        var result = DomainGpoBackupParser.ParseBackups(root, "TestPack");

        result.Backups.Should().HaveCount(1);
        result.Backups[0].DisplayName.Should().Be("AD Domain Security");
        result.Backups[0].GpoGuid.Should().Be("{99999999-8888-7777-6666-555544443333}");
    }

    private string CreatePackageWithDomainGpos((string guid, string displayName, string? gpoGuid)[] gpos)
    {
        var root = Path.Combine(_tempDir, "gpo-package");
        var gposDir = Path.Combine(root, "GPOs");

        foreach (var (guid, displayName, gpoGuid) in gpos)
        {
            var backupDir = Path.Combine(gposDir, guid);
            Directory.CreateDirectory(backupDir);

            // Write Backup.xml
            var gpoGuidElement = gpoGuid != null
                ? $"<GroupPolicyObjectId>{gpoGuid}</GroupPolicyObjectId>"
                : string.Empty;

            File.WriteAllText(Path.Combine(backupDir, "Backup.xml"),
                $"<?xml version=\"1.0\"?><GroupPolicyBackupScheme><GroupPolicyObject><DisplayName>{displayName}</DisplayName>{gpoGuidElement}<BackupId>{guid}</BackupId></GroupPolicyObject></GroupPolicyBackupScheme>");

            // Create DomainSysvol structure
            var sysvolDir = Path.Combine(backupDir, "DomainSysvol", "GPO", "Machine");
            Directory.CreateDirectory(sysvolDir);
        }

        return root;
    }
}
