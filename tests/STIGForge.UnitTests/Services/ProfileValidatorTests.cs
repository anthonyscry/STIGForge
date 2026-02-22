using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public class ProfileValidatorTests
{
  private static Profile MakeValidProfile()
  {
    return new Profile
    {
      ProfileId = "test-profile-01",
      Name = "Test Profile",
      OsTarget = OsTarget.Win11,
      RoleTemplate = RoleTemplate.Workstation,
      HardeningMode = HardeningMode.Safe,
      ClassificationMode = ClassificationMode.Classified,
      NaPolicy = new NaPolicy
      {
        AutoNaOutOfScope = true,
        ConfidenceThreshold = Confidence.High,
        DefaultNaCommentTemplate = "Auto-NA"
      },
      AutomationPolicy = new AutomationPolicy
      {
        Mode = AutomationMode.Standard,
        NewRuleGraceDays = 30,
        AutoApplyRequiresMapping = true,
        ReleaseDateSource = ReleaseDateSource.ContentPack
      },
      OverlayIds = Array.Empty<string>()
    };
  }

  [Fact]
  public void ValidProfile_Passes()
  {
    var validator = new ProfileValidator();
    var result = validator.Validate(MakeValidProfile());

    result.IsValid.Should().BeTrue();
    result.Errors.Should().BeEmpty();
  }

  [Fact]
  public void NullProfile_Fails()
  {
    var validator = new ProfileValidator();
    var result = validator.Validate(null!);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("null"));
  }

  [Fact]
  public void EmptyProfileId_Fails()
  {
    var validator = new ProfileValidator();
    var profile = MakeValidProfile();
    profile.ProfileId = "";

    var result = validator.Validate(profile);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("ProfileId"));
  }

  [Fact]
  public void EmptyName_Fails()
  {
    var validator = new ProfileValidator();
    var profile = MakeValidProfile();
    profile.Name = "";

    var result = validator.Validate(profile);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("Name"));
  }

  [Fact]
  public void NullNaPolicy_Fails()
  {
    var validator = new ProfileValidator();
    var profile = MakeValidProfile();
    profile.NaPolicy = null!;

    var result = validator.Validate(profile);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("NaPolicy"));
  }

  [Fact]
  public void NullAutomationPolicy_Fails()
  {
    var validator = new ProfileValidator();
    var profile = MakeValidProfile();
    profile.AutomationPolicy = null!;

    var result = validator.Validate(profile);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("AutomationPolicy"));
  }

  [Fact]
  public void NegativeGraceDays_Fails()
  {
    var validator = new ProfileValidator();
    var profile = MakeValidProfile();
    profile.AutomationPolicy.NewRuleGraceDays = -5;

    var result = validator.Validate(profile);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("NewRuleGraceDays"));
  }

  [Fact]
  public void NullOverlayIdEntry_Fails()
  {
    var validator = new ProfileValidator();
    var profile = MakeValidProfile();
    profile.OverlayIds = new[] { "valid-overlay", null!, "another-valid" };

    var result = validator.Validate(profile);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().Contain(e => e.Contains("OverlayIds[1]"));
  }

  [Fact]
  public void MultipleErrors_AllAccumulated()
  {
    var validator = new ProfileValidator();
    var profile = MakeValidProfile();
    profile.ProfileId = "";
    profile.Name = "";
    profile.NaPolicy = null!;
    profile.AutomationPolicy = null!;

    var result = validator.Validate(profile);

    result.IsValid.Should().BeFalse();
    result.Errors.Should().HaveCountGreaterThanOrEqualTo(4);
    result.Errors.Should().Contain(e => e.Contains("ProfileId"));
    result.Errors.Should().Contain(e => e.Contains("Name"));
    result.Errors.Should().Contain(e => e.Contains("NaPolicy"));
    result.Errors.Should().Contain(e => e.Contains("AutomationPolicy"));
  }

  [Fact]
  public void ZeroGraceDays_Passes()
  {
    var validator = new ProfileValidator();
    var profile = MakeValidProfile();
    profile.AutomationPolicy.NewRuleGraceDays = 0;

    var result = validator.Validate(profile);

    result.IsValid.Should().BeTrue();
  }
}
