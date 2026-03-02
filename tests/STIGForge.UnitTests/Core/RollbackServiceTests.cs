using System.Diagnostics;
using System.Text;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class RollbackServiceTests : IDisposable
{
  private readonly string _bundleRoot;

  public RollbackServiceTests()
  {
    _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-rollback-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_bundleRoot);
    Directory.CreateDirectory(Path.Combine(_bundleRoot, "Manifest"));
    Directory.CreateDirectory(Path.Combine(_bundleRoot, "Manual"));
    Directory.CreateDirectory(Path.Combine(_bundleRoot, "Apply"));

    File.WriteAllText(Path.Combine(_bundleRoot, "Manifest", "manifest.json"), "{}");
    File.WriteAllText(Path.Combine(_bundleRoot, "Manifest", "pack_controls.json"), "[]");
    File.WriteAllText(Path.Combine(_bundleRoot, "Manifest", "overlays.json"), "[]");
    File.WriteAllText(Path.Combine(_bundleRoot, "Manual", "answers.json"), "{}");
    File.WriteAllText(Path.Combine(_bundleRoot, "Apply", "RunApply.ps1"), "Write-Output 'ok'");
  }

  public void Dispose()
  {
    try { Directory.Delete(_bundleRoot, true); } catch { }
  }

  [Fact]
  public async Task CapturePreHardeningState_CreatesSnapshotAndRollbackScript()
  {
    var repo = new InMemoryRollbackRepository();
    var processRunner = new TestProcessRunner();
    var now = new DateTimeOffset(2026, 2, 20, 8, 30, 0, TimeSpan.Zero);
    var service = new RollbackService(repo, processRunner, new TestClock(now));

    var snapshot = await service.CapturePreHardeningStateAsync(_bundleRoot, "before apply", CancellationToken.None);

    snapshot.SnapshotId.Should().NotBeNullOrWhiteSpace();
    snapshot.BundleRoot.Should().Be(_bundleRoot);
    snapshot.Description.Should().Be("before apply");
    snapshot.CreatedAt.Should().Be(now);
    snapshot.RegistryKeys.Should().NotBeEmpty();
    snapshot.FilePaths.Should().NotBeEmpty();
    snapshot.ServiceStates.Should().NotBeEmpty();
    snapshot.GpoSettings.Should().Contain(g => g.GpoName == "Default Domain Policy");
    snapshot.RollbackScriptPath.Should().NotBeNullOrWhiteSpace();
    File.Exists(snapshot.RollbackScriptPath).Should().BeTrue();

    var persisted = await repo.GetAsync(snapshot.SnapshotId, CancellationToken.None);
    persisted.Should().NotBeNull();
  }

  [Fact]
  public async Task ExecuteRollbackAsync_RunsGeneratedScript()
  {
    var repo = new InMemoryRollbackRepository();
    var processRunner = new TestProcessRunner();
    var service = new RollbackService(repo, processRunner, new TestClock(new DateTimeOffset(2026, 2, 20, 8, 30, 0, TimeSpan.Zero)));

    var snapshot = await service.CapturePreHardeningStateAsync(_bundleRoot, "before apply", CancellationToken.None);
    var result = await service.ExecuteRollbackAsync(snapshot.SnapshotId, CancellationToken.None);

    result.Success.Should().BeTrue();
    result.ExitCode.Should().Be(0);
    processRunner.Commands.Should().Contain(c => c.FileName == "powershell.exe" && c.Arguments.Contains("-File", StringComparison.Ordinal));
  }

  [Fact]
  public async Task ListSnapshotsAsync_ReturnsMostRecentFirst()
  {
    var repo = new InMemoryRollbackRepository();
    var service = new RollbackService(repo, new TestProcessRunner(), new TestClock(new DateTimeOffset(2026, 2, 20, 8, 30, 0, TimeSpan.Zero)));

    var first = await service.CapturePreHardeningStateAsync(_bundleRoot, "first", CancellationToken.None);
    var second = await service.CapturePreHardeningStateAsync(_bundleRoot, "second", CancellationToken.None);

    var snapshots = await service.ListSnapshotsAsync(_bundleRoot, 10, CancellationToken.None);

    snapshots.Should().HaveCount(2);
    snapshots[0].SnapshotId.Should().Be(second.SnapshotId);
    snapshots[1].SnapshotId.Should().Be(first.SnapshotId);
  }

  private sealed class TestClock : IClock
  {
    private DateTimeOffset _now;

    public TestClock(DateTimeOffset now)
    {
      _now = now;
    }

    public DateTimeOffset Now
    {
      get
      {
        var value = _now;
        _now = _now.AddMinutes(1);
        return value;
      }
    }
  }

  private sealed class InMemoryRollbackRepository : IRollbackRepository
  {
    private readonly List<RollbackSnapshot> _items = new();
    private readonly object _sync = new();

    public Task SaveAsync(RollbackSnapshot snapshot, CancellationToken ct)
    {
      lock (_sync)
      {
        var index = _items.FindIndex(i => string.Equals(i.SnapshotId, snapshot.SnapshotId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
          _items[index] = snapshot;
        else
          _items.Add(snapshot);
      }

      return Task.CompletedTask;
    }

    public Task<RollbackSnapshot?> GetAsync(string snapshotId, CancellationToken ct)
    {
      lock (_sync)
      {
        var item = _items.FirstOrDefault(i => string.Equals(i.SnapshotId, snapshotId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(item);
      }
    }

    public Task<IReadOnlyList<RollbackSnapshot>> ListByBundleAsync(string bundleRoot, int limit, CancellationToken ct)
    {
      lock (_sync)
      {
        var rows = _items
          .Where(i => string.Equals(i.BundleRoot, bundleRoot, StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(i => i.CreatedAt)
          .Take(limit)
          .ToList();
        return Task.FromResult<IReadOnlyList<RollbackSnapshot>>(rows);
      }
    }
  }

  private sealed class TestProcessRunner : IProcessRunner
  {
    public List<(string FileName, string Arguments)> Commands { get; } = new();

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
      Commands.Add((startInfo.FileName, startInfo.Arguments));

      if (string.Equals(startInfo.FileName, "gpresult.exe", StringComparison.OrdinalIgnoreCase))
      {
        return Task.FromResult(new ProcessResult
        {
          ExitCode = 0,
          StandardOutput = """
Computer Settings
------------------
Applied Group Policy Objects
-----------------------------
    Default Domain Policy

The following GPOs were not applied because they were filtered out
"""
        });
      }

      if (string.Equals(startInfo.FileName, "powershell.exe", StringComparison.OrdinalIgnoreCase)
          && startInfo.Arguments.Contains("-EncodedCommand", StringComparison.OrdinalIgnoreCase))
      {
        var script = DecodeEncodedCommand(startInfo.Arguments);
        if (script.Contains("Get-CimInstance Win32_Service", StringComparison.Ordinal))
        {
          return Task.FromResult(new ProcessResult
          {
            ExitCode = 0,
            StandardOutput = "Running|Auto"
          });
        }

        return Task.FromResult(new ProcessResult
        {
          ExitCode = 0,
          StandardOutput = "1"
        });
      }

      return Task.FromResult(new ProcessResult
      {
        ExitCode = 0,
        StandardOutput = "rollback complete"
      });
    }

    public bool ExistsInPath(string fileName)
    {
      return true;
    }

    private static string DecodeEncodedCommand(string arguments)
    {
      var marker = "-EncodedCommand ";
      var index = arguments.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
      if (index < 0)
        return string.Empty;

      var encoded = arguments.Substring(index + marker.Length).Trim();
      var bytes = Convert.FromBase64String(encoded);
      return Encoding.Unicode.GetString(bytes);
    }
  }
}
