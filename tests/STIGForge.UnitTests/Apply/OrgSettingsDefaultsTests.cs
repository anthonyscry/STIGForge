using FluentAssertions;
using STIGForge.Apply.OrgSettings;

namespace STIGForge.UnitTests.Apply;

/// <summary>
/// Tests for OrgSettingsDefaults: verifies STIG-compliant default values for
/// certificate thumbprints, security options, registry, and service rules.
/// </summary>
public sealed class OrgSettingsDefaultsTests
{
  private readonly IReadOnlyDictionary<string, OrgSettingDefault> _defaults;

  public OrgSettingsDefaultsTests()
  {
    _defaults = OrgSettingsDefaults.GetDefaults();
  }

  // ── GetDefaults ─────────────────────────────────────────────────────────────

  [Fact]
  public void GetDefaults_ReturnsNonEmptyDictionary()
  {
    _defaults.Should().NotBeEmpty();
  }

  [Fact]
  public void GetDefaults_ReturnsSameInstanceOnMultipleCalls()
  {
    var first = OrgSettingsDefaults.GetDefaults();
    var second = OrgSettingsDefaults.GetDefaults();
    first.Should().BeSameAs(second);
  }

  [Fact]
  public void GetDefaults_IsCaseInsensitive()
  {
    _defaults.ContainsKey("v-205648.a").Should().BeTrue();
    _defaults.ContainsKey("V-205648.A").Should().BeTrue();
    _defaults.ContainsKey("V-205648.a").Should().BeTrue();
  }

  // ── Certificate thumbprints (V-205648.x) ────────────────────────────────────

  [Fact]
  public void Defaults_ContainsDoDRootCA3()
  {
    _defaults.Should().ContainKey("V-205648.a");
    _defaults["V-205648.a"].Value.Should().Be("D73CA91102A2204A36459ED32213B467D7CE97FB");
    _defaults["V-205648.a"].Category.Should().Be("Certificate");
  }

  [Fact]
  public void Defaults_ContainsDoDRootCA4()
  {
    _defaults.Should().ContainKey("V-205648.b");
    _defaults["V-205648.b"].Value.Should().Be("B8269F25DBD937ECAFD4C35A9838571723F2D026");
    _defaults["V-205648.b"].Category.Should().Be("Certificate");
  }

  [Fact]
  public void Defaults_ContainsDoDRootCA5()
  {
    _defaults.Should().ContainKey("V-205648.c");
    _defaults["V-205648.c"].Value.Should().Be("4ECB440B02C5B31AA0CEE945023B194F8671A3A1");
  }

  [Fact]
  public void Defaults_ContainsDoDRootCA6()
  {
    _defaults.Should().ContainKey("V-205648.d");
    _defaults["V-205648.d"].Value.Should().Be("B1F2FAC5C7E53F887E2EC96AB2571F7D84D2BF29");
  }

  [Fact]
  public void Defaults_ContainsDoDInteroperabilityRootCA()
  {
    _defaults.Should().ContainKey("V-205649");
    _defaults["V-205649"].Value.Should().Be("FFAD03C4A9D90D4FE04D51A4F0B0F2AFAC13A1CD");
    _defaults["V-205649"].Category.Should().Be("Certificate");
    _defaults["V-205649"].Description.Should().Contain("Interoperability");
  }

  [Fact]
  public void Defaults_ContainsECARootCA4()
  {
    _defaults.Should().ContainKey("V-205650.a");
    _defaults["V-205650.a"].Value.Should().Be("A44B096012D2C32ADBFBDC7571FD39BD6DE3B997");
    _defaults["V-205650.a"].Category.Should().Be("Certificate");
  }

  [Fact]
  public void Defaults_ContainsECARootCA6()
  {
    _defaults.Should().ContainKey("V-205650.b");
    _defaults["V-205650.b"].Value.Should().Be("D8FE446FC40BE11F1D7B58EA10A85B3BA94BBFBC");
  }

  // ── Certificate thumbprints have expected length (40 hex chars) ─────────────

  [Theory]
  [InlineData("V-205648.a")]
  [InlineData("V-205648.b")]
  [InlineData("V-205648.c")]
  [InlineData("V-205648.d")]
  [InlineData("V-205649")]
  [InlineData("V-205650.a")]
  [InlineData("V-205650.b")]
  public void CertificateThumbprints_AreFortyUppercaseHexChars(string vulgId)
  {
    var value = _defaults[vulgId].Value;
    value.Should().HaveLength(40, "SHA-1 thumbprints are 40 hex characters");
    value.Should().MatchRegex("^[0-9A-F]+$", "thumbprints should be uppercase hex");
  }

  // ── Security option defaults (V-205909, V-205910) ────────────────────────────

  [Fact]
  public void Defaults_ContainsLogonMessageTitle()
  {
    _defaults.Should().ContainKey("V-205909");
    _defaults["V-205909"].Value.Should().Be("US Department of Defense Warning Statement");
    _defaults["V-205909"].Category.Should().Be("Security Option");
  }

  [Fact]
  public void Defaults_ContainsLogonMessageText()
  {
    _defaults.Should().ContainKey("V-205910");
    var banner = _defaults["V-205910"].Value;
    banner.Should().Contain("U.S. Government");
    banner.Should().Contain("USG-authorized use only");
    _defaults["V-205910"].Category.Should().Be("Security Option");
  }

  // ── Registry rule (V-205906) ─────────────────────────────────────────────────

  [Fact]
  public void Defaults_ContainsScreenSaverTimeout()
  {
    _defaults.Should().ContainKey("V-205906");
    _defaults["V-205906"].Value.Should().Be("900");
    _defaults["V-205906"].Category.Should().Be("Registry");
    _defaults["V-205906"].Description.Should().Contain("15 minutes");
  }

  // ── Service rules (V-205850, V-214936) ──────────────────────────────────────

  [Fact]
  public void Defaults_ContainsSecondaryLogonService()
  {
    _defaults.Should().ContainKey("V-205850");
    _defaults["V-205850"].Value.Should().Be("seclogon");
    _defaults["V-205850"].Category.Should().Be("Service");
  }

  [Fact]
  public void Defaults_ContainsBitsService()
  {
    _defaults.Should().ContainKey("V-214936");
    _defaults["V-214936"].Value.Should().Be("BITS");
    _defaults["V-214936"].Category.Should().Be("Service");
  }

  // ── OrgSettingDefault model ───────────────────────────────────────────────────

  [Fact]
  public void OrgSettingDefault_ConstructorSetsAllProperties()
  {
    var entry = new OrgSettingDefault("myValue", "A description", "MyCategory");
    entry.Value.Should().Be("myValue");
    entry.Description.Should().Be("A description");
    entry.Category.Should().Be("MyCategory");
  }

  [Fact]
  public void AllDefaults_HaveNonEmptyValueDescriptionAndCategory()
  {
    foreach (var kvp in _defaults)
    {
      kvp.Value.Value.Should().NotBeNullOrWhiteSpace(
        because: $"Vuln ID '{kvp.Key}' must have a non-empty value");
      kvp.Value.Description.Should().NotBeNullOrWhiteSpace(
        because: $"Vuln ID '{kvp.Key}' must have a non-empty description");
      kvp.Value.Category.Should().NotBeNullOrWhiteSpace(
        because: $"Vuln ID '{kvp.Key}' must have a non-empty category");
    }
  }

  [Fact]
  public void AllDefaults_CategoryIsOneOfKnownValues()
  {
    var knownCategories = new[] { "Certificate", "Security Option", "Registry", "Service" };
    foreach (var kvp in _defaults)
    {
      kvp.Value.Category.Should().BeOneOf(knownCategories,
        because: $"Vuln ID '{kvp.Key}' should belong to a known STIG category");
    }
  }

  [Fact]
  public void GetDefaults_ContainsExpectedTotalCount()
  {
    // 4 (V-205648.x) + 1 (V-205649) + 2 (V-205650.x) + 2 (V-20590x) + 1 (V-205906) + 2 (services) = 12
    _defaults.Count.Should().Be(12);
  }
}
