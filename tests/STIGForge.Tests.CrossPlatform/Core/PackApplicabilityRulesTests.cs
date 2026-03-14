using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Tests.CrossPlatform.Core;

public sealed class PackApplicabilityRulesTests
{
    // ── Null guard ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_NullInput_ThrowsArgumentNullException()
    {
        var act = () => PackApplicabilityRules.Evaluate(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("input");
    }

    [Fact]
    public void IsApplicable_NullInput_ThrowsArgumentNullException()
    {
        var act = () => PackApplicabilityRules.IsApplicable(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("input");
    }

    // ── Android on Windows ────────────────────────────────────────────────────

    [Theory]
    [InlineData(OsTarget.Win11)]
    [InlineData(OsTarget.Win10)]
    [InlineData(OsTarget.Server2022)]
    [InlineData(OsTarget.Server2019)]
    public void Evaluate_AndroidPackOnWindowsHost_ReturnsNotApplicable(OsTarget os)
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Android 12 STIG",
            MachineOs = os
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.NotApplicable);
        result.Confidence.Should().Be(ApplicabilityConfidence.High);
        result.ReasonCode.Should().Be("android_on_windows");
    }

    [Fact]
    public void Evaluate_AndroidPackOnUnknownOs_DoesNotReturnAndroidOnWindows()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Android 12 STIG",
            MachineOs = OsTarget.Unknown
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.ReasonCode.Should().NotBe("android_on_windows");
    }

    // ── Firewall device scope on Windows ─────────────────────────────────────

    [Fact]
    public void Evaluate_FirewallDeviceScopePackOnWindows_ReturnsNotApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Network Firewall SRG",
            MachineOs = OsTarget.Win11
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.NotApplicable);
        result.Confidence.Should().Be(ApplicabilityConfidence.High);
        result.ReasonCode.Should().Be("firewall_device_scope_mismatch");
    }

    [Fact]
    public void Evaluate_WindowsDefenderFirewallPackOnWindows_DoesNotMatchDeviceScope()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Windows Firewall STIG",
            MachineOs = OsTarget.Win10
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.ReasonCode.Should().NotBe("firewall_device_scope_mismatch");
    }

    // ── Sym Edge DSP ──────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_SymEdgeDspPackOnWindows_ReturnsUnknownLowConfidence()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Sym Edge DSP Security Baseline",
            MachineOs = OsTarget.Win11
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Unknown);
        result.Confidence.Should().Be(ApplicabilityConfidence.Low);
        result.ReasonCode.Should().Be("sym_edge_ambiguous");
    }

    // ── Vendor-specific: FortiGate ────────────────────────────────────────────

    [Fact]
    public void Evaluate_FortiGatePackWithMatchingHostSignal_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "FortiGate Firewall STIG",
            MachineOs = OsTarget.Unknown,
            HostSignals = new[] { "fortigate-service" }
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.Confidence.Should().Be(ApplicabilityConfidence.High);
        result.ReasonCode.Should().Be("fortigate_signal_match");
    }

    [Fact]
    public void Evaluate_FortiGatePackWithoutHostSignal_ReturnsUnknown()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "FortiGate Firewall STIG",
            MachineOs = OsTarget.Unknown,
            HostSignals = []
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Unknown);
        result.Confidence.Should().Be(ApplicabilityConfidence.Low);
        result.ReasonCode.Should().Be("fortigate_signal_missing");
    }

    // ── Vendor-specific: Symantec ─────────────────────────────────────────────

    [Fact]
    public void Evaluate_SymantecPackWithMatchingSignal_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Symantec Endpoint Protection STIG",
            MachineOs = OsTarget.Unknown,
            HostSignals = new[] { "symantec-service-running" }
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("symantec_signal_match");
    }

    [Fact]
    public void Evaluate_SymantecPackWithoutSignal_ReturnsUnknown()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Symantec Endpoint Protection STIG",
            MachineOs = OsTarget.Unknown,
            HostSignals = []
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Unknown);
        result.ReasonCode.Should().Be("symantec_signal_missing");
    }

    // ── OS mismatch ───────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_Win11PackOnWin10Host_ReturnsNotApplicable_OsMismatch()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Windows 11 STIG",
            MachineOs = OsTarget.Win10
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.NotApplicable);
        result.ReasonCode.Should().Be("os_mismatch");
    }

    [Fact]
    public void Evaluate_Win11PackOnWin11Host_ReturnsApplicable_OsTagMatch()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Windows 11 STIG",
            MachineOs = OsTarget.Win11
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("os_tag_match");
    }

    // ── ADMX format ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OsTarget.Win11)]
    [InlineData(OsTarget.Win10)]
    public void Evaluate_GenericMicrosoftAdmxOnWindowsClient_ReturnsApplicable(OsTarget os)
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Microsoft ADMX Templates",
            Format = "ADMX",
            MachineOs = os
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("admx_microsoft_windows");
    }

    [Fact]
    public void Evaluate_AdmxWithMatchingFeatureTags_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "IIS 10 ADMX STIG",
            Format = "ADMX",
            MachineOs = OsTarget.Server2022,
            InstalledFeatures = new[] { "IIS-WebServer" }
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("admx_feature_match");
    }

    [Fact]
    public void Evaluate_AdmxWithNoFeatureOrOsMatch_ReturnsNotApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "IIS 10 ADMX STIG",
            Format = "ADMX",
            MachineOs = OsTarget.Win11,
            InstalledFeatures = []
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.NotApplicable);
        result.ReasonCode.Should().Be("admx_feature_mismatch");
    }

    [Fact]
    public void Evaluate_AdmxNoFeatureTags_OsMatchOnly_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Windows 10 ADMX Baseline",
            Format = "ADMX",
            MachineOs = OsTarget.Win10
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("admx_os_match");
    }

    [Fact]
    public void Evaluate_AdmxNoFeatureTagsAndNoOsMatch_ReturnsNotApplicable()
    {
        // Pack name has no Microsoft keyword → not generic Microsoft ADMX, no OS tags, no feature tags
        // → falls to admx_no_feature_match (no feature or OS match found)
        var input = new PackApplicabilityInput
        {
            PackName = "CIS ADMX Baseline",
            Format = "ADMX",
            MachineOs = OsTarget.Win10
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.NotApplicable);
        result.ReasonCode.Should().Be("admx_no_feature_match");
    }

    // ── Domain GPO ────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_DomainGpoPackOnDomainController_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Domain GPO Policy",
            Format = "GPO",
            MachineOs = OsTarget.Server2022,
            MachineRole = RoleTemplate.DomainController
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("domain_gpo_dc_role_match");
    }

    [Fact]
    public void Evaluate_DomainGpoPackOnMemberServer_ReturnsUnknown()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Domain GPO Policy",
            Format = "GPO",
            MachineOs = OsTarget.Server2022,
            MachineRole = RoleTemplate.MemberServer
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Unknown);
        result.Confidence.Should().Be(ApplicabilityConfidence.Low);
        result.ReasonCode.Should().Be("domain_gpo_requires_domain_context");
    }

    [Fact]
    public void Evaluate_DomainGpoBySourceLabel_ReturnsApplicableForDC()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Custom Policy",
            SourceLabel = "gpo_domain_import",
            Format = "GPO",
            MachineOs = OsTarget.Server2022,
            MachineRole = RoleTemplate.DomainController
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("domain_gpo_dc_role_match");
    }

    // ── Security baseline GPO ─────────────────────────────────────────────────

    [Fact]
    public void Evaluate_BaselineGpoOsCompatible_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Windows 11 Security Baseline",
            Format = "GPO",
            MachineOs = OsTarget.Win11
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("gpo_baseline_os_match");
    }

    [Fact]
    public void Evaluate_BaselineGpoNoOsTags_OsCompatible_ReturnsApplicable()
    {
        // "LGPO Baseline" has no OS tags → osCompatible = true (no OS tags required)
        var input = new PackApplicabilityInput
        {
            PackName = "LGPO Baseline",
            Format = "GPO",
            MachineOs = OsTarget.Win11
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("gpo_baseline_os_match");
    }

    [Fact]
    public void Evaluate_BaselineGpoWithFeatureMatch_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "IIS Local Policy Baseline",
            Format = "GPO",
            MachineOs = OsTarget.Server2022,
            InstalledFeatures = new[] { "IIS-WebServer" }
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("gpo_baseline_feature_match");
    }

    [Fact]
    public void Evaluate_BaselineGpoWithFeatureMismatch_ReturnsNotApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "DNS Local Policy Baseline",
            Format = "GPO",
            MachineOs = OsTarget.Server2022,
            InstalledFeatures = []
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.NotApplicable);
        result.ReasonCode.Should().Be("gpo_baseline_feature_mismatch");
    }

    // ── Control OS targets ────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_ControlTargetsIncludeMachineOs_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Some STIG Pack",
            MachineOs = OsTarget.Server2022,
            ControlOsTargets = new[] { OsTarget.Server2022, OsTarget.Server2019 }
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("control_target_match");
    }

    [Fact]
    public void Evaluate_ControlTargetsExcludeMachineOsWithOsTag_ReturnsNotApplicable()
    {
        // Server2019 pack name passes os_mismatch on Server2022 (generic "server" tag matches),
        // but ControlOsTargets=[Server2019] excludes the machine, so control_target_mismatch fires.
        var input = new PackApplicabilityInput
        {
            PackName = "Windows Server 2019 STIG",
            MachineOs = OsTarget.Server2022,
            ControlOsTargets = new[] { OsTarget.Server2019 }
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.NotApplicable);
        result.ReasonCode.Should().Be("control_target_mismatch");
    }

    // ── Feature tag matching ──────────────────────────────────────────────────

    [Fact]
    public void Evaluate_FeatureTagMatchOnIIS_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "IIS 10.0 STIG",
            MachineOs = OsTarget.Server2022,
            InstalledFeatures = new[] { "IIS-WebServer", "IIS-FTPServer" }
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("feature_tag_match");
    }

    [Fact]
    public void Evaluate_FeatureTagMismatch_ReturnsNotApplicable()
    {
        // "SQL STIG" extracts "sql" feature tag, but machine has no SQL installed
        var input = new PackApplicabilityInput
        {
            PackName = "SQL STIG",
            MachineOs = OsTarget.Win11,
            InstalledFeatures = []
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.NotApplicable);
        result.ReasonCode.Should().Be("feature_tag_mismatch");
    }

    // ── Role matching ─────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_DomainControllerRoleWithDCPackName_ReturnsApplicable()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Active Directory Domain Controller STIG",
            MachineOs = OsTarget.Server2022,
            MachineRole = RoleTemplate.DomainController
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("role_dc_match");
    }

    [Fact]
    public void Evaluate_MemberServerRoleWithMemberServerPackName_ReturnsApplicable()
    {
        // No OS tags in pack name so os_tag_match doesn't fire first;
        // role_member_server_match triggers on name containing "Member Server"
        var input = new PackApplicabilityInput
        {
            PackName = "Member Server STIG",
            MachineOs = OsTarget.Server2022,
            MachineRole = RoleTemplate.MemberServer
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Applicable);
        result.ReasonCode.Should().Be("role_member_server_match");
    }

    // ── Insufficient signal fallback ──────────────────────────────────────────

    [Fact]
    public void Evaluate_NoMatchingSignals_ReturnsUnknownInsufficientSignal()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Unknown Obscure STIG Pack",
            MachineOs = OsTarget.Unknown
        };

        var result = PackApplicabilityRules.Evaluate(input);

        result.State.Should().Be(ApplicabilityState.Unknown);
        result.Confidence.Should().Be(ApplicabilityConfidence.Low);
        result.ReasonCode.Should().Be("insufficient_signal");
    }

    // ── IsApplicable delegation ───────────────────────────────────────────────

    [Fact]
    public void IsApplicable_Win11PackOnWin11_ReturnsTrue()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Windows 11 STIG",
            MachineOs = OsTarget.Win11
        };

        PackApplicabilityRules.IsApplicable(input).Should().BeTrue();
    }

    [Fact]
    public void IsApplicable_AndroidPackOnWindows_ReturnsFalse()
    {
        var input = new PackApplicabilityInput
        {
            PackName = "Android 12 STIG",
            MachineOs = OsTarget.Win11
        };

        PackApplicabilityRules.IsApplicable(input).Should().BeFalse();
    }

    // ── IsFeatureTag ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("defender")]
    [InlineData("firewall")]
    [InlineData("edge")]
    [InlineData("dotnet")]
    [InlineData("iis")]
    [InlineData("sql")]
    [InlineData("dns")]
    [InlineData("dhcp")]
    [InlineData("chrome")]
    [InlineData("firefox")]
    [InlineData("adobe")]
    [InlineData("office")]
    [InlineData("onedrive")]
    [InlineData("android")]
    public void IsFeatureTag_KnownFeatureTags_ReturnsTrue(string tag)
    {
        PackApplicabilityRules.IsFeatureTag(tag).Should().BeTrue();
    }

    [Theory]
    [InlineData("win11")]
    [InlineData("server2022")]
    [InlineData("")]
    [InlineData(null)]
    public void IsFeatureTag_NonFeatureTags_ReturnsFalse(string? tag)
    {
        PackApplicabilityRules.IsFeatureTag(tag).Should().BeFalse();
    }

    // ── IsOsTag ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("win11")]
    [InlineData("win10")]
    [InlineData("server")]
    [InlineData("server2022")]
    [InlineData("server2019")]
    [InlineData("server2016")]
    [InlineData("server2012r2")]
    [InlineData("server2025")]
    public void IsOsTag_KnownOsTags_ReturnsTrue(string tag)
    {
        PackApplicabilityRules.IsOsTag(tag).Should().BeTrue();
    }

    [Theory]
    [InlineData("iis")]
    [InlineData("defender")]
    [InlineData("")]
    [InlineData(null)]
    public void IsOsTag_NonOsTags_ReturnsFalse(string? tag)
    {
        PackApplicabilityRules.IsOsTag(tag).Should().BeFalse();
    }

    // ── IsPackOsCompatible ────────────────────────────────────────────────────

    [Fact]
    public void IsPackOsCompatible_Win11TagOnWin11_ReturnsTrue()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "win11" };
        PackApplicabilityRules.IsPackOsCompatible(tags, OsTarget.Win11, requireOsTag: true).Should().BeTrue();
    }

    [Fact]
    public void IsPackOsCompatible_Win11TagOnWin10_ReturnsFalse()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "win11" };
        PackApplicabilityRules.IsPackOsCompatible(tags, OsTarget.Win10, requireOsTag: true).Should().BeFalse();
    }

    [Fact]
    public void IsPackOsCompatible_ServerTagOnServer2022_ReturnsTrue()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "server" };
        PackApplicabilityRules.IsPackOsCompatible(tags, OsTarget.Server2022, requireOsTag: true).Should().BeTrue();
    }

    [Fact]
    public void IsPackOsCompatible_NoOsTagWithRequireFalse_ReturnsTrue()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "iis" };
        PackApplicabilityRules.IsPackOsCompatible(tags, OsTarget.Win11, requireOsTag: false).Should().BeTrue();
    }

    [Fact]
    public void IsPackOsCompatible_NoOsTagWithRequireTrue_ReturnsFalse()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "iis" };
        PackApplicabilityRules.IsPackOsCompatible(tags, OsTarget.Win11, requireOsTag: true).Should().BeFalse();
    }

    [Fact]
    public void IsPackOsCompatible_NullTags_ThrowsArgumentNullException()
    {
        var act = () => PackApplicabilityRules.IsPackOsCompatible(null!, OsTarget.Win11, requireOsTag: true);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── IsScapFallbackTagCompatible ───────────────────────────────────────────

    [Fact]
    public void IsScapFallbackTagCompatible_OverlappingOsTags_ReturnsTrue()
    {
        var stigTags = new[] { "win11", "win10" };
        var scapTags = new[] { "win11", "server2022" };

        PackApplicabilityRules.IsScapFallbackTagCompatible(stigTags, scapTags).Should().BeTrue();
    }

    [Fact]
    public void IsScapFallbackTagCompatible_NonOverlappingOsTags_ReturnsFalse()
    {
        var stigTags = new[] { "win10" };
        var scapTags = new[] { "server2022" };

        PackApplicabilityRules.IsScapFallbackTagCompatible(stigTags, scapTags).Should().BeFalse();
    }

    [Fact]
    public void IsScapFallbackTagCompatible_EmptyStigt_ReturnsFalse()
    {
        PackApplicabilityRules.IsScapFallbackTagCompatible([], new[] { "win11" }).Should().BeFalse();
    }

    [Fact]
    public void IsScapFallbackTagCompatible_NullInputs_ReturnsFalse()
    {
        PackApplicabilityRules.IsScapFallbackTagCompatible(null, null).Should().BeFalse();
    }

    [Fact]
    public void IsScapFallbackTagCompatible_OverlappingFeatureTags_ReturnsTrue()
    {
        var stigTags = new[] { "win11", "iis" };
        var scapTags = new[] { "win11", "iis" };

        PackApplicabilityRules.IsScapFallbackTagCompatible(stigTags, scapTags).Should().BeTrue();
    }

    [Fact]
    public void IsScapFallbackTagCompatible_ScapHasFeatureTags_StigDoesNot_ReturnsFalse()
    {
        var stigTags = new[] { "win11" };
        var scapTags = new[] { "win11", "iis" };

        PackApplicabilityRules.IsScapFallbackTagCompatible(stigTags, scapTags).Should().BeFalse();
    }

    // ── GetMachineFeatureTags ─────────────────────────────────────────────────

    [Fact]
    public void GetMachineFeatureTags_NullInstalledFeatures_ReturnsDefaults()
    {
        var tags = PackApplicabilityRules.GetMachineFeatureTags(null);

        tags.Should().Contain("defender");
        tags.Should().Contain("firewall");
        tags.Should().Contain("edge");
        tags.Should().Contain("dotnet");
    }

    [Fact]
    public void GetMachineFeatureTags_WithIisFeature_IncludesIisTag()
    {
        var tags = PackApplicabilityRules.GetMachineFeatureTags(new[] { "IIS-WebServer" });
        tags.Should().Contain("iis");
    }

    [Fact]
    public void GetMachineFeatureTags_WithSqlFeature_IncludesSqlTag()
    {
        var tags = PackApplicabilityRules.GetMachineFeatureTags(new[] { "SQL Server 2019" });
        tags.Should().Contain("sql");
    }

    [Fact]
    public void GetMachineFeatureTags_WithGoogleChrome_IncludesChromeTag()
    {
        var tags = PackApplicabilityRules.GetMachineFeatureTags(new[] { "Google Chrome" });
        tags.Should().Contain("chrome");
    }

    [Fact]
    public void GetMachineFeatureTags_WithMozillaFirefox_IncludesFirefoxTag()
    {
        var tags = PackApplicabilityRules.GetMachineFeatureTags(new[] { "Mozilla Firefox" });
        tags.Should().Contain("firefox");
    }

    // ── ExtractMatchingTags ───────────────────────────────────────────────────

    [Fact]
    public void ExtractMatchingTags_Win11InText_YieldsWin11Tag()
    {
        var tags = PackApplicabilityRules.ExtractMatchingTags("Windows 11 STIG V1R1").ToList();
        tags.Should().Contain("win11");
    }

    [Fact]
    public void ExtractMatchingTags_ServerAndSpecificVersion_YieldsBothTags()
    {
        var tags = PackApplicabilityRules.ExtractMatchingTags("Windows Server 2022 STIG").ToList();
        tags.Should().Contain("server");
        tags.Should().Contain("server2022");
    }

    [Fact]
    public void ExtractMatchingTags_DefenderFirewall_YieldsFirewallTag()
    {
        var tags = PackApplicabilityRules.ExtractMatchingTags("Microsoft Defender Firewall STIG").ToList();
        tags.Should().Contain("firewall");
    }

    [Fact]
    public void ExtractMatchingTags_EmptyText_YieldsNoTags()
    {
        var tags = PackApplicabilityRules.ExtractMatchingTags(string.Empty).ToList();
        tags.Should().BeEmpty();
    }

    [Fact]
    public void ExtractMatchingTags_NullText_YieldsNoTags()
    {
        var tags = PackApplicabilityRules.ExtractMatchingTags(null).ToList();
        tags.Should().BeEmpty();
    }
}
