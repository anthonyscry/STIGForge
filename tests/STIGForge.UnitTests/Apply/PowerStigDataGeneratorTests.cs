using STIGForge.Apply.PowerStig;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Apply;

public sealed class PowerStigDataGeneratorTests
{
  [Fact]
  public void CreateDefault_ReturnsValidStructure()
  {
    // Arrange & Act
    var data = PowerStigDataGenerator.CreateDefault(string.Empty, string.Empty);

    // Assert
    Assert.NotNull(data);
    Assert.NotNull(data.GlobalSettings);
    Assert.Contains("OrganizationName", data.GlobalSettings.Keys);
    Assert.Equal("STIGForge", data.GlobalSettings["OrganizationName"]);
    Assert.Contains("ApplyProfile", data.GlobalSettings.Keys);
    Assert.Equal("Baseline", data.GlobalSettings["ApplyProfile"]);
    Assert.NotNull(data.RuleSettings);
    Assert.Empty(data.RuleSettings);
  }

  [Fact]
  public void CreateFromControls_MapsRuleIds()
  {
    // Arrange
    var controls = new List<ControlRecord>
    {
      new ControlRecord
      {
        ControlId = "V-123456",
        ExternalIds = new ExternalIds { RuleId = "SV-123456r1_rule" }
      },
      new ControlRecord
      {
        ControlId = "V-789012",
        ExternalIds = new ExternalIds { RuleId = "SV-789012r2_rule" }
      },
      new ControlRecord
      {
        ControlId = "V-no-rule",
        ExternalIds = new ExternalIds { RuleId = null } // No rule ID
      }
    };

    // Act
    var data = PowerStigDataGenerator.CreateFromControls(controls, null);

    // Assert
    Assert.Equal(2, data.RuleSettings.Count);
    Assert.Equal("SV-123456r1_rule", data.RuleSettings[0].RuleId);
    Assert.Equal("SV-789012r2_rule", data.RuleSettings[1].RuleId);
  }

  [Fact]
  public void CreateFromControls_AppliesOverrides()
  {
    // Arrange
    var controls = new List<ControlRecord>
    {
      new ControlRecord
      {
        ControlId = "V-123456",
        ExternalIds = new ExternalIds { RuleId = "SV-123456r1_rule" }
      }
    };

    var overrides = new List<PowerStigOverride>
    {
      new PowerStigOverride
      {
        RuleId = "SV-123456r1_rule",
        SettingName = "MaxPasswordAge",
        Value = "60"
      },
      new PowerStigOverride
      {
        RuleId = "SV-NONEXISTENT_rule", // Non-existent rule
        SettingName = "OtherSetting",
        Value = "value"
      }
    };

    // Act
    var data = PowerStigDataGenerator.CreateFromControls(controls, overrides);

    // Assert
    Assert.Single(data.RuleSettings);
    var setting = data.RuleSettings[0];
    Assert.Equal("SV-123456r1_rule", setting.RuleId);
    Assert.Equal("MaxPasswordAge", setting.SettingName);
    Assert.Equal("60", setting.Value);
  }

  [Fact]
  public void CreateFromControls_FiltersNewRules()
  {
    // Arrange
    var clock = new STIGForge.Core.Services.SystemClock();
    var releaseAgeGate = new STIGForge.Core.Services.ReleaseAgeGate(clock);
    PowerStigDataGenerator.Initialize(releaseAgeGate, new STIGForge.Core.Services.ClassificationScopeService());

    var controls = new List<ControlRecord>
    {
      new ControlRecord
      {
        ControlId = "V-123456",
        ExternalIds = new ExternalIds { RuleId = "SV-123456r1_rule" },
        Revision = new RevisionInfo { BenchmarkDate = clock.Now.AddDays(-5) } // Recent (within 30 days)
      },
      new ControlRecord
      {
        ControlId = "V-789012",
        ExternalIds = new ExternalIds { RuleId = "SV-789012r2_rule" },
        Revision = new RevisionInfo { BenchmarkDate = clock.Now.AddDays(-40) } // Mature (beyond 30 days)
      }
    };

    var profile = new STIGForge.Core.Models.Profile
    {
      AutomationPolicy = new STIGForge.Core.Models.AutomationPolicy { NewRuleGraceDays = 30 }
    };

    // Act
    var data = PowerStigDataGenerator.CreateFromControls(controls, null, profile);

    // Assert
    Assert.Single(data.RuleSettings);
    Assert.Equal("SV-789012r2_rule", data.RuleSettings[0].RuleId); // Only mature rule included
  }

  [Fact]
  public void CreateFromControls_FiltersClassificationScope()
  {
    // Arrange
    var clock = new STIGForge.Core.Services.SystemClock();
    var scopeService = new STIGForge.Core.Services.ClassificationScopeService();
    PowerStigDataGenerator.Initialize(new STIGForge.Core.Services.ReleaseAgeGate(clock), scopeService);

    var controls = new List<ControlRecord>
    {
      new ControlRecord
      {
        ControlId = "V-123456",
        ExternalIds = new ExternalIds { RuleId = "SV-123456r1_rule" },
        Applicability = new Applicability { ClassificationScope = STIGForge.Core.Models.ScopeTag.ClassifiedOnly }
      },
      new ControlRecord
      {
        ControlId = "V-789012",
        ExternalIds = new ExternalIds { RuleId = "SV-789012r2_rule" },
        Applicability = new Applicability { ClassificationScope = STIGForge.Core.Models.ScopeTag.UnclassifiedOnly }
      }
    };

    var profile = new STIGForge.Core.Models.Profile
    {
      ClassificationMode = STIGForge.Core.Models.ClassificationMode.Classified
    };

    // Act
    var data = PowerStigDataGenerator.CreateFromControls(controls, null, profile);

    // Assert
    Assert.Single(data.RuleSettings);
    Assert.Equal("SV-123456r1_rule", data.RuleSettings[0].RuleId); // Only Classified control included
  }

  [Fact]
  public void CreateFromControls_HandlesEmptyInput()
  {
    // Arrange
    var emptyControls = Array.Empty<ControlRecord>();
    var emptyOverrides = Array.Empty<PowerStigOverride>();

    // Act
    var data = PowerStigDataGenerator.CreateFromControls(emptyControls, emptyOverrides);

    // Assert
    Assert.NotNull(data);
    Assert.NotNull(data.RuleSettings);
    Assert.Empty(data.RuleSettings);
    Assert.NotNull(data.GlobalSettings);
  }

  [Fact]
  public void CreateFromControls_HandlesWhitespaceRuleIds()
  {
    // Arrange
    var controls = new List<ControlRecord>
    {
      new ControlRecord
      {
        ControlId = "V-123456",
        ExternalIds = new ExternalIds { RuleId = "  SV-123456r1_rule  " } // With whitespace
      },
      new ControlRecord
      {
        ControlId = "V-789012",
        ExternalIds = new ExternalIds { RuleId = "" } // Empty string
      },
      new ControlRecord
      {
        ControlId = "V-no-externalids",
        ExternalIds = null! // Intentionally null to test null-safety
      }
    };

    // Act
    var data = PowerStigDataGenerator.CreateFromControls(controls, null);

    // Assert
    Assert.Single(data.RuleSettings);
    Assert.Equal("SV-123456r1_rule", data.RuleSettings[0].RuleId);
  }
}
