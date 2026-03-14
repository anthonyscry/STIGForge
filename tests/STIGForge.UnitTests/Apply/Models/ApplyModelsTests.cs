using FluentAssertions;
using STIGForge.Apply;
using STIGForge.Apply.Dsc;
using STIGForge.Apply.PowerStig;
using STIGForge.Apply.Security;
using STIGForge.Apply.Snapshot;

namespace STIGForge.UnitTests.Apply.Models;

public sealed class ApplyModelsTests
{
    // ── ApplyStepOutcome ─────────────────────────────────────────────────────

    [Fact]
    public void ApplyStepOutcome_DefaultProperties_HaveExpectedDefaults()
    {
        var o = new ApplyStepOutcome();

        o.StepName.Should().BeEmpty();
        o.ExitCode.Should().Be(0);
        o.EvidenceMetadataPath.Should().BeNull();
        o.ArtifactSha256.Should().BeNull();
        o.ContinuityMarker.Should().BeNull();
    }

    [Fact]
    public void ApplyStepOutcome_AllPropertiesRoundTrip()
    {
        var now = DateTimeOffset.UtcNow;
        var o = new ApplyStepOutcome
        {
            StepName = "MyStep",
            ExitCode = 42,
            StartedAt = now,
            FinishedAt = now.AddSeconds(5),
            StdOutPath = @"C:\logs\out.log",
            StdErrPath = @"C:\logs\err.log",
            EvidenceMetadataPath = @"C:\Evidence\meta.json",
            ArtifactSha256 = "abc123",
            ContinuityMarker = "retained"
        };

        o.StepName.Should().Be("MyStep");
        o.ExitCode.Should().Be(42);
        o.StartedAt.Should().Be(now);
        o.FinishedAt.Should().Be(now.AddSeconds(5));
        o.StdOutPath.Should().Be(@"C:\logs\out.log");
        o.EvidenceMetadataPath.Should().Be(@"C:\Evidence\meta.json");
        o.ArtifactSha256.Should().Be("abc123");
        o.ContinuityMarker.Should().Be("retained");
    }

    // ── PreflightRequest ─────────────────────────────────────────────────────

    [Fact]
    public void PreflightRequest_AllPropertiesRoundTrip()
    {
        var r = new PreflightRequest
        {
            BundleRoot = @"C:\bundle",
            ModulesPath = @"C:\modules",
            PowerStigModulePath = @"C:\PowerSTIG",
            CheckLgpoConflict = true,
            BundleManifestPath = @"C:\bundle\manifest.json"
        };

        r.BundleRoot.Should().Be(@"C:\bundle");
        r.ModulesPath.Should().Be(@"C:\modules");
        r.PowerStigModulePath.Should().Be(@"C:\PowerSTIG");
        r.CheckLgpoConflict.Should().BeTrue();
        r.BundleManifestPath.Should().Be(@"C:\bundle\manifest.json");
    }

    // ── BitLockerConfig ──────────────────────────────────────────────────────

    [Fact]
    public void BitLockerConfig_DefaultsAreSet()
    {
        var cfg = new BitLockerConfig();

        cfg.VolumeTargets.Should().ContainSingle(v => v == "C:");
        cfg.EncryptionMethod.Should().Be("XtsAes256");
        cfg.RequireTpm.Should().BeTrue();
        cfg.RecoveryKeyPath.Should().BeNull();
    }

    [Fact]
    public void BitLockerConfig_PropertiesRoundTrip()
    {
        var cfg = new BitLockerConfig
        {
            VolumeTargets = ["C:", "D:"],
            EncryptionMethod = "Aes256",
            RecoveryKeyPath = @"C:\keys",
            RequireTpm = false
        };

        cfg.VolumeTargets.Should().HaveCount(2);
        cfg.EncryptionMethod.Should().Be("Aes256");
        cfg.RecoveryKeyPath.Should().Be(@"C:\keys");
        cfg.RequireTpm.Should().BeFalse();
    }

    // ── FirewallConfig ───────────────────────────────────────────────────────

    [Fact]
    public void FirewallConfig_DefaultsAreSet()
    {
        var cfg = new FirewallConfig();

        cfg.EnableAllProfiles.Should().BeTrue();
        cfg.DefaultInboundAction.Should().Be("Block");
        cfg.DefaultOutboundAction.Should().Be("Allow");
        cfg.RequiredRules.Should().BeEmpty();
    }

    [Fact]
    public void FirewallConfig_PropertiesRoundTrip()
    {
        var rule = new FirewallRuleDefinition
        {
            DisplayName = "Block RDP",
            Direction = "Inbound",
            Action = "Block",
            Protocol = "TCP",
            LocalPort = "3389",
            RemoteAddress = "Any",
            Description = "Block RDP traffic"
        };

        var cfg = new FirewallConfig
        {
            EnableAllProfiles = false,
            DefaultInboundAction = "Allow",
            DefaultOutboundAction = "Block",
            RequiredRules = [rule]
        };

        cfg.EnableAllProfiles.Should().BeFalse();
        cfg.DefaultInboundAction.Should().Be("Allow");
        cfg.RequiredRules.Should().ContainSingle();
        cfg.RequiredRules[0].DisplayName.Should().Be("Block RDP");
        cfg.RequiredRules[0].Protocol.Should().Be("TCP");
        cfg.RequiredRules[0].LocalPort.Should().Be("3389");
    }

    // ── FirewallRuleDefinition ───────────────────────────────────────────────

    [Fact]
    public void FirewallRuleDefinition_DefaultsAreSet()
    {
        var r = new FirewallRuleDefinition();

        r.DisplayName.Should().BeEmpty();
        r.Direction.Should().Be("Inbound");
        r.Action.Should().Be("Block");
        r.Protocol.Should().BeNull();
        r.LocalPort.Should().BeNull();
        r.RemoteAddress.Should().BeNull();
        r.Description.Should().BeNull();
    }

    // ── WdacPolicyConfig ─────────────────────────────────────────────────────

    [Fact]
    public void WdacPolicyConfig_DefaultsAreSet()
    {
        var cfg = new WdacPolicyConfig();

        cfg.PolicyPath.Should().BeNull();
        cfg.EnforcementMode.Should().Be("Audit");
        cfg.AllowMicrosoft.Should().BeTrue();
        cfg.AllowWindows.Should().BeTrue();
        cfg.AllowedPublishers.Should().BeEmpty();
    }

    [Fact]
    public void WdacPolicyConfig_PropertiesRoundTrip()
    {
        var cfg = new WdacPolicyConfig
        {
            PolicyPath = @"C:\policies\wdac.xml",
            EnforcementMode = "Enforce",
            AllowMicrosoft = false,
            AllowWindows = false,
            AllowedPublishers = ["CN=Contoso"]
        };

        cfg.PolicyPath.Should().Be(@"C:\policies\wdac.xml");
        cfg.EnforcementMode.Should().Be("Enforce");
        cfg.AllowMicrosoft.Should().BeFalse();
        cfg.AllowedPublishers.Should().ContainSingle("CN=Contoso");
    }

    // ── LcmException ────────────────────────────────────────────────────────

    [Fact]
    public void LcmException_MessageIsSet()
    {
        var ex = new LcmException("LCM failed");

        ex.Message.Should().Be("LCM failed");
    }

    [Fact]
    public void LcmException_WithInnerException_BothAreSet()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new LcmException("outer", inner);

        ex.Message.Should().Be("outer");
        ex.InnerException.Should().BeSameAs(inner);
    }

    // ── SnapshotException ────────────────────────────────────────────────────

    [Fact]
    public void SnapshotException_MessageIsSet()
    {
        var ex = new SnapshotException("snap failed");

        ex.Message.Should().Be("snap failed");
    }

    [Fact]
    public void SnapshotException_WithInnerException_BothAreSet()
    {
        var inner = new IOException("io");
        var ex = new SnapshotException("snap outer", inner);

        ex.Message.Should().Be("snap outer");
        ex.InnerException.Should().BeSameAs(inner);
    }

    // ── ValidationException ──────────────────────────────────────────────────

    [Fact]
    public void ValidationException_MessageIsSet()
    {
        var ex = new ValidationException("validation error");

        ex.Message.Should().Be("validation error");
    }

    [Fact]
    public void ValidationException_WithInnerException_BothAreSet()
    {
        var inner = new ArgumentException("arg");
        var ex = new ValidationException("val outer", inner);

        ex.Message.Should().Be("val outer");
        ex.InnerException.Should().BeSameAs(inner);
    }
}
