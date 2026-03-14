using FluentAssertions;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class ScapRunnerTests : IDisposable
{
    private readonly string _tempDir;

    public ScapRunnerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-scap-runner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ── ResolveCommandPath: empty / null ──────────────────────────────────────

    [Fact]
    public async Task RunAsync_EmptyCommandPath_ThrowsArgumentException()
    {
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync(string.Empty, "--scan x", null, 5);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*command path*");
    }

    [Fact]
    public async Task RunAsync_WhiteSpaceCommandPath_ThrowsArgumentException()
    {
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync("   ", "--scan x", null, 5);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Unsupported GUI binary (scc.exe) ──────────────────────────────────────

    [Fact]
    public async Task RunAsync_SccExeGuiBinary_ThrowsInvalidOperationException()
    {
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync("scc.exe", "--scan x", null, 5);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SCC GUI binary*");
    }

    [Fact]
    public async Task RunAsync_SccWithoutExtension_ThrowsInvalidOperationException()
    {
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync("scc", "--scan x", null, 5);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SCC GUI binary*");
    }

    // ── Full path to scc.exe also rejected ───────────────────────────────────

    [Fact]
    public async Task RunAsync_FullPathToSccExe_ThrowsInvalidOperationException()
    {
        var sccPath = Path.Combine(_tempDir, "scc.exe");
        File.WriteAllBytes(sccPath, Array.Empty<byte>());
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync(sccPath, "--scan x", null, 5);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SCC GUI binary*");
    }

    // ── Directory containing only scc.exe ────────────────────────────────────

    [Fact]
    public async Task RunAsync_DirectoryWithOnlySccGui_ThrowsInvalidOperationException()
    {
        var dir = Path.Combine(_tempDir, "scap-gui-only");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "scc.exe"), Array.Empty<byte>());
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync(dir, "--scan x", null, 5);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*scc.exe*");
    }

    // ── Supported CLI binaries (simple command name) ──────────────────────────

    [Theory]
    [InlineData("cscc.exe")]
    [InlineData("cscc-remote.exe")]
    [InlineData("cscc")]
    [InlineData("cscc-remote")]
    public async Task RunAsync_SupportedCliBinarySimpleName_DoesNotThrowArgumentOrValidationError(string binaryName)
    {
        var runner = new ScapRunner();

        // Simple name resolves immediately; process start will fail (binary not present) but that is not an arg error
        var act = async () => await runner.RunAsync(binaryName, "--scan x", null, 1);

        var exception = await Record.ExceptionAsync(act);
        exception.Should().NotBeOfType<ArgumentException>("supported CLI binary names should pass validation");
        exception.Should().NotBeOfType<InvalidOperationException>("supported CLI binaries should not be rejected");
    }

    // ── Non-existent full path (not a directory, not a file) ─────────────────

    [Fact]
    public async Task RunAsync_NonExistentFilePath_ThrowsFileNotFoundException()
    {
        var runner = new ScapRunner();
        var fakePath = Path.Combine(_tempDir, "no-such-dir", "cscc.exe");

        var act = async () => await runner.RunAsync(fakePath, "--scan x", null, 5);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*SCAP command not found*");
    }

    // ── Directory with no supported binaries ─────────────────────────────────

    [Fact]
    public async Task RunAsync_DirectoryWithNoSupportedBinary_ThrowsFileNotFoundException()
    {
        var dir = Path.Combine(_tempDir, "scap-empty");
        Directory.CreateDirectory(dir);
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync(dir, "--scan x", null, 5);

        await act.Should().ThrowAsync<FileNotFoundException>()
            .WithMessage("*SCAP command not found*");
    }

    // ── Directory with supported CLI binary ───────────────────────────────────

    [Fact]
    public async Task RunAsync_DirectoryContainingCsccExe_DoesNotThrowPathOrValidationError()
    {
        var dir = Path.Combine(_tempDir, "scap-with-cscc");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "cscc.exe"), Array.Empty<byte>());
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync(dir, "--scan x", null, 1);

        var exception = await Record.ExceptionAsync(act);
        if (exception is not null)
        {
            exception.Should().NotBeOfType<FileNotFoundException>();
            exception.Should().NotBeOfType<ArgumentException>();
            exception.Should().NotBeOfType<InvalidOperationException>();
        }
    }

    // ── Path without extension resolved to .exe variant ──────────────────────

    [Fact]
    public async Task RunAsync_PathWithoutExtensionWhenExeExists_DoesNotThrowPathError()
    {
        // Use a valid SCAP binary name so the runner accepts it after resolving the .exe extension
        var pathWithoutExt = Path.Combine(_tempDir, "cscc");
        File.WriteAllBytes(pathWithoutExt + ".exe", Array.Empty<byte>());
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync(pathWithoutExt, "--scan x", null, 1);

        var exception = await Record.ExceptionAsync(act);
        if (exception is not null)
        {
            exception.Should().NotBeOfType<FileNotFoundException>();
            exception.Should().NotBeOfType<ArgumentException>();
        }
    }

    // ── Full path to a supported CLI binary that exists ───────────────────────

    [Fact]
    public async Task RunAsync_FullPathToCsccExe_DoesNotThrowPathOrValidationError()
    {
        var csccPath = Path.Combine(_tempDir, "cscc.exe");
        File.WriteAllBytes(csccPath, Array.Empty<byte>());
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync(csccPath, "--scan x", null, 1);

        var exception = await Record.ExceptionAsync(act);
        exception.Should().NotBeOfType<FileNotFoundException>();
        exception.Should().NotBeOfType<ArgumentException>();
        exception.Should().NotBeOfType<InvalidOperationException>();
    }

    // ── cscc.exe in a subdirectory ────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_DirectoryWithCsccExeInSubDir_ResolvesCorrectly()
    {
        var dir = Path.Combine(_tempDir, "scap-subdir");
        var subDir = Path.Combine(dir, "bin");
        Directory.CreateDirectory(subDir);
        File.WriteAllBytes(Path.Combine(subDir, "cscc.exe"), Array.Empty<byte>());
        var runner = new ScapRunner();

        var act = async () => await runner.RunAsync(dir, "--scan x", null, 1);

        var exception = await Record.ExceptionAsync(act);
        exception.Should().NotBeOfType<FileNotFoundException>();
        exception.Should().NotBeOfType<ArgumentException>();
        exception.Should().NotBeOfType<InvalidOperationException>();
    }
}
