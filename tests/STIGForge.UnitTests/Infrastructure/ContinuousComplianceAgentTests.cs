using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Infrastructure.System;

namespace STIGForge.UnitTests.Infrastructure;

public sealed class ContinuousComplianceAgentTests : IDisposable
{
  private readonly string _tempDir;

  public ContinuousComplianceAgentTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-compliance-agent-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
  }

  [Fact]
  public async Task Agent_ExecutesComplianceCheckOnSchedule()
  {
    var bundleRoot = CreateBundleRoot("schedule", """
      {
        "results": [
          { "ruleId": "SV-1001", "status": "NotAFinding" }
        ]
      }
      """);
    var logger = new TestLogger<ContinuousComplianceAgent>();
    var auditTrail = new RecordingAuditTrailService();
    var driftService = new DriftDetectionService(new InMemoryDriftRepository());
    var agent = CreateAgent(
      driftService,
      logger,
      bundleRoot,
      TimeSpan.FromMilliseconds(40),
      false,
      null,
      auditTrail);
    using var cts = new CancellationTokenSource();
    var runTask = RunExecuteAsync(agent, cts.Token);

    var completed = await WaitForAsync(() => auditTrail.Entries.Count >= 2, TimeSpan.FromSeconds(3));
    cts.Cancel();
    await runTask;

    completed.Should().BeTrue();
    auditTrail.Entries.Count.Should().BeGreaterThanOrEqualTo(2);
  }

  [Fact]
  public async Task Agent_StopsOnCancellation()
  {
    var bundleRoot = CreateBundleRoot("stop", """
      {
        "results": [
          { "ruleId": "SV-1001", "status": "Open" }
        ]
      }
      """);
    var logger = new TestLogger<ContinuousComplianceAgent>();
    var driftService = new DriftDetectionService(new InMemoryDriftRepository());
    var agent = CreateAgent(
      driftService,
      logger,
      bundleRoot,
      TimeSpan.FromSeconds(10));
    using var cts = new CancellationTokenSource();
    var runTask = RunExecuteAsync(agent, cts.Token);

    cts.Cancel();
    var stopTask = runTask;
    var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(2)));

    completed.Should().Be(stopTask);
    logger.Messages.Should().Contain(m => m.Contains("Continuous Compliance Agent stopped", StringComparison.Ordinal));
  }

  [Fact]
  public async Task Agent_LogsDriftEvents()
  {
    var bundleRoot = CreateBundleRoot("drift-log", """
      {
        "results": [
          { "ruleId": "SV-1001", "status": "NotAFinding" },
          { "ruleId": "SV-1002", "status": "Open" }
        ]
      }
      """);
    var logger = new TestLogger<ContinuousComplianceAgent>();
    var driftService = new DriftDetectionService(new InMemoryDriftRepository());
    var agent = CreateAgent(
      driftService,
      logger,
      bundleRoot,
      TimeSpan.FromSeconds(1));
    using var cts = new CancellationTokenSource();
    var runTask = RunExecuteAsync(agent, cts.Token);

    var logged = await WaitForAsync(
      () => logger.Messages.Any(m => m.Contains("Drift events: 2", StringComparison.Ordinal)),
      TimeSpan.FromSeconds(3));
    cts.Cancel();
    await runTask;

    logged.Should().BeTrue();
  }

  [Fact]
  public async Task Agent_RecordsAuditTrailEntries()
  {
    var bundleRoot = CreateBundleRoot("audit", """
      {
        "results": [
          { "ruleId": "SV-1200", "status": "NotAFinding" }
        ]
      }
      """);
    var logger = new TestLogger<ContinuousComplianceAgent>();
    var auditTrail = new RecordingAuditTrailService();
    var driftService = new DriftDetectionService(new InMemoryDriftRepository());
    var agent = CreateAgent(
      driftService,
      logger,
      bundleRoot,
      TimeSpan.FromSeconds(1),
      false,
      null,
      auditTrail);
    using var cts = new CancellationTokenSource();
    var runTask = RunExecuteAsync(agent, cts.Token);

    var recorded = await WaitForAsync(() => auditTrail.Entries.Count >= 1, TimeSpan.FromSeconds(3));
    cts.Cancel();
    await runTask;

    recorded.Should().BeTrue();
    var entry = auditTrail.Entries[0];
    entry.Action.Should().Be("ContinuousCompliance");
    entry.Target.Should().Be(bundleRoot);
    entry.Result.Should().Be("Success");
  }

  [Fact]
  public async Task Agent_HandlesCheckFailuresGracefully()
  {
    var missingBundleRoot = Path.Combine(_tempDir, "missing-bundle");
    var logger = new TestLogger<ContinuousComplianceAgent>();
    var driftService = new DriftDetectionService(new InMemoryDriftRepository());
    var agent = CreateAgent(
      driftService,
      logger,
      missingBundleRoot,
      TimeSpan.FromMilliseconds(40));
    using var cts = new CancellationTokenSource();
    var runTask = RunExecuteAsync(agent, cts.Token);

    var hasError = await WaitForAsync(
      () => logger.Errors.Any(e => e.Contains("Compliance check failed", StringComparison.Ordinal)),
      TimeSpan.FromSeconds(3));
    cts.Cancel();
    await runTask;

    hasError.Should().BeTrue();
  }

  [Fact]
  public async Task Agent_ForwardsDriftEventsToAuditLogForwarder()
  {
    var bundleRoot = CreateBundleRoot("forward", """
      {
        "results": [
          { "ruleId": "SV-1001", "status": "NotAFinding" }
        ]
      }
      """);

    using var udpServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var port = ((IPEndPoint)udpServer.Client.LocalEndPoint!).Port;
    var forwarder = new AuditLogForwarder(new SyslogForwarder("127.0.0.1", port, "udp"));
    var logger = new TestLogger<ContinuousComplianceAgent>();
    var driftService = new DriftDetectionService(new InMemoryDriftRepository());
    var agent = CreateAgent(
      driftService,
      logger,
      bundleRoot,
      TimeSpan.FromSeconds(1),
      false,
      forwarder,
      null);
    using var cts = new CancellationTokenSource();
    var runTask = RunExecuteAsync(agent, cts.Token);

    var message = await ReceiveUdpAsync(udpServer, TimeSpan.FromSeconds(3));
    cts.Cancel();
    await runTask;

    message.Should().NotBeNull();
    message!.Should().Contain("DRIFT-001");
    message.Should().Contain("ruleId=SV-1001");
  }

  private string CreateBundleRoot(string name, string consolidatedResultsJson)
  {
    var root = Path.Combine(_tempDir, name);
    var verifyDir = Path.Combine(root, "Verify", "run-1");
    Directory.CreateDirectory(verifyDir);
    File.WriteAllText(Path.Combine(verifyDir, "consolidated-results.json"), consolidatedResultsJson);
    return root;
  }

  private static async Task<string?> ReceiveUdpAsync(UdpClient udpClient, TimeSpan timeout)
  {
    using var cts = new CancellationTokenSource(timeout);
    try
    {
      var result = await udpClient.ReceiveAsync(cts.Token);
      return Encoding.UTF8.GetString(result.Buffer);
    }
    catch (OperationCanceledException)
    {
      return null;
    }
  }

  private static async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan timeout)
  {
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
      if (condition())
        return true;

      await Task.Delay(25);
    }

    return condition();
  }

  private static Task RunExecuteAsync(ContinuousComplianceAgent agent, CancellationToken cancellationToken)
  {
    var method = typeof(ContinuousComplianceAgent).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic);
    method.Should().NotBeNull();
    return (Task)method!.Invoke(agent, [cancellationToken])!;
  }

  private static ContinuousComplianceAgent CreateAgent(
    DriftDetectionService driftService,
    object logger,
    string bundleRoot,
    TimeSpan checkInterval,
    bool autoRemediate = false,
    AuditLogForwarder? auditForwarder = null,
    IAuditTrailService? auditTrail = null)
  {
    var agent = Activator.CreateInstance(
      typeof(ContinuousComplianceAgent),
      driftService,
      logger,
      bundleRoot,
      (TimeSpan?)checkInterval,
      autoRemediate,
      10,           // maxDriftEventsToForward
      auditForwarder,
      auditTrail,
      null);        // config

    agent.Should().NotBeNull();
    return (ContinuousComplianceAgent)agent!;
  }

  private sealed class TestLogger<T> : ILogger<T>
  {
    private readonly List<LogEntry> _entries = new();
    private readonly object _sync = new();

    public IReadOnlyList<string> Messages
    {
      get
      {
        lock (_sync)
          return _entries.Select(e => e.Message).ToList();
      }
    }

    public IReadOnlyList<string> Errors
    {
      get
      {
        lock (_sync)
          return _entries
            .Where(e => e.Level == LogLevel.Error)
            .Select(e => e.Message)
            .ToList();
      }
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
      return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
      return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
      lock (_sync)
      {
        _entries.Add(new LogEntry(logLevel, formatter(state, exception)));
      }
    }

    private readonly record struct LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
      public static readonly NullScope Instance = new();

      public void Dispose()
      {
      }
    }
  }

  private sealed class RecordingAuditTrailService : IAuditTrailService
  {
    private readonly List<AuditEntry> _entries = new();
    private readonly object _sync = new();

    public IReadOnlyList<AuditEntry> Entries
    {
      get
      {
        lock (_sync)
          return _entries.ToList();
      }
    }

    public Task RecordAsync(AuditEntry entry, CancellationToken ct)
    {
      lock (_sync)
      {
        _entries.Add(entry);
      }

      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct)
    {
      lock (_sync)
      {
        return Task.FromResult<IReadOnlyList<AuditEntry>>(_entries.ToList());
      }
    }

    public Task<bool> VerifyIntegrityAsync(CancellationToken ct)
    {
      return Task.FromResult(true);
    }
  }

  private sealed class InMemoryDriftRepository : IDriftRepository
  {
    private readonly List<DriftSnapshot> _snapshots = new();
    private readonly object _sync = new();

    public Task SaveAsync(DriftSnapshot snapshot, CancellationToken ct)
    {
      lock (_sync)
      {
        var existing = _snapshots.FindIndex(s => string.Equals(s.SnapshotId, snapshot.SnapshotId, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
          _snapshots[existing] = snapshot;
        else
          _snapshots.Add(snapshot);
      }

      return Task.CompletedTask;
    }

    public Task SaveBatchAsync(IReadOnlyList<DriftSnapshot> snapshots, CancellationToken ct)
    {
      lock (_sync)
      {
        foreach (var snapshot in snapshots)
        {
          var existing = _snapshots.FindIndex(s => string.Equals(s.SnapshotId, snapshot.SnapshotId, StringComparison.OrdinalIgnoreCase));
          if (existing >= 0)
            _snapshots[existing] = snapshot;
          else
            _snapshots.Add(snapshot);
        }
      }

      return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DriftSnapshot>> GetDriftHistoryAsync(string bundleRoot, string? ruleId, int limit, CancellationToken ct)
    {
      lock (_sync)
      {
        var query = _snapshots
          .Where(s => string.Equals(s.BundleRoot, bundleRoot, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(ruleId))
          query = query.Where(s => string.Equals(s.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));

        var rows = query
          .OrderByDescending(s => s.DetectedAt)
          .ThenByDescending(s => s.SnapshotId, StringComparer.OrdinalIgnoreCase)
          .Take(limit)
          .ToList();

        return Task.FromResult<IReadOnlyList<DriftSnapshot>>(rows);
      }
    }

    public async Task<DriftSnapshot?> GetLatestSnapshotAsync(string bundleRoot, string ruleId, CancellationToken ct)
    {
      var rows = await GetDriftHistoryAsync(bundleRoot, ruleId, 1, ct);
      return rows.Count > 0 ? rows[0] : null;
    }

    public Task<IReadOnlyList<DriftSnapshot>> GetLatestByRuleAsync(string bundleRoot, CancellationToken ct)
    {
      lock (_sync)
      {
        var rows = _snapshots
          .Where(s => string.Equals(s.BundleRoot, bundleRoot, StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(s => s.DetectedAt)
          .ThenByDescending(s => s.SnapshotId, StringComparer.OrdinalIgnoreCase)
          .GroupBy(s => s.RuleId, StringComparer.OrdinalIgnoreCase)
          .Select(g => g.First())
          .ToList();

        return Task.FromResult<IReadOnlyList<DriftSnapshot>>(rows);
      }
    }
  }
}
