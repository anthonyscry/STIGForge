using FluentAssertions;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class EvaluateStigRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public EvaluateStigRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-eval-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── ResolveScriptPath: empty / null tool root ────────────────────────────

    [Fact]
    public async Task RunAsync_EmptyToolRoot_ThrowsArgumentException()
    {
        var runner = new EvaluateStigRunner();

        var act = async () => await runner.RunAsync(string.Empty, string.Empty, null, 5);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*tool root*");
    }

    [Fact]
    public async Task RunAsync_WhiteSpaceToolRoot_ThrowsArgumentException()
    {
        var runner = new EvaluateStigRunner();

        var act = async () => await runner.RunAsync("   ", string.Empty, null, 5);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── ResolveScriptPath: non-existent directory ────────────────────────────

    [Fact]
    public async Task RunAsync_NonExistentDirectory_ThrowsFileNotFoundException()
    {
        var runner = new EvaluateStigRunner();
        var fakePath = Path.Combine(_tempDir, "no-such-dir");

        var act = async () => await runner.RunAsync(fakePath, string.Empty, null, 5);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*Evaluate-STIG.ps1*");
    }

    // ── ResolveScriptPath: existing directory without the script ─────────────

    [Fact]
    public async Task RunAsync_DirectoryWithoutScript_ThrowsFileNotFoundException()
    {
        var runner = new EvaluateStigRunner();
        var dir = Path.Combine(_tempDir, "empty-tool-dir");
        Directory.CreateDirectory(dir);

        var act = async () => await runner.RunAsync(dir, string.Empty, null, 5);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*Evaluate-STIG.ps1*");
    }

    // ── ResolveScriptPath: file path that is not Evaluate-STIG.ps1 ───────────

    [Fact]
    public async Task RunAsync_FilePathThatIsNotEvaluateStigScript_ThrowsFileNotFoundException()
    {
        var runner = new EvaluateStigRunner();
        var wrongFile = Path.Combine(_tempDir, "other-script.ps1");
        File.WriteAllText(wrongFile, "# not the right script");

        var act = async () => await runner.RunAsync(wrongFile, string.Empty, null, 5);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ── ResolveScriptPath: direct path to Evaluate-STIG.ps1 resolves correctly

    [Fact]
    public async Task RunAsync_DirectFilePathToEvaluateStigPs1_DoesNotThrowPathError()
    {
        var runner = new EvaluateStigRunner();
        var scriptPath = Path.Combine(_tempDir, "Evaluate-STIG.ps1");
        File.WriteAllText(scriptPath, "# stub script");

        // The path resolves fine; the failure will come from process start (powershell not available in test env)
        // We only verify that it doesn't throw a FileNotFoundException or ArgumentException
        var act = async () => await runner.RunAsync(scriptPath, string.Empty, null, 1);

        var exception = await Record.ExceptionAsync(act);
        exception.Should().NotBeOfType<FileNotFoundException>();
        exception.Should().NotBeOfType<ArgumentException>();
    }

    // ── ResolveScriptPath: directory containing Evaluate-STIG.ps1 ────────────

    [Fact]
    public async Task RunAsync_DirectoryContainingScript_DoesNotThrowPathError()
    {
        var runner = new EvaluateStigRunner();
        var dir = Path.Combine(_tempDir, "tool-dir");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Evaluate-STIG.ps1"), "# stub");

        var act = async () => await runner.RunAsync(dir, string.Empty, null, 1);

        var exception = await Record.ExceptionAsync(act);
        exception.Should().NotBeOfType<FileNotFoundException>();
        exception.Should().NotBeOfType<ArgumentException>();
    }

    // ── ResolveScriptPath: script in subdirectory ─────────────────────────────

    [Fact]
    public async Task RunAsync_ScriptInSubdirectory_DoesNotThrowPathError()
    {
        var runner = new EvaluateStigRunner();
        var dir = Path.Combine(_tempDir, "tool-with-subdir");
        var subDir = Path.Combine(dir, "Evaluate-STIG");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Evaluate-STIG.ps1"), "# stub");

        var act = async () => await runner.RunAsync(dir, string.Empty, null, 1);

        var exception = await Record.ExceptionAsync(act);
        exception.Should().NotBeOfType<FileNotFoundException>();
        exception.Should().NotBeOfType<ArgumentException>();
    }
}
