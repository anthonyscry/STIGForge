using STIGForge.Apply.PowerStig;
using STIGForge.Core.Models;
using Xunit;

namespace STIGForge.UnitTests.Apply;

public sealed class PowerStigDataWriterTests
{
  [Fact]
  public void Write_CreatesValidPsd1File()
  {
    // Arrange
    var data = new PowerStigData
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
        }
      }
    };

    var outputPath = Path.Combine(Path.GetTempPath(), "test.psd1");

    // Act
    PowerStigDataWriter.Write(outputPath, data);

    // Assert
    Assert.True(File.Exists(outputPath));
    var content = File.ReadAllText(outputPath);
    Assert.Contains("@{", content);
    Assert.Contains("StigVersion = \"1.0\"", content);
    Assert.Contains("MaxPasswordAge\"", content);
  }

  [Fact]
  public void Write_EscapesSpecialCharacters()
  {
    // Arrange
    var data = new PowerStigData
    {
      StigVersion = "1.0",
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>
      {
        { "OrganizationName", "Test\"Quote\"Org" },
        { "ApplyProfile", "TestProfile" }
      },
      RuleSettings = new List<PowerStigRuleSetting>
      {
        new PowerStigRuleSetting
        {
          RuleId = "SV-123456r1_rule",
          SettingName = "Test\"Value\"",
          Value = "Back\\Slash$Test"
        }
      }
    };

    var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".psd1");

    // Act
    PowerStigDataWriter.Write(outputPath, data);

    // Assert
    var content = File.ReadAllText(outputPath);
    // Quotes should be escaped with backticks
    Assert.Contains("Test`\"Quote`\"Org", content);
    // Value should include escaped backslash path fragment and preserved dollar sign
    Assert.Contains("Back", content);
    Assert.Contains("Slash$Test", content);
  }

  [Fact]
  public void Write_HandlesNullValues()
  {
    // Arrange
    var data = new PowerStigData
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
          SettingName = null,
          Value = null
        }
      }
    };

    var outputPath = Path.Combine(Path.GetTempPath(), "test.psd1");

    // Act
    PowerStigDataWriter.Write(outputPath, data);

    // Assert
    var content = File.ReadAllText(outputPath);
    Assert.Contains("SettingName = $null", content);
    Assert.Contains("Value = $null", content);
  }

  [Fact]
  public void Write_ThrowsOnInvalidData()
  {
    // Arrange
    var invalidData = new PowerStigData
    {
      StigVersion = null, // Missing required field
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>(),
      RuleSettings = new List<PowerStigRuleSetting>()
    };

    var outputPath = Path.Combine(Path.GetTempPath(), "test.psd1");

    // Act & Assert
    var exception = Assert.Throws<ValidationException>(() => PowerStigDataWriter.Write(outputPath, invalidData));
    Assert.Contains("StigVersion", exception.Message);
  }

  [Fact]
  public void Write_ThrowsOnMissingGlobalSettings()
  {
    // Arrange
    var invalidData = new PowerStigData
    {
      StigVersion = "1.0",
      StigRelease = "R1",
      GlobalSettings = new Dictionary<string, string>(), // Missing required fields
      RuleSettings = new List<PowerStigRuleSetting>()
    };

    var outputPath = Path.Combine(Path.GetTempPath(), "test.psd1");

    // Act & Assert
    var exception = Assert.Throws<ValidationException>(() => PowerStigDataWriter.Write(outputPath, invalidData));
    Assert.Contains("GlobalSettings", exception.Message);
  }
}
