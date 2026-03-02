using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Services;

namespace STIGForge.Infrastructure.System;

public sealed class ContinuousComplianceAgent : BackgroundService
{
  private readonly DriftDetectionService _driftService;
  private readonly AuditLogForwarder? _auditForwarder;
  private readonly IAuditTrailService? _auditTrail;
  private readonly ILogger<ContinuousComplianceAgent> _logger;
  private readonly TimeSpan _checkInterval;
  private readonly string _bundleRoot;
  private readonly bool _autoRemediate;

  public ContinuousComplianceAgent(
    DriftDetectionService driftService,
    ILogger<ContinuousComplianceAgent> logger,
    string bundleRoot,
    TimeSpan? checkInterval = null,
    bool autoRemediate = false,
    AuditLogForwarder? auditForwarder = null,
    IAuditTrailService? auditTrail = null)
  {
    _driftService = driftService ?? throw new ArgumentNullException(nameof(driftService));
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _bundleRoot = bundleRoot ?? throw new ArgumentNullException(nameof(bundleRoot));
    _checkInterval = checkInterval ?? TimeSpan.FromHours(24);
    _autoRemediate = autoRemediate;
    _auditForwarder = auditForwarder;
    _auditTrail = auditTrail;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Continuous Compliance Agent started for {BundleRoot}", _bundleRoot);
    _logger.LogInformation("Check interval: {Interval}, Auto-remediate: {AutoRemediate}", _checkInterval, _autoRemediate);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await RunComplianceCheckAsync(stoppingToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Compliance check failed");
      }

      try
      {
        await Task.Delay(_checkInterval, stoppingToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }

    _logger.LogInformation("Continuous Compliance Agent stopped");
  }

  private async Task RunComplianceCheckAsync(CancellationToken ct)
  {
    _logger.LogDebug("Running scheduled compliance check");

    var stopwatch = Stopwatch.StartNew();
    var result = await _driftService.CheckBundleAsync(_bundleRoot, _autoRemediate, ct).ConfigureAwait(false);
    stopwatch.Stop();

    _logger.LogInformation(
      "Compliance check completed in {ElapsedMs}ms. Drift events: {DriftCount}, Auto-remediated: {RemediatedCount}",
      stopwatch.ElapsedMilliseconds,
      result.DriftEvents.Count,
      result.AutoRemediatedRuleIds.Count);

    if (_auditTrail != null)
    {
      await _auditTrail.RecordAsync(new AuditEntry
      {
        Timestamp = DateTimeOffset.UtcNow,
        User = Environment.UserName,
        Machine = Environment.MachineName,
        Action = "ContinuousCompliance",
        Target = _bundleRoot,
        Result = "Success",
        Detail = $"Scheduled check: {result.DriftEvents.Count} drift events detected"
      }, ct).ConfigureAwait(false);
    }

    if (result.DriftEvents.Count > 0 && _auditForwarder != null)
    {
      foreach (var drift in result.DriftEvents.Take(10))
      {
        await _auditForwarder.ForwardDriftEventAsync(
          _bundleRoot,
          drift.RuleId,
          drift.ChangeType,
          drift.PreviousState ?? "unknown",
          drift.CurrentState,
          ct).ConfigureAwait(false);
      }
    }

    if (result.RemediationErrors.Count > 0)
    {
      _logger.LogWarning("Remediation errors: {ErrorCount}", result.RemediationErrors.Count);
      foreach (var error in result.RemediationErrors)
      {
        _logger.LogWarning("  - {Error}", error);
      }
    }
  }
}

public sealed class ComplianceAgentFactory
{
  private readonly IServiceProvider _services;

  public ComplianceAgentFactory(IServiceProvider services)
  {
    _services = services ?? throw new ArgumentNullException(nameof(services));
  }

  public ContinuousComplianceAgent CreateAgent(string bundleRoot, TimeSpan? interval = null, bool autoRemediate = false)
  {
    var driftService = _services.GetRequiredService<DriftDetectionService>();
    var logger = _services.GetRequiredService<ILogger<ContinuousComplianceAgent>>();
    var auditForwarder = _services.GetService<AuditLogForwarder>();
    var auditTrail = _services.GetService<IAuditTrailService>();

    return new ContinuousComplianceAgent(
      driftService,
      logger,
      bundleRoot,
      interval,
      autoRemediate,
      auditForwarder,
      auditTrail);
  }
}

public static class WindowsServiceInstaller
{
  public static void InstallService(string serviceName, string displayName, string executablePath)
  {
    if (OperatingSystem.IsWindows())
    {
      var psi = new ProcessStartInfo
      {
        FileName = "sc.exe",
        Arguments = $"create {serviceName} binPath= \"{executablePath}\" displayName= \"{displayName}\" start= auto",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = Process.Start(psi);
      process?.WaitForExit();
    }
  }

  public static void UninstallService(string serviceName)
  {
    if (OperatingSystem.IsWindows())
    {
      var psi = new ProcessStartInfo
      {
        FileName = "sc.exe",
        Arguments = $"delete {serviceName}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      };

      using var process = Process.Start(psi);
      process?.WaitForExit();
    }
  }
}
