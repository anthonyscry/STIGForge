using FluentAssertions;
using STIGForge.Apply.OrgSettings;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply;

/// <summary>
/// Tests for OrgSettingsSerializer: Save, Load, GetDefaultPath, and MergeAnswers.
/// </summary>
public sealed class OrgSettingsSerializerTests : IDisposable
{
  private readonly string _tempDir;

  public OrgSettingsSerializerTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-orgsettings-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
  }

  // ── Save ────────────────────────────────────────────────────────────────────

  [Fact]
  public void Save_CreatesFile()
  {
    var path = Path.Combine(_tempDir, "profile.stigorgsettings.json");
    var profile = BuildProfile("TestProfile");

    OrgSettingsSerializer.Save(path, profile);

    File.Exists(path).Should().BeTrue();
  }

  [Fact]
  public void Save_WritesValidJson()
  {
    var path = Path.Combine(_tempDir, "profile.stigorgsettings.json");
    var profile = BuildProfile("TestProfile");

    OrgSettingsSerializer.Save(path, profile);

    var content = File.ReadAllText(path);
    content.Should().NotBeNullOrWhiteSpace();
    var act = () => System.Text.Json.JsonDocument.Parse(content);
    act.Should().NotThrow("the file must contain valid JSON");
  }

  [Fact]
  public void Save_UpdatesUpdatedAtTimestamp()
  {
    var path = Path.Combine(_tempDir, "timestamp.stigorgsettings.json");
    var before = DateTimeOffset.UtcNow.AddSeconds(-1);
    var profile = BuildProfile("Timestamp");

    OrgSettingsSerializer.Save(path, profile);

    profile.UpdatedAt.Should().BeAfter(before);
  }

  [Fact]
  public void Save_CreatesParentDirectory()
  {
    var nested = Path.Combine(_tempDir, "nested", "deep", "profile.stigorgsettings.json");

    OrgSettingsSerializer.Save(nested, BuildProfile("Nested"));

    File.Exists(nested).Should().BeTrue();
  }

  [Fact]
  public void Save_IsAtomic_NoTempFileRemainsAfterSuccess()
  {
    var path = Path.Combine(_tempDir, "atomic.stigorgsettings.json");

    OrgSettingsSerializer.Save(path, BuildProfile("Atomic"));

    File.Exists(path + ".tmp").Should().BeFalse("temp file should be deleted after atomic rename");
  }

  [Fact]
  public void Save_OverwritesExistingFile()
  {
    var path = Path.Combine(_tempDir, "overwrite.stigorgsettings.json");
    OrgSettingsSerializer.Save(path, BuildProfile("First"));

    var profile2 = BuildProfile("Second");
    profile2.OsTarget = "Windows2022";
    OrgSettingsSerializer.Save(path, profile2);

    var loaded = OrgSettingsSerializer.Load(path);
    loaded!.OsTarget.Should().Be("Windows2022");
  }

  // ── Load ────────────────────────────────────────────────────────────────────

  [Fact]
  public void Load_ReturnsNullForMissingFile()
  {
    var path = Path.Combine(_tempDir, "nonexistent.stigorgsettings.json");

    var result = OrgSettingsSerializer.Load(path);

    result.Should().BeNull();
  }

  [Fact]
  public void Load_RoundTripsProfileName()
  {
    var path = Path.Combine(_tempDir, "roundtrip.stigorgsettings.json");
    var profile = BuildProfile("RoundTripTest");

    OrgSettingsSerializer.Save(path, profile);
    var loaded = OrgSettingsSerializer.Load(path);

    loaded.Should().NotBeNull();
    loaded!.ProfileName.Should().Be("RoundTripTest");
  }

  [Fact]
  public void Load_RoundTripsEntries()
  {
    var path = Path.Combine(_tempDir, "entries.stigorgsettings.json");
    var profile = BuildProfile("WithEntries");
    profile.Entries.Add(new OrgSettingEntry { RuleId = "V-100001", Value = "someValue", Category = "Registry" });
    profile.Entries.Add(new OrgSettingEntry { RuleId = "V-100002", Value = "otherValue", Category = "Service" });

    OrgSettingsSerializer.Save(path, profile);
    var loaded = OrgSettingsSerializer.Load(path);

    loaded!.Entries.Should().HaveCount(2);
    loaded.Entries[0].RuleId.Should().Be("V-100001");
    loaded.Entries[0].Value.Should().Be("someValue");
    loaded.Entries[1].RuleId.Should().Be("V-100002");
  }

  [Fact]
  public void Load_RoundTripsAllProfileFields()
  {
    var path = Path.Combine(_tempDir, "allfields.stigorgsettings.json");
    var profile = new OrgSettingsProfile
    {
      ProfileName = "FullProfile",
      OsTarget = "WindowsServer2019",
      RoleTemplate = "MemberServer",
      StigVersion = "2.5",
      CreatedBy = "UnitTest",
      CreatedAt = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero)
    };

    OrgSettingsSerializer.Save(path, profile);
    var loaded = OrgSettingsSerializer.Load(path);

    loaded!.OsTarget.Should().Be("WindowsServer2019");
    loaded.RoleTemplate.Should().Be("MemberServer");
    loaded.StigVersion.Should().Be("2.5");
    loaded.CreatedBy.Should().Be("UnitTest");
  }

  // ── GetDefaultPath ──────────────────────────────────────────────────────────

  [Fact]
  public void GetDefaultPath_ReturnsPathInOutputDirectory()
  {
    var path = OrgSettingsSerializer.GetDefaultPath(_tempDir, "MyProfile");

    path.Should().StartWith(_tempDir);
  }

  [Fact]
  public void GetDefaultPath_UsesStignOrgsettingsExtension()
  {
    var path = OrgSettingsSerializer.GetDefaultPath(_tempDir, "MyProfile");

    path.Should().EndWith(".stigorgsettings.json");
  }

  [Fact]
  public void GetDefaultPath_IncludesProfileNameInFileName()
  {
    var path = OrgSettingsSerializer.GetDefaultPath(_tempDir, "MyProfile");

    Path.GetFileName(path).Should().Contain("MyProfile");
  }

  [Fact]
  public void GetDefaultPath_SanitizesInvalidFileNameCharacters()
  {
    var path = OrgSettingsSerializer.GetDefaultPath(_tempDir, "My:Profile/With*Chars");

    var fileName = Path.GetFileName(path);
    fileName.Should().NotContain(":");
    fileName.Should().NotContain("/");
    fileName.Should().NotContain("*");
  }

  [Fact]
  public void GetDefaultPath_DifferentProfileNameProducesDifferentPath()
  {
    var path1 = OrgSettingsSerializer.GetDefaultPath(_tempDir, "ProfileA");
    var path2 = OrgSettingsSerializer.GetDefaultPath(_tempDir, "ProfileB");

    path1.Should().NotBe(path2);
  }

  // ── MergeAnswers ─────────────────────────────────────────────────────────────

  [Fact]
  public void MergeAnswers_OverwritesMatchingEntryValues()
  {
    var entries = new List<OrgSettingEntry>
    {
      new() { RuleId = "V-100001", Value = "defaultValue" }
    };
    var saved = new OrgSettingsProfile();
    saved.Entries.Add(new OrgSettingEntry { RuleId = "V-100001", Value = "userProvidedValue" });

    OrgSettingsSerializer.MergeAnswers(entries, saved);

    entries[0].Value.Should().Be("userProvidedValue");
  }

  [Fact]
  public void MergeAnswers_IgnoresEntriesNotInSavedProfile()
  {
    var entries = new List<OrgSettingEntry>
    {
      new() { RuleId = "V-100001", Value = "unchanged" }
    };
    var saved = new OrgSettingsProfile();
    saved.Entries.Add(new OrgSettingEntry { RuleId = "V-999999", Value = "irrelevant" });

    OrgSettingsSerializer.MergeAnswers(entries, saved);

    entries[0].Value.Should().Be("unchanged");
  }

  [Fact]
  public void MergeAnswers_DoesNotMergeBlankSavedValues()
  {
    var entries = new List<OrgSettingEntry>
    {
      new() { RuleId = "V-100001", Value = "defaultValue" }
    };
    var saved = new OrgSettingsProfile();
    saved.Entries.Add(new OrgSettingEntry { RuleId = "V-100001", Value = "   " });

    OrgSettingsSerializer.MergeAnswers(entries, saved);

    entries[0].Value.Should().Be("defaultValue", "blank saved values should not overwrite defaults");
  }

  [Fact]
  public void MergeAnswers_IsCaseInsensitiveForRuleId()
  {
    var entries = new List<OrgSettingEntry>
    {
      new() { RuleId = "V-100001", Value = "original" }
    };
    var saved = new OrgSettingsProfile();
    saved.Entries.Add(new OrgSettingEntry { RuleId = "v-100001", Value = "caseInsensitiveValue" });

    OrgSettingsSerializer.MergeAnswers(entries, saved);

    entries[0].Value.Should().Be("caseInsensitiveValue");
  }

  [Fact]
  public void MergeAnswers_HandlesEmptyEntriesList()
  {
    var entries = new List<OrgSettingEntry>();
    var saved = new OrgSettingsProfile();
    saved.Entries.Add(new OrgSettingEntry { RuleId = "V-100001", Value = "value" });

    var act = () => OrgSettingsSerializer.MergeAnswers(entries, saved);

    act.Should().NotThrow();
    entries.Should().BeEmpty();
  }

  [Fact]
  public void MergeAnswers_HandlesEmptySavedProfile()
  {
    var entries = new List<OrgSettingEntry>
    {
      new() { RuleId = "V-100001", Value = "keepMe" }
    };
    var saved = new OrgSettingsProfile();

    OrgSettingsSerializer.MergeAnswers(entries, saved);

    entries[0].Value.Should().Be("keepMe");
  }

  [Fact]
  public void MergeAnswers_MergesMultipleEntries()
  {
    var entries = new List<OrgSettingEntry>
    {
      new() { RuleId = "V-100001", Value = "d1" },
      new() { RuleId = "V-100002", Value = "d2" },
      new() { RuleId = "V-100003", Value = "d3" }
    };
    var saved = new OrgSettingsProfile();
    saved.Entries.Add(new OrgSettingEntry { RuleId = "V-100001", Value = "user1" });
    saved.Entries.Add(new OrgSettingEntry { RuleId = "V-100003", Value = "user3" });

    OrgSettingsSerializer.MergeAnswers(entries, saved);

    entries[0].Value.Should().Be("user1");
    entries[1].Value.Should().Be("d2", "V-100002 was not in saved profile");
    entries[2].Value.Should().Be("user3");
  }

  // ── Helper ──────────────────────────────────────────────────────────────────

  private static OrgSettingsProfile BuildProfile(string name) => new()
  {
    ProfileName = name,
    OsTarget = "WindowsServer2019",
    RoleTemplate = "MemberServer",
    StigVersion = "2.4",
    CreatedBy = "TestRunner",
    CreatedAt = DateTimeOffset.UtcNow
  };
}
