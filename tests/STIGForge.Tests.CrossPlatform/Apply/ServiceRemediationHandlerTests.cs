using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Moq;
using STIGForge.Apply.Remediation.Handlers;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Tests.CrossPlatform.Apply;

public sealed class ServiceRemediationHandlerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ServiceRemediationHandler CreateHandler(
        string serviceName,
        string expectedStartType,
        string expectedStatus,
        IProcessRunner? runner = null)
        => new("SV-000002", serviceName, expectedStartType, expectedStatus, "Unit test", runner);

    private static RemediationContext CreateContext(bool dryRun = false)
        => new()
        {
            BundleRoot = "C:\\bundle",
            Mode = HardeningMode.Safe,
            DryRun = dryRun,
            Control = new ControlRecord
            {
                ControlId = "CTRL-2",
                SourcePackId = "pack",
                ExternalIds = new ExternalIds { RuleId = "SV-000002" },
                Title = "Service test"
            }
        };

    /// <summary>
    /// Creates a mock that returns the given output for every call.
    /// Also captures the decoded PowerShell script from the encoded command argument.
    /// </summary>
    private static (Mock<IProcessRunner> Mock, Func<string?> GetLastScript) MockRunnerCapturing(string output)
    {
        string? lastScript = null;
        var mock = new Mock<IProcessRunner>();
        mock.Setup(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()))
            .Callback<ProcessStartInfo, CancellationToken>((psi, _) =>
            {
                var m = Regex.Match(psi.Arguments ?? "", @"-EncodedCommand\s+(\S+)");
                if (m.Success)
                    lastScript = Encoding.Unicode.GetString(Convert.FromBase64String(m.Groups[1].Value));
            })
            .ReturnsAsync(new ProcessResult { ExitCode = 0, StandardOutput = output });
        return (mock, () => lastScript);
    }

    // ── TestAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TestAsync_ServiceNotFound_ReturnsEmptyOutput_Failure()
    {
        // Empty PS output → service not found
        var (mock, _) = MockRunnerCapturing(string.Empty);
        var handler = CreateHandler("nosuchsvc", "Disabled", "Stopped", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("nosuchsvc");
    }

    [Fact]
    public async Task TestAsync_CompliantService_ReturnsSuccessNoChange()
    {
        var (mock, _) = MockRunnerCapturing("Auto|Running");
        var handler = CreateHandler("Spooler", "Auto", "Running", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.NewValue.Should().BeNull();
        result.Detail.Should().Contain("Already compliant");
    }

    [Fact]
    public async Task TestAsync_NonCompliantStartType_ReturnsSuccessWithNewValue()
    {
        // Service is currently Auto|Running but we expect Disabled|Stopped
        var (mock, _) = MockRunnerCapturing("Auto|Running");
        var handler = CreateHandler("Spooler", "Disabled", "Stopped", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.NewValue.Should().Contain("Disabled");
        result.Detail.Should().Contain("Non-compliant");
    }

    // ── ApplyAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_DryRun_DelegatesToTest()
    {
        var (mock, _) = MockRunnerCapturing("Auto|Running");
        var handler = CreateHandler("Spooler", "Disabled", "Stopped", mock.Object);

        var result = await handler.ApplyAsync(CreateContext(dryRun: true), CancellationToken.None);

        // DryRun → TestAsync → non-compliant result
        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        result.NewValue.Should().Contain("Disabled");
    }

    [Fact]
    public async Task ApplyAsync_AlreadyCompliant_ReturnsWithoutApplying()
    {
        var (mock, _) = MockRunnerCapturing("Auto|Running");
        var handler = CreateHandler("Spooler", "Auto", "Running", mock.Object);

        var result = await handler.ApplyAsync(CreateContext(), CancellationToken.None);

        // Already compliant → no second (apply) PS call
        result.Success.Should().BeTrue();
        result.Changed.Should().BeFalse();
        mock.Verify(r => r.RunAsync(It.IsAny<ProcessStartInfo>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── NormalizeStartType ───────────────────────────────────────────────────

    [Theory]
    [InlineData("auto", "Automatic")]
    [InlineData("Automatic", "Automatic")]
    public async Task NormalizeStartType_Auto_ReturnsAutomatic(string input, string expected)
    {
        // Supply exactly the expected normalized value in the PS output
        var (mock, _) = MockRunnerCapturing($"{expected}|Running");
        var handler = CreateHandler("Spooler", input, "Running", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Detail.Should().Contain("Already compliant",
            $"'{input}' should normalize to '{expected}' and match output '{expected}'");
    }

    [Fact]
    public async Task NormalizeStartType_AutoDelayed_ReturnsAutoDelayedStart()
    {
        var (mock, _) = MockRunnerCapturing("AutomaticDelayedStart|Running");
        var handler = CreateHandler("Spooler", "AutoDelayed", "Running", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Detail.Should().Contain("Already compliant");
    }

    [Fact]
    public async Task NormalizeStartType_Disabled_ReturnsDisabled()
    {
        var (mock, _) = MockRunnerCapturing("Disabled|Stopped");
        var handler = CreateHandler("Spooler", "disabled", "Stopped", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Detail.Should().Contain("Already compliant");
    }

    [Fact]
    public async Task NormalizeStartType_Manual_ReturnsManual()
    {
        var (mock, _) = MockRunnerCapturing("Manual|Stopped");
        var handler = CreateHandler("Spooler", "manual", "Stopped", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Detail.Should().Contain("Already compliant");
    }

    // ── NormalizeStatus ──────────────────────────────────────────────────────

    [Fact]
    public async Task NormalizeStatus_Running_ReturnsRunning()
    {
        var (mock, _) = MockRunnerCapturing("Automatic|Running");
        var handler = CreateHandler("Spooler", "Automatic", "running", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Detail.Should().Contain("Already compliant");
    }

    [Fact]
    public async Task NormalizeStatus_Stopped_ReturnsStopped()
    {
        var (mock, _) = MockRunnerCapturing("Disabled|Stopped");
        var handler = CreateHandler("Spooler", "Disabled", "stopped", mock.Object);

        var result = await handler.TestAsync(CreateContext(), CancellationToken.None);

        result.Detail.Should().Contain("Already compliant");
    }

    // ── EscapeWmiFilterValue (via captured PS script) ─────────────────────────

    [Fact]
    public async Task EscapeWmiFilterValue_SingleQuote_IsEscaped()
    {
        var (mock, getScript) = MockRunnerCapturing("Automatic|Running");
        var handler = CreateHandler("O'Brien", "Automatic", "Running", mock.Object);

        await handler.TestAsync(CreateContext(), CancellationToken.None);

        var script = getScript();
        script.Should().NotBeNull();
        // Single quote in service name must be escaped as \' in WMI filter
        script!.Should().Contain(@"O\'Brien");
    }

    [Fact]
    public async Task EscapeWmiFilterValue_Backslash_IsDoubled()
    {
        var (mock, getScript) = MockRunnerCapturing("Automatic|Running");
        var handler = CreateHandler(@"domain\service", "Automatic", "Running", mock.Object);

        await handler.TestAsync(CreateContext(), CancellationToken.None);

        var script = getScript();
        script.Should().NotBeNull();
        script!.Should().Contain(@"domain\\service");
    }

    [Fact]
    public async Task EscapeWmiFilterValue_CleanName_IsUnchanged()
    {
        var (mock, getScript) = MockRunnerCapturing("Automatic|Running");
        var handler = CreateHandler("Spooler", "Automatic", "Running", mock.Object);

        await handler.TestAsync(CreateContext(), CancellationToken.None);

        var script = getScript();
        script.Should().NotBeNull();
        script!.Should().Contain("Name='Spooler'");
    }

    [Fact]
    public async Task EscapeWmiFilterValue_MaliciousInjection_IsNeutralized()
    {
        // svc' OR Name='* would break WMI filter; must be escaped
        var (mock, getScript) = MockRunnerCapturing("Automatic|Running");
        var handler = CreateHandler("svc' OR Name='*", "Automatic", "Running", mock.Object);

        await handler.TestAsync(CreateContext(), CancellationToken.None);

        var script = getScript();
        script.Should().NotBeNull();
        // Confirm injection characters are escaped
        script!.Should().NotContain("Name='svc' OR Name='*'");
        script!.Should().Contain(@"svc\' OR Name=\'*");
    }
}
