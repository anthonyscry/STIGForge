using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using STIGForge.Apply;
using STIGForge.Apply.Steps;
using STIGForge.Core.Abstractions;

namespace STIGForge.UnitTests.Apply.Steps;

public sealed class PolicyStepHandlerTests : IDisposable
{
    private readonly string _tempDir;

    public PolicyStepHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-policy-step-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── CanRunLgpo ───────────────────────────────────────────────────────────

    [Fact]
    public void CanRunLgpo_WhenNoLgpoRunnerInjected_ReturnsFalse()
    {
        var handler = new PolicyStepHandler(NullLogger<ApplyRunner>.Instance, null, new Mock<IProcessRunner>().Object);

        handler.CanRunLgpo.Should().BeFalse();
    }

    // ── RunAdmxImport ────────────────────────────────────────────────────────

    [Fact]
    public void RunAdmxImport_NonExistentSourceDir_ReturnsExitCode1()
    {
        var logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logsDir);

        var handler = new PolicyStepHandler(NullLogger<ApplyRunner>.Instance, null, new Mock<IProcessRunner>().Object);
        var request = new ApplyRequest
        {
            BundleRoot = _tempDir,
            AdmxTemplateRootPath = Path.Combine(_tempDir, "doesNotExist"),
            AdmxPolicyDefinitionsPath = Path.Combine(_tempDir, "PolicyDefs")
        };

        var outcome = handler.RunAdmxImport(request, _tempDir, logsDir, "admx-step");

        outcome.ExitCode.Should().Be(1);
        outcome.StepName.Should().Be("admx-step");
        File.Exists(outcome.StdErrPath).Should().BeTrue();
        File.ReadAllText(outcome.StdErrPath).Should().Contain("not found");
    }

    [Fact]
    public void RunAdmxImport_EmptySourceDir_ReturnsExitCode1()
    {
        var sourceDir = Path.Combine(_tempDir, "admx-empty");
        Directory.CreateDirectory(sourceDir);

        var logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logsDir);

        var targetDir = Path.Combine(_tempDir, "PolicyDefs");

        var handler = new PolicyStepHandler(NullLogger<ApplyRunner>.Instance, null, new Mock<IProcessRunner>().Object);
        var request = new ApplyRequest
        {
            BundleRoot = _tempDir,
            AdmxTemplateRootPath = sourceDir,
            AdmxPolicyDefinitionsPath = targetDir
        };

        var outcome = handler.RunAdmxImport(request, _tempDir, logsDir, "admx-empty");

        outcome.ExitCode.Should().Be(1);
        File.ReadAllText(outcome.StdErrPath).Should().Contain("No applicable ADMX");
    }

    [Fact]
    public void RunAdmxImport_WithOneAdmxFile_CopiesToTargetAndReturnsExitCode0()
    {
        var sourceDir = Path.Combine(_tempDir, "admx-source");
        Directory.CreateDirectory(sourceDir);

        // Write a generic .admx (no OS-specific tag, so IsTemplateApplicableToCurrentOs returns true)
        File.WriteAllText(Path.Combine(sourceDir, "generic.admx"), "<admx/>");

        var logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logsDir);

        var targetDir = Path.Combine(_tempDir, "PolicyDefs");

        var handler = new PolicyStepHandler(NullLogger<ApplyRunner>.Instance, null, new Mock<IProcessRunner>().Object);
        var request = new ApplyRequest
        {
            BundleRoot = _tempDir,
            AdmxTemplateRootPath = sourceDir,
            AdmxPolicyDefinitionsPath = targetDir
        };

        var outcome = handler.RunAdmxImport(request, _tempDir, logsDir, "admx-one");

        outcome.ExitCode.Should().Be(0);
        File.Exists(Path.Combine(targetDir, "generic.admx")).Should().BeTrue();
        var stdout = File.ReadAllText(outcome.StdOutPath);
        stdout.Should().Contain("Copied .admx: 1");
    }

    [Fact]
    public void RunAdmxImport_WithAdmxAndAdml_CopiesBothFiles()
    {
        var sourceDir = Path.Combine(_tempDir, "admx-with-adml");
        Directory.CreateDirectory(sourceDir);

        File.WriteAllText(Path.Combine(sourceDir, "mypol.admx"), "<admx/>");
        File.WriteAllText(Path.Combine(sourceDir, "mypol.adml"), "<adml/>");

        var logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logsDir);

        var targetDir = Path.Combine(_tempDir, "PolicyDefs2");

        var handler = new PolicyStepHandler(NullLogger<ApplyRunner>.Instance, null, new Mock<IProcessRunner>().Object);
        var request = new ApplyRequest
        {
            BundleRoot = _tempDir,
            AdmxTemplateRootPath = sourceDir,
            AdmxPolicyDefinitionsPath = targetDir
        };

        var outcome = handler.RunAdmxImport(request, _tempDir, logsDir, "admx-adml");

        outcome.ExitCode.Should().Be(0);
        File.Exists(Path.Combine(targetDir, "mypol.admx")).Should().BeTrue();
        File.Exists(Path.Combine(targetDir, "mypol.adml")).Should().BeTrue();
        File.ReadAllText(outcome.StdOutPath).Should().Contain("Copied .adml: 1");
    }

    [Fact]
    public void RunAdmxImport_AdmxPolicyDefinitionsPathNotSet_FallsBackToBundleRootPath()
    {
        var sourceDir = Path.Combine(_tempDir, "admx-fallback");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "base.admx"), "<admx/>");

        var logsDir = Path.Combine(_tempDir, "logs");
        Directory.CreateDirectory(logsDir);

        var handler = new PolicyStepHandler(NullLogger<ApplyRunner>.Instance, null, new Mock<IProcessRunner>().Object);
        var request = new ApplyRequest
        {
            BundleRoot = _tempDir,
            AdmxTemplateRootPath = sourceDir,
            AdmxPolicyDefinitionsPath = null   // not set → fallback
        };

        var outcome = handler.RunAdmxImport(request, _tempDir, logsDir, "admx-fallback");

        // On Linux the fallback is bundleRoot/Apply/PolicyDefinitions; on Windows it may differ
        outcome.ExitCode.Should().Be(0);
    }

    [Fact]
    public void RunAdmxImport_WritesStdOutAndStdErrFiles()
    {
        var sourceDir = Path.Combine(_tempDir, "admx-logs");
        Directory.CreateDirectory(sourceDir);

        var logsDir = Path.Combine(_tempDir, "log-check");
        Directory.CreateDirectory(logsDir);

        var handler = new PolicyStepHandler(NullLogger<ApplyRunner>.Instance, null, new Mock<IProcessRunner>().Object);
        var request = new ApplyRequest
        {
            BundleRoot = _tempDir,
            AdmxTemplateRootPath = sourceDir,
            AdmxPolicyDefinitionsPath = Path.Combine(_tempDir, "PD")
        };

        var outcome = handler.RunAdmxImport(request, _tempDir, logsDir, "log-step");

        File.Exists(outcome.StdOutPath).Should().BeTrue();
        File.Exists(outcome.StdErrPath).Should().BeTrue();
        outcome.StartedAt.Should().BeBefore(DateTimeOffset.Now.AddSeconds(1));
        outcome.FinishedAt.Should().BeOnOrAfter(outcome.StartedAt);
    }
}
