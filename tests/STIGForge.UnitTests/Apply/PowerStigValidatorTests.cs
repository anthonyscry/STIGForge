using STIGForge.Apply.PowerStig;
using Xunit;

namespace STIGForge.UnitTests.Apply;

public sealed class PowerStigValidatorTests
{
  [Fact]
  public void Validate_AcceptsValidData()
  {
    // Arrange
    var validData = new PowerStigData
    {
      StigVersion = "1.0",
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>
      {
        { "OrganizationName", "TestOrg" },
        { "ApplyProfile", "TestProfile" }
      },
      RuleSettings = new List<PowerStigRuleSetting>
      {
        new PowerStigRuleSetting
        {
          RuleId = "SV-123456r1_rule",
          SettingName = "MaxPasswordAge",
          Value = "60"
        },
        new PowerStigRuleSetting
        {
          RuleId = "SV-789012r2_rule",
          SettingName = null,
          Value = null
        }
      }
    };

    // Act
    var result = PowerStigValidator.Validate(validData);

    // Assert
    Assert.True(result.IsValid);
    Assert.Empty(result.Errors);
  }

  [Fact]
  public void Validate_RejectsMissingStigVersion()
  {
    // Arrange
    var invalidData = new PowerStigData
    {
      StigVersion = null, // Missing required field
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>
      {
        { "OrganizationName", "TestOrg" },
        { "ApplyProfile", "TestProfile" }
      },
      RuleSettings = new List<PowerStigRuleSetting>()
    };

    // Act
    var result = PowerStigValidator.Validate(invalidData);

    // Assert
    Assert.False(result.IsValid);
    Assert.Single(result.Errors);
    Assert.Contains("StigVersion", result.Errors[0]);
  }

  [Fact]
  public void Validate_RejectsInvalidRuleId()
  {
    // Arrange
    var invalidData = new PowerStigData
    {
      StigVersion = "1.0",
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>
      {
        { "OrganizationName", "TestOrg" },
        { "ApplyProfile", "TestProfile" }
      },
      RuleSettings = new List<PowerStigRuleSetting>
      {
        new PowerStigRuleSetting
        {
          RuleId = "INVALID-RULE-ID", // Doesn't match SV-<digits>r<digits>_rule pattern
          SettingName = "MaxPasswordAge",
          Value = "60"
        }
      }
    };

    // Act
    var result = PowerStigValidator.Validate(invalidData);

    // Assert
    Assert.False(result.IsValid);
    Assert.Single(result.Errors);
    Assert.Contains("RuleId 'INVALID-RULE-ID' does not match", result.Errors[0]);
  }

  [Fact]
  public void Validate_RejectsMissingGlobalSettings()
  {
    // Arrange
    var invalidData = new PowerStigData
    {
      StigVersion = "1.0",
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>(), // Missing required fields
      RuleSettings = new List<PowerStigRuleSetting>()
    };

    // Act
    var result = PowerStigValidator.Validate(invalidData);

    // Assert
    Assert.False(result.IsValid);
    Assert.Single(result.Errors);
    Assert.Contains("GlobalSettings", result.Errors[0]);
  }

  [Fact]
  public void Validate_RejectsMissingOrganizationName()
  {
    // Arrange
    var invalidData = new PowerStigData
    {
      StigVersion = "1.0",
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>
      {
        { "ApplyProfile", "TestProfile" } // Missing OrganizationName
      },
      RuleSettings = new List<PowerStigRuleSetting>()
    };

    // Act
    var result = PowerStigValidator.Validate(invalidData);

    // Assert
    Assert.False(result.IsValid);
    Assert.Single(result.Errors);
    Assert.Contains("OrganizationName", result.Errors[0]);
  }

  [Fact]
  public void Validate_RejectsNullRuleSettings()
  {
    // Arrange
    var invalidData = new PowerStigData
    {
      StigVersion = "1.0",
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>
      {
        { "OrganizationName", "TestOrg" },
        { "ApplyProfile", "TestProfile" }
      },
      RuleSettings = null // Null instead of empty list
    };

    // Act
    var result = PowerStigValidator.Validate(invalidData);

    // Assert
    Assert.False(result.IsValid);
    Assert.Single(result.Errors);
    Assert.Contains("RuleSettings", result.Errors[0]);
  }

  [Fact]
  public void Validate_AcceptsNullRuleIdInSettings()
  {
    // Arrange
    var validData = new PowerStigData
    {
      StigVersion = "1.0",
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>
      {
        { "OrganizationName", "TestOrg" },
        { "ApplyProfile", "TestProfile" }
      },
      RuleSettings = new List<PowerStigRuleSetting>() // Empty list is valid
    };

    // Act
    var result = PowerStigValidator.Validate(validData);

    // Assert
    Assert.True(result.IsValid);
    Assert.Empty(result.Errors);
  }
}
