using STIGForge.Core.Models;
using System.Diagnostics;
using System.Text.Json;

namespace STIGForge.Core.Services;

public sealed class PhaseCCommandService
{
  private readonly DriftDetectionService _drift;
  private readonly RollbackService _rollback;
  private readonly GpoConflictDetector _gpoConflicts;
  private readonly NessusImporter? _nessus;
  private readonly AcasCorrelationService? _acas;
  private readonly CklImporter? _cklImporter;
  private readonly CklExporter? _cklExporter;
  private readonly EmassPackageGenerator? _emass;

  public PhaseCCommandService(
    DriftDetectionService drift,
    RollbackService rollback,
    GpoConflictDetector gpoConflicts,
    NessusImporter? nessus = null,
    AcasCorrelationService? acas = null,
    CklImporter? cklImporter = null,
    CklExporter? cklExporter = null,
    EmassPackageGenerator? emass = null)
  {
    _drift = drift ?? throw new ArgumentNullException(nameof(drift));
    _rollback = rollback ?? throw new ArgumentNullException(nameof(rollback));
    _gpoConflicts = gpoConflicts ?? throw new ArgumentNullException(nameof(gpoConflicts));
    _nessus = nessus;
    _acas = acas;
    _cklImporter = cklImporter;
    _cklExporter = cklExporter;
    _emass = emass;
  }

  public Task<DriftCheckResult> DriftCheckAsync(string bundlePath, bool autoRemediate, CancellationToken ct)
  {
    return _drift.CheckBundleAsync(bundlePath, autoRemediate, ct);
  }

  public Task<IReadOnlyList<DriftSnapshot>> DriftHistoryAsync(string bundlePath, string? ruleId, int limit, CancellationToken ct)
  {
    return _drift.GetHistoryAsync(bundlePath, ruleId, limit, ct);
  }

  public Task<RollbackSnapshot> RollbackCreateAsync(string bundlePath, string description, CancellationToken ct)
  {
    return _rollback.CapturePreHardeningStateAsync(bundlePath, description, ct);
  }

  public Task<RollbackApplyResult> RollbackApplyAsync(string snapshotId, CancellationToken ct)
  {
    return _rollback.ExecuteRollbackAsync(snapshotId, ct);
  }

  public Task<IReadOnlyList<RollbackSnapshot>> RollbackListAsync(string bundlePath, int limit, CancellationToken ct)
  {
    return _rollback.ListSnapshotsAsync(bundlePath, limit, ct);
  }

  public Task<IReadOnlyList<GpoConflict>> GpoConflictsAsync(string bundlePath, CancellationToken ct)
  {
    return _gpoConflicts.DetectConflictsAsync(bundlePath, ct);
  }

  public Task<IReadOnlyList<NessusFinding>> NessusImportAsync(string nessusFilePath, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (_nessus == null)
      throw new InvalidOperationException("NessusImporter is not registered.");
    return Task.FromResult(_nessus.Import(nessusFilePath));
  }

  public Task<AcasCorrelationResult> AcasImportAsync(string nessusFilePath, string? bundlePath, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (_acas == null)
      throw new InvalidOperationException("AcasCorrelationService is not registered.");
    return Task.FromResult(_acas.Correlate(nessusFilePath, bundlePath));
  }

  public Task<CklChecklist> CklImportAsync(string cklFilePath, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (_cklImporter == null)
      throw new InvalidOperationException("CklImporter is not registered.");
    return Task.FromResult(_cklImporter.Import(cklFilePath));
  }

  public Task<string> CklExportAsync(string bundlePath, string outputPath, string hostName, string stigTitle, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (_cklExporter == null)
      throw new InvalidOperationException("CklExporter is not registered.");

    var controlsPath = Path.Combine(bundlePath, "Manifest", "pack_controls.json");
    var controls = File.Exists(controlsPath)
      ? JsonSerializer.Deserialize<List<ControlRecord>>(File.ReadAllText(controlsPath)) ?? new List<ControlRecord>()
      : new List<ControlRecord>();

    var results = controls.Select(control => new ControlResult
    {
      RuleId = control.ExternalIds.RuleId ?? control.ControlId,
      VulnId = control.ExternalIds.VulnId,
      Status = "NotReviewed",
      Title = control.Title,
      Severity = control.Severity,
      Comments = string.Empty,
      FindingDetails = string.Empty,
      SourceFile = controlsPath
    }).ToList();

    var checklist = _cklExporter.FromControlResults(results, stigTitle, hostName);
    _cklExporter.Export(checklist, outputPath);
    return Task.FromResult(outputPath);
  }

  public async Task<EmassPackage> EmassPackageAsync(string bundlePath, string systemName, string systemAcronym, string outputDirectory, string? previousPackagePath, CancellationToken ct)
  {
    if (_emass == null)
      throw new InvalidOperationException("EmassPackageGenerator is not registered.");

    var package = await _emass.GeneratePackageAsync(bundlePath, systemName, systemAcronym, previousPackagePath, ct).ConfigureAwait(false);
    await _emass.SavePackageAsync(package, outputDirectory, ct).ConfigureAwait(false);
    return package;
  }

  public Task AgentInstallAsync(string serviceName, string displayName, string executablePath, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (!OperatingSystem.IsWindows())
      return Task.CompletedTask;

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
    return Task.CompletedTask;
  }

  public Task AgentUninstallAsync(string serviceName, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (!OperatingSystem.IsWindows())
      return Task.CompletedTask;

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
    return Task.CompletedTask;
  }

  public Task<string> AgentStatusAsync(string serviceName, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (!OperatingSystem.IsWindows())
      return Task.FromResult("Unsupported on non-Windows host");

    var psi = new ProcessStartInfo
    {
      FileName = "sc.exe",
      Arguments = $"query {serviceName}",
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    using var process = Process.Start(psi);
    process?.WaitForExit();
    return Task.FromResult((process?.StandardOutput.ReadToEnd() ?? string.Empty).Trim());
  }
}
