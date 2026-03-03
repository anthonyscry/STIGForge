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
  private readonly CklMergeService? _cklMerge;
  private readonly EmassPackageGenerator? _emass;

  public PhaseCCommandService(
    DriftDetectionService drift,
    RollbackService rollback,
    GpoConflictDetector gpoConflicts,
    NessusImporter? nessus = null,
    AcasCorrelationService? acas = null,
    CklImporter? cklImporter = null,
    CklExporter? cklExporter = null,
    EmassPackageGenerator? emass = null,
    CklMergeService? cklMerge = null)
  {
    _drift = drift ?? throw new ArgumentNullException(nameof(drift));
    _rollback = rollback ?? throw new ArgumentNullException(nameof(rollback));
    _gpoConflicts = gpoConflicts ?? throw new ArgumentNullException(nameof(gpoConflicts));
    _nessus = nessus;
    _acas = acas;
    _cklImporter = cklImporter;
    _cklExporter = cklExporter;
    _emass = emass;
    _cklMerge = cklMerge;
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

  public async Task<AcasCorrelationResult> AcasImportAsync(string nessusFilePath, string? bundlePath, CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (_acas == null)
      throw new InvalidOperationException("AcasCorrelationService is not registered.");
    return await _acas.CorrelateAsync(nessusFilePath, bundlePath, ct).ConfigureAwait(false);
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

  public Task<CklMergeResult> MergeAsync(
    CklChecklist checklist,
    IReadOnlyList<ControlResult> existingResults,
    CklConflictResolutionStrategy strategy,
    CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (_cklMerge == null)
      throw new InvalidOperationException("CklMergeService is not registered.");

    return _cklMerge.MergeAsync(checklist, existingResults, strategy, ct);
  }

  public async Task<CklMergeResult> CklMergeAsync(
    string cklFilePath,
    string bundlePath,
    CklConflictResolutionStrategy strategy,
    CancellationToken ct)
  {
    ct.ThrowIfCancellationRequested();
    if (_cklImporter == null)
      throw new InvalidOperationException("CklImporter is not registered.");

    var checklist = _cklImporter.Import(cklFilePath);
    var existingResults = LoadVerifyControlResults(bundlePath);
    return await MergeAsync(checklist, existingResults, strategy, ct).ConfigureAwait(false);
  }

  private static IReadOnlyList<ControlResult> LoadVerifyControlResults(string bundleRoot)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot))
      throw new ArgumentException("Value cannot be null or empty.", nameof(bundleRoot));

    var verifyRoot = Path.Combine(bundleRoot, "Verify");
    if (!Directory.Exists(verifyRoot))
      throw new DirectoryNotFoundException("Verify output directory not found: " + verifyRoot);

    var reportPath = Directory
      .GetFiles(verifyRoot, "consolidated-results.json", SearchOption.AllDirectories)
      .OrderByDescending(File.GetLastWriteTimeUtc)
      .FirstOrDefault();

    if (string.IsNullOrWhiteSpace(reportPath))
      throw new FileNotFoundException("No consolidated verify report found under bundle Verify directory.", verifyRoot);

    using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
    if (!TryGetPropertyCaseInsensitive(document.RootElement, "results", out var resultsElement)
      || resultsElement.ValueKind != JsonValueKind.Array)
    {
      throw new InvalidOperationException("Verify report is missing a results array: " + reportPath);
    }

    var controlResults = new List<ControlResult>();
    foreach (var item in resultsElement.EnumerateArray())
    {
      if (item.ValueKind != JsonValueKind.Object)
        continue;

      var ruleId = ReadStringProperty(item, "ruleId") ?? string.Empty;
      var vulnId = ReadStringProperty(item, "vulnId");
      var status = ReadStringProperty(item, "status") ?? "NotReviewed";

      if (string.IsNullOrWhiteSpace(ruleId) && string.IsNullOrWhiteSpace(vulnId))
        continue;

      controlResults.Add(new ControlResult
      {
        RuleId = ruleId,
        VulnId = vulnId,
        Status = status,
        Comments = ReadStringProperty(item, "comments"),
        FindingDetails = ReadStringProperty(item, "findingDetails"),
        Severity = ReadStringProperty(item, "severity"),
        Title = ReadStringProperty(item, "title"),
        SourceFile = reportPath
      });
    }

    return controlResults;
  }

  private static string? ReadStringProperty(JsonElement element, string propertyName)
  {
    if (!TryGetPropertyCaseInsensitive(element, propertyName, out var value))
      return null;

    return value.ValueKind == JsonValueKind.String
      ? value.GetString()
      : null;
  }

  private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
  {
    if (element.ValueKind != JsonValueKind.Object)
    {
      value = default;
      return false;
    }

    if (element.TryGetProperty(propertyName, out value))
      return true;

    foreach (var property in element.EnumerateObject())
    {
      if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
      {
        value = property.Value;
        return true;
      }
    }

    value = default;
    return false;
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
