using FluentAssertions;
using STIGForge.Apply;

namespace STIGForge.Tests.CrossPlatform.Apply;

/// <summary>
/// Tests for <see cref="IdempotencyTracker"/>.
/// Uses real temp directories for file I/O validation.
/// </summary>
public sealed class IdempotencyTrackerTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    // ── helpers ──────────────────────────────────────────────────────────────

    private string MakeBundleRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"stigforge_idem_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private IdempotencyTracker CreateTracker(string? bundleRoot = null)
        => new(bundleRoot ?? MakeBundleRoot());

    // ── IsCompleted ──────────────────────────────────────────────────────────

    [Fact]
    public void NewTracker_IsCompletedReturnsFalse()
    {
        var tracker = CreateTracker();

        tracker.IsCompleted("step-1").Should().BeFalse();
    }

    // ── MarkCompleted ────────────────────────────────────────────────────────

    [Fact]
    public void MarkCompleted_StepIsCompleted()
    {
        var tracker = CreateTracker();

        tracker.MarkCompleted("step-1", "fp1", "Apply registry key");

        tracker.IsCompleted("step-1").Should().BeTrue();
    }

    [Fact]
    public void MarkCompleted_DuplicateStep_NoDuplicate()
    {
        var tracker = CreateTracker();

        tracker.MarkCompleted("step-1", "fp1", "First");
        tracker.MarkCompleted("step-1", "fp2", "Second");

        var ops = tracker.GetCompletedOperations();
        ops.Should().ContainSingle(o => o.Key == "step-1");
    }

    [Fact]
    public void MarkCompleted_PersistsAcrossLoad()
    {
        var root = MakeBundleRoot();
        var tracker1 = new IdempotencyTracker(root);
        tracker1.MarkCompleted("step-persist", "fp", "Persisted step");

        // New tracker instance reads the same file
        var tracker2 = new IdempotencyTracker(root);

        tracker2.IsCompleted("step-persist").Should().BeTrue();
    }

    // ── Load behavior ────────────────────────────────────────────────────────

    [Fact]
    public void Load_MissingFile_ReturnsEmptyTracker()
    {
        var tracker = CreateTracker(); // fresh bundle root, no existing file

        tracker.GetCompletedOperations().Should().BeEmpty();
    }

    [Fact]
    public void Load_ExistingFile_RestoresState()
    {
        var root = MakeBundleRoot();
        var writer = new IdempotencyTracker(root);
        writer.MarkCompleted("op-a", "fingerA", "Step A");
        writer.MarkCompleted("op-b", "fingerB", "Step B");

        var reader = new IdempotencyTracker(root);

        reader.IsCompleted("op-a").Should().BeTrue();
        reader.IsCompleted("op-b").Should().BeTrue();
    }

    // ── GetCompletedOperations ───────────────────────────────────────────────

    [Fact]
    public void GetCompletedSteps_ReturnsAll()
    {
        var tracker = CreateTracker();
        tracker.MarkCompleted("x", "fp-x", "X");
        tracker.MarkCompleted("y", "fp-y", "Y");
        tracker.MarkCompleted("z", "fp-z", "Z");

        var ops = tracker.GetCompletedOperations();

        ops.Select(o => o.Key).Should().BeEquivalentTo(["x", "y", "z"]);
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_ClearsAllSteps()
    {
        var tracker = CreateTracker();
        tracker.MarkCompleted("step-a", "fp", "Step A");
        tracker.MarkCompleted("step-b", "fp", "Step B");

        tracker.Reset();

        tracker.GetCompletedOperations().Should().BeEmpty();
        tracker.IsCompleted("step-a").Should().BeFalse();
    }

    // ── Thread safety ────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkCompleted_ThreadSafe_NoConcurrentDataLoss()
    {
        const int threadCount = 20;
        var tracker = CreateTracker();

        var tasks = Enumerable.Range(0, threadCount)
            .Select(i => Task.Run(() => tracker.MarkCompleted($"step-{i}", $"fp-{i}", $"Step {i}")))
            .ToArray();

        await Task.WhenAll(tasks);

        var completed = tracker.GetCompletedOperations();
        completed.Should().HaveCount(threadCount,
            "all 20 concurrent MarkCompleted calls must be persisted without data loss");
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
