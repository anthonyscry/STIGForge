using FluentAssertions;
using STIGForge.Apply;

namespace STIGForge.UnitTests.Build;

public class IdempotencyTrackerTests : IDisposable
{
  private readonly string _bundleRoot;

  public IdempotencyTrackerTests()
  {
    _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-idem-test-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(_bundleRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_bundleRoot, true); } catch { }
  }

  [Fact]
  public void NewTracker_NoOperationsCompleted()
  {
    var tracker = new IdempotencyTracker(_bundleRoot);

    tracker.IsCompleted("step-1").Should().BeFalse();
    tracker.GetCompletedOperations().Should().BeEmpty();
  }

  [Fact]
  public void MarkCompleted_IsCompleted_ReturnsTrue()
  {
    var tracker = new IdempotencyTracker(_bundleRoot);

    tracker.MarkCompleted("step-1", "hash-abc", "Apply security policy");

    tracker.IsCompleted("step-1").Should().BeTrue();
    tracker.IsCompleted("step-2").Should().BeFalse();
  }

  [Fact]
  public void FingerprintMatches_SameFingerprint_ReturnsTrue()
  {
    var tracker = new IdempotencyTracker(_bundleRoot);
    tracker.MarkCompleted("step-1", "hash-abc", "Step 1");

    tracker.FingerprintMatches("step-1", "hash-abc").Should().BeTrue();
    tracker.FingerprintMatches("step-1", "hash-xyz").Should().BeFalse();
    tracker.FingerprintMatches("step-unknown", "hash-abc").Should().BeFalse();
  }

  [Fact]
  public void Reset_ClearsAllOperations()
  {
    var tracker = new IdempotencyTracker(_bundleRoot);
    tracker.MarkCompleted("step-1", "hash-1", "Step 1");
    tracker.MarkCompleted("step-2", "hash-2", "Step 2");
    tracker.GetCompletedOperations().Should().HaveCount(2);

    tracker.Reset();

    tracker.IsCompleted("step-1").Should().BeFalse();
    tracker.IsCompleted("step-2").Should().BeFalse();
    tracker.GetCompletedOperations().Should().BeEmpty();
  }

  [Fact]
  public void GetCompletedOperations_ReturnsAll()
  {
    var tracker = new IdempotencyTracker(_bundleRoot);
    tracker.MarkCompleted("step-a", "hash-a", "Description A");
    tracker.MarkCompleted("step-b", "hash-b", "Description B");

    var ops = tracker.GetCompletedOperations();

    ops.Should().HaveCount(2);
    ops.Should().Contain(o => o.Key == "step-a" && o.Description == "Description A");
    ops.Should().Contain(o => o.Key == "step-b" && o.Description == "Description B");
  }

  [Fact]
  public void Persistence_SurvivesReload()
  {
    var tracker1 = new IdempotencyTracker(_bundleRoot);
    tracker1.MarkCompleted("persistent-op", "fp-123", "Persisted");

    // Create new tracker from same bundle root (simulates restart)
    var tracker2 = new IdempotencyTracker(_bundleRoot);

    tracker2.IsCompleted("persistent-op").Should().BeTrue();
    tracker2.FingerprintMatches("persistent-op", "fp-123").Should().BeTrue();
  }
}
