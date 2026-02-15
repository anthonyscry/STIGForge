using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public sealed class PackFormatClassifierTests
{
  [Theory]
  [InlineData("admx_template_import", "Windows 11 baseline", true)]
  [InlineData("gpo_lgpo_import/ADMX", "ADMX Templates - OneDrive", true)]
  [InlineData("gpo_lgpo_import/LocalPolicy/ADMX", "Ambiguous import marker", false)]
  [InlineData("gpo_lgpo_import/LocalPolicy", "Local Policy - DoD Windows 11", false)]
  [InlineData("gpo_lgpo_import", "ADMX Templates - Microsoft", true)]
  [InlineData("gpo_lgpo_import", "STIG GPO Package January 2026", false)]
  [InlineData("admx_import", "STIG GPO Package January 2026", false)]
  [InlineData("admx_import", "Firefox Policy Templates", true)]
  [InlineData(null, "ADMX Templates - Browser", true)]
  [InlineData(null, "STIG GPO Package January 2026", false)]
  [InlineData("", "", false)]
  public void IsAdmxTemplatePack_UsesSourceLabelAndNameHints(string? sourceLabel, string? name, bool expected)
  {
    var actual = PackFormatClassifier.IsAdmxTemplatePack(sourceLabel, name);

    Assert.Equal(expected, actual);
  }
}
