using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using STIGForge.Apply.Reboot;

namespace STIGForge.Tests.CrossPlatform.Apply;

/// <summary>
/// Tests for <see cref="RebootCoordinator"/>.
/// Focuses on the ordering invariant: resume marker written BEFORE the reboot is scheduled
/// (and BEFORE the in-memory count is incremented), ensuring correct state on resume.
/// Uses real temp directories for file I/O tests.
/// </summary>
public sealed class RebootCoordinatorTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // ── helpers ──────────────────────────────────────────────────────────────

    private string MakeBundleRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stigforge_reboot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static string MarkerPath(string bundleRoot)
        => Path.Combine(bundleRoot, "Apply", ".resume_marker.json");

    private static RebootCoordinator CreateCoordinator(Func<int, bool>? scheduleReboot = null)
        => new(NullLogger<RebootCoordinator>.Instance, scheduleReboot ?? (_ => true));

    private RebootContext MakeContext(string? bundleRoot = null, int rebootCount = 0)
        => new()
        {
            BundleRoot = bundleRoot ?? MakeBundleRoot(),
            RebootCount = rebootCount,
            CurrentStepIndex = 1,
            CompletedSteps = ["step-0"],
            RebootScheduledAt = DateTimeOffset.UtcNow
        };

    private async Task WriteMarkerAsync(string bundleRoot, RebootContext ctx)
    {
        var dir = Path.Combine(bundleRoot, "Apply");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(MarkerPath(bundleRoot), JsonSerializer.Serialize(ctx));
    }

    // ── ResumeAfterReboot: no marker ─────────────────────────────────────────

    [Fact]
    public async Task DetectRebootRequired_NoMarkerFile_ReturnsFalse()
    {
        // ResumeAfterReboot returns null (no marker) → treated as "no pending reboot resume"
        var bundleRoot = MakeBundleRoot();
        var coordinator = CreateCoordinator();

        var context = await coordinator.ResumeAfterReboot(bundleRoot, CancellationToken.None);

        context.Should().BeNull("no resume marker means no pending reboot to resume");
    }

    [Fact]
    public async Task DetectRebootRequired_MarkerFileExists_ReturnsTrue()
    {
        // ResumeAfterReboot returns non-null when marker exists
        var bundleRoot = MakeBundleRoot();
        await WriteMarkerAsync(bundleRoot, MakeContext(bundleRoot));
        var coordinator = CreateCoordinator();

        var context = await coordinator.ResumeAfterReboot(bundleRoot, CancellationToken.None);

        context.Should().NotBeNull("resume marker indicates a pending reboot to resume");
    }

    // ── LoadResumeContext / SaveResumeContext (via ScheduleReboot / ResumeAfterReboot) ──

    [Fact]
    public async Task LoadResumeContext_NoFile_ReturnsNull()
    {
        var bundleRoot = MakeBundleRoot();
        var coordinator = CreateCoordinator();

        var result = await coordinator.ResumeAfterReboot(bundleRoot, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadResumeContext_ExistingFile_DeserializesCorrectly()
    {
        var bundleRoot = MakeBundleRoot();
        var written = MakeContext(bundleRoot, rebootCount: 2);
        written.CompletedSteps = ["step-a", "step-b"];
        await WriteMarkerAsync(bundleRoot, written);
        var coordinator = CreateCoordinator();

        var loaded = await coordinator.ResumeAfterReboot(bundleRoot, CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.RebootCount.Should().Be(2);
        loaded.CompletedSteps.Should().BeEquivalentTo(["step-a", "step-b"]);
        loaded.BundleRoot.Should().Be(bundleRoot);
    }

    [Fact]
    public async Task SaveResumeContext_WritesFile()
    {
        var bundleRoot = MakeBundleRoot();
        var coordinator = CreateCoordinator();
        var ctx = MakeContext(bundleRoot);

        await coordinator.ScheduleReboot(ctx, CancellationToken.None, delaySeconds: 0);

        File.Exists(MarkerPath(bundleRoot)).Should().BeTrue("ScheduleReboot must write a resume marker");
    }

    /// <summary>
    /// Ordering invariant: the marker file must be written with the CURRENT RebootCount
    /// (before the in-memory increment) so that if the machine reboots between the file
    /// write and the in-memory increment, the persisted count is still correct.
    /// </summary>
    [Fact]
    public async Task SaveResumeContext_RebootCount_IsPreIncrementedBeforeWrite()
    {
        var bundleRoot = MakeBundleRoot();
        const int initialCount = 0;
        var rebootScheduledAt = DateTimeOffset.MinValue;
        var fileWrittenAt = DateTimeOffset.MinValue;

        // Capture the moment the file is written versus when ScheduleReboot invokes the delegate
        var coordinator = new RebootCoordinator(
            NullLogger<RebootCoordinator>.Instance,
            _ =>
            {
                // Record what's in the file at the moment the OS reboot is requested
                var json = File.ReadAllText(MarkerPath(bundleRoot));
                var ctx = JsonSerializer.Deserialize<RebootContext>(json)!;
                rebootScheduledAt = DateTimeOffset.UtcNow;
                fileWrittenAt = ctx.RebootScheduledAt; // non-default means file exists
                return true;
            });

        var context = MakeContext(bundleRoot, rebootCount: initialCount);

        await coordinator.ScheduleReboot(context, CancellationToken.None, delaySeconds: 0);

        // Read the persisted marker
        var markerJson = await File.ReadAllTextAsync(MarkerPath(bundleRoot));
        var written = JsonSerializer.Deserialize<RebootContext>(markerJson)!;

        // The count in the file should equal the count BEFORE the in-memory increment
        written.RebootCount.Should().Be(initialCount,
            "the resume marker must record the count value at the time of writing " +
            "(before the in-memory increment), preserving correct state if reboot fires immediately");

        // In-memory context should have been incremented
        context.RebootCount.Should().Be(initialCount + 1,
            "after ScheduleReboot completes, the in-memory count must be incremented");
    }

    // ── ClearResumeMarker (via ResumeAfterReboot which deletes the file) ──────

    [Fact]
    public async Task ClearResumeMarker_DeletesFile()
    {
        var bundleRoot = MakeBundleRoot();
        await WriteMarkerAsync(bundleRoot, MakeContext(bundleRoot));
        var coordinator = CreateCoordinator();

        var result = await coordinator.ResumeAfterReboot(bundleRoot, CancellationToken.None);

        result.Should().NotBeNull();
        // After successful read, the marker file is deleted to prevent duplicate resumes
        File.Exists(MarkerPath(bundleRoot)).Should().BeFalse("marker must be deleted after successful resume read");
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
