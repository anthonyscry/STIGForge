using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Workflow;
using STIGForge.Tests.CrossPlatform.Helpers;
using System.IO.Compression;

namespace STIGForge.Tests.CrossPlatform.Infrastructure;

public sealed class LocalSetupValidatorTests
{
    // ── Happy path: explicit path with Evaluate-STIG.ps1 present ────────────

    [Fact]
    public void ValidateRequiredTools_ExplicitPath_ScriptPresent_ReturnsThatPath()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(tmp.File("Evaluate-STIG.ps1"), "# stub");

        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        var result = validator.ValidateRequiredTools(tmp.Path);

        result.Should().Be(tmp.Path);
    }

    // ── Default tool-root candidates ─────────────────────────────────────────

    [Fact]
    public void ValidateRequiredTools_DefaultCandidate_NestedPath_ReturnsNestedPath()
    {
        using var tmp = new TempDirectory();
        // Match: {toolsRoot}/Evaluate-STIG/Evaluate-STIG/Evaluate-STIG.ps1
        var nestedDir = Path.Combine(tmp.Path, "Evaluate-STIG", "Evaluate-STIG");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "Evaluate-STIG.ps1"), "# stub");

        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        var result = validator.ValidateRequiredTools();

        result.Should().Be(nestedDir);
    }

    [Fact]
    public void ValidateRequiredTools_DefaultCandidate_ShallowPath_ReturnsShallowPath()
    {
        using var tmp = new TempDirectory();
        // Match: {toolsRoot}/Evaluate-STIG/Evaluate-STIG.ps1
        var shallowDir = Path.Combine(tmp.Path, "Evaluate-STIG");
        Directory.CreateDirectory(shallowDir);
        File.WriteAllText(Path.Combine(shallowDir, "Evaluate-STIG.ps1"), "# stub");

        // Create only the shallow candidate, not the nested one
        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        // Call with no evaluateStigToolRoot to use the default resolution
        var result = validator.ValidateRequiredTools(null, null);

        result.Should().Be(shallowDir);
    }

    // ── Missing tool paths → exception ───────────────────────────────────────

    [Fact]
    public void ValidateRequiredTools_NoScriptAnywhere_ThrowsInvalidOperationException()
    {
        using var tmp = new TempDirectory();
        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        var act = () => validator.ValidateRequiredTools();
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Evaluate-STIG*");
    }

    [Fact]
    public void ValidateRequiredTools_ExplicitPath_DirectoryMissing_ThrowsInvalidOperationException()
    {
        using var tmp = new TempDirectory();
        var missingPath = Path.Combine(tmp.Path, "does-not-exist");
        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        var act = () => validator.ValidateRequiredTools(missingPath);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateRequiredTools_ExplicitPath_ScriptMissing_ThrowsInvalidOperationException()
    {
        using var tmp = new TempDirectory();
        // Directory exists but no Evaluate-STIG.ps1
        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        var act = () => validator.ValidateRequiredTools(tmp.Path);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── ZIP staging from import root ─────────────────────────────────────────

    [Fact]
    public void ValidateRequiredTools_ImportRoot_ContainsEvaluateStigZip_StagesAndReturns()
    {
        using var tmp = new TempDirectory();

        // Build a zip with Evaluate-STIG.ps1 inside
        var zipPath = tmp.File("evaluate-stig-v1.zip");
        CreateEvaluateStigZip(zipPath, "Evaluate-STIG/Evaluate-STIG.ps1");

        var toolsRoot = tmp.File("tools");
        Directory.CreateDirectory(toolsRoot);

        var paths = MockPaths(toolsRoot: toolsRoot);
        var validator = new LocalSetupValidator(paths);

        // No explicit tool root; import root has the zip
        var result = validator.ValidateRequiredTools(null, tmp.Path);

        result.Should().NotBeNullOrEmpty();
        File.Exists(Path.Combine(result, "Evaluate-STIG.ps1")).Should().BeTrue();
    }

    [Fact]
    public void ValidateRequiredTools_ImportRootEmpty_NoZips_Throws()
    {
        using var tmp = new TempDirectory();
        var toolsRoot = tmp.File("tools");
        Directory.CreateDirectory(toolsRoot);

        var paths = MockPaths(toolsRoot: toolsRoot);
        var validator = new LocalSetupValidator(paths);

        // Import root exists but has no evaluate-stig zips
        var act = () => validator.ValidateRequiredTools(null, tmp.Path);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateRequiredTools_ImportRootDoesNotExist_Throws()
    {
        using var tmp = new TempDirectory();
        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        var act = () => validator.ValidateRequiredTools(null, "/nonexistent-import-root");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ValidateRequiredTools_ImportRoot_ZipNoScript_Throws()
    {
        using var tmp = new TempDirectory();

        // Build a zip that contains "evaluate-stig" in the name but NOT Evaluate-STIG.ps1
        var zipPath = tmp.File("evaluate-stig-empty.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("README.txt");
            using var sw = new StreamWriter(entry.Open());
            sw.Write("no script here");
        }

        var toolsRoot = tmp.File("tools");
        Directory.CreateDirectory(toolsRoot);

        var paths = MockPaths(toolsRoot: toolsRoot);
        var validator = new LocalSetupValidator(paths);

        var act = () => validator.ValidateRequiredTools(null, tmp.Path);
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Overload forwarding ──────────────────────────────────────────────────

    [Fact]
    public void ValidateRequiredTools_ZeroArg_ForwardsToFullOverload()
    {
        using var tmp = new TempDirectory();
        var nestedDir = Path.Combine(tmp.Path, "Evaluate-STIG", "Evaluate-STIG");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "Evaluate-STIG.ps1"), "# stub");

        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        // Zero-arg overload should succeed the same as full overload
        var result = validator.ValidateRequiredTools();
        result.Should().Be(nestedDir);
    }

    [Fact]
    public void ValidateRequiredTools_OneArg_ForwardsToFullOverload()
    {
        using var tmp = new TempDirectory();
        File.WriteAllText(tmp.File("Evaluate-STIG.ps1"), "# stub");

        var paths = MockPaths(toolsRoot: tmp.Path);
        var validator = new LocalSetupValidator(paths);

        var result = validator.ValidateRequiredTools(tmp.Path);
        result.Should().Be(tmp.Path);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IPathBuilder MockPaths(string toolsRoot)
    {
        var mock = new Mock<IPathBuilder>();
        mock.Setup(p => p.GetToolsRoot()).Returns(toolsRoot);
        return mock.Object;
    }

    private static void CreateEvaluateStigZip(string zipPath, string entryName)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry(entryName);
        using var sw = new StreamWriter(entry.Open());
        sw.Write("# Evaluate-STIG stub");
    }
}
