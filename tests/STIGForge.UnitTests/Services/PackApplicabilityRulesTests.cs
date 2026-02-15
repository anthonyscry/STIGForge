using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Services;

public class PackApplicabilityRulesTests
{
  [Fact]
  public void IsApplicable_Excludes_Server_LocalPolicy_On_Win11_Workstation()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "Local Policy - DoD Windows Server 2022 MS v2r7",
      SourceLabel = "gpo_lgpo_import",
      Format = "GPO",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { ".NET Framework 4.x", "Google Chrome" },
      ControlOsTargets = new[] { OsTarget.Win11 }
    };

    PackApplicabilityRules.IsApplicable(input).Should().BeFalse();
  }

  [Fact]
  public void IsApplicable_Includes_Win11_LocalPolicy_On_Win11_Workstation()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "Local Policy - DoD Windows 11 v2r6",
      SourceLabel = "gpo_lgpo_import",
      Format = "GPO",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { ".NET Framework 4.x" },
      ControlOsTargets = Array.Empty<OsTarget>()
    };

    PackApplicabilityRules.IsApplicable(input).Should().BeTrue();
  }

  [Fact]
  public void IsApplicable_AdmxOffice_Requires_Office_Feature()
  {
    var withoutOffice = new PackApplicabilityInput
    {
      PackName = "ADMX Templates - Office 2016-2019-M365",
      SourceLabel = "admx_import",
      Format = "ADMX",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { ".NET Framework 4.x", "Google Chrome" },
      ControlOsTargets = Array.Empty<OsTarget>()
    };

    var withOffice = new PackApplicabilityInput
    {
      PackName = withoutOffice.PackName,
      SourceLabel = withoutOffice.SourceLabel,
      Format = withoutOffice.Format,
      MachineOs = withoutOffice.MachineOs,
      MachineRole = withoutOffice.MachineRole,
      InstalledFeatures = new[] { ".NET Framework 4.x", "Google Chrome", "Microsoft Office" },
      ControlOsTargets = Array.Empty<OsTarget>()
    };

    PackApplicabilityRules.IsApplicable(withoutOffice).Should().BeFalse();
    PackApplicabilityRules.IsApplicable(withOffice).Should().BeTrue();
  }

  [Fact]
  public void IsApplicable_AdmxGoogle_Matches_When_Chrome_Installed()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "ADMX Templates - Google",
      SourceLabel = "admx_import",
      Format = "ADMX",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { "Google Chrome" },
      ControlOsTargets = Array.Empty<OsTarget>()
    };

    PackApplicabilityRules.IsApplicable(input).Should().BeTrue();
  }

  [Fact]
  public void IsApplicable_Android_Stig_Is_Excluded_On_Windows_Hosts()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "Google Android STIG V2R1",
      SourceLabel = "stig_import",
      Format = "STIG",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { "Google Chrome" },
      ControlOsTargets = Array.Empty<OsTarget>()
    };

    PackApplicabilityRules.IsApplicable(input).Should().BeFalse();
  }

  [Fact]
  public void IsApplicable_AdmxOneDrive_Excluded_When_OneDrive_Not_Installed()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "ADMX Templates - OneDrive",
      SourceLabel = "admx_import",
      Format = "ADMX",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { ".NET Framework 4.x", "Google Chrome" },
      ControlOsTargets = Array.Empty<OsTarget>()
    };

    PackApplicabilityRules.IsApplicable(input).Should().BeFalse();
  }

  [Fact]
  public void Evaluate_FortiGate_WithoutStrongSignals_ReturnsUnknown()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "FortiGate Firewall STIG V1R1",
      SourceLabel = "stig_import",
      Format = "STIG",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { "Windows Defender" },
      ControlOsTargets = new[] { OsTarget.Win11 },
      HostSignals = Array.Empty<string>()
    };

    var decision = PackApplicabilityRules.Evaluate(input);

    decision.State.Should().Be(ApplicabilityState.Unknown);
    decision.Confidence.Should().Be(ApplicabilityConfidence.Low);
    decision.ReasonCode.Should().Be("fortigate_signal_missing");
  }

  [Fact]
  public void Evaluate_FortiGate_WithServiceSignal_ReturnsApplicableHigh()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "FortiGate Firewall STIG V1R1",
      SourceLabel = "stig_import",
      Format = "STIG",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { "Windows Defender" },
      ControlOsTargets = new[] { OsTarget.Win11 },
      HostSignals = new[] { "service:FortiClient Service" }
    };

    var decision = PackApplicabilityRules.Evaluate(input);

    decision.State.Should().Be(ApplicabilityState.Applicable);
    decision.Confidence.Should().Be(ApplicabilityConfidence.High);
    decision.ReasonCode.Should().Be("fortigate_signal_match");
  }

  [Fact]
  public void Evaluate_SymantecEndpoint_WithoutStrongSignals_ReturnsUnknown()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "Symantec Endpoint Protection STIG V2R1",
      SourceLabel = "stig_import",
      Format = "STIG",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { "Windows Defender" },
      ControlOsTargets = new[] { OsTarget.Win11 },
      HostSignals = Array.Empty<string>()
    };

    var decision = PackApplicabilityRules.Evaluate(input);

    decision.State.Should().Be(ApplicabilityState.Unknown);
    decision.ReasonCode.Should().Be("symantec_signal_missing");
  }

  [Fact]
  public void IsApplicable_CompatibilityShim_ReturnsFalse_ForUnknown()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "Symantec Endpoint Protection STIG V2R1",
      SourceLabel = "stig_import",
      Format = "STIG",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { "Windows Defender" },
      ControlOsTargets = new[] { OsTarget.Win11 },
      HostSignals = Array.Empty<string>()
    };

    PackApplicabilityRules.IsApplicable(input).Should().BeFalse();
  }

  [Fact]
  public void Evaluate_FirewallDeviceSrg_OnWorkstation_ReturnsNotApplicable()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "Firewall Device SRG V3R1",
      SourceLabel = "stig_import",
      Format = "STIG",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { "Windows Defender" },
      ControlOsTargets = Array.Empty<OsTarget>(),
      HostSignals = Array.Empty<string>()
    };

    var decision = PackApplicabilityRules.Evaluate(input);

    decision.State.Should().Be(ApplicabilityState.NotApplicable);
    decision.Confidence.Should().Be(ApplicabilityConfidence.High);
    decision.ReasonCode.Should().Be("firewall_device_scope_mismatch");
  }

  [Fact]
  public void Evaluate_SymEdgeDspFrame_WithoutSignals_ReturnsUnknown()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "Sym Edge DSP Frame STIG V1R1",
      SourceLabel = "stig_import",
      Format = "STIG",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = new[] { "Windows Defender" },
      ControlOsTargets = Array.Empty<OsTarget>(),
      HostSignals = Array.Empty<string>()
    };

    var decision = PackApplicabilityRules.Evaluate(input);

    decision.State.Should().Be(ApplicabilityState.Unknown);
    decision.Confidence.Should().Be(ApplicabilityConfidence.Low);
    decision.ReasonCode.Should().Be("sym_edge_ambiguous");
  }

  [Fact]
  public void Evaluate_AdmxMicrosoft_OnWin11_ReturnsApplicableHigh()
  {
    var input = new PackApplicabilityInput
    {
      PackName = "ADMX Templates - Microsoft",
      SourceLabel = "admx_template_import",
      Format = "ADMX",
      MachineOs = OsTarget.Win11,
      MachineRole = RoleTemplate.Workstation,
      InstalledFeatures = Array.Empty<string>(),
      ControlOsTargets = Array.Empty<OsTarget>(),
      HostSignals = Array.Empty<string>()
    };

    var decision = PackApplicabilityRules.Evaluate(input);

    decision.State.Should().Be(ApplicabilityState.Applicable);
    decision.Confidence.Should().Be(ApplicabilityConfidence.High);
    decision.ReasonCode.Should().Be("admx_microsoft_windows");
  }
}
