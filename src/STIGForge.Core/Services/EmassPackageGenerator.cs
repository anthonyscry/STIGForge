using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public sealed class EmassPackageGenerator
{
  private readonly IComplianceTrendRepository? _trendRepo;
  private readonly IExceptionRepository? _exceptionRepo;
  private readonly IAuditTrailService? _auditTrail;
  private readonly IClock _clock;

  public EmassPackageGenerator(
    IComplianceTrendRepository? trendRepo = null,
    IExceptionRepository? exceptionRepo = null,
    IAuditTrailService? auditTrail = null,
    IClock? clock = null)
  {
    _trendRepo = trendRepo;
    _exceptionRepo = exceptionRepo;
    _auditTrail = auditTrail;
    _clock = clock ?? new SystemClock();
  }

  public async Task<EmassPackage> GeneratePackageAsync(
    string bundleRoot,
    string systemName,
    string systemAcronym,
    string? previousPackagePath,
    CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(bundleRoot)) throw new ArgumentException("Bundle root is required.", nameof(bundleRoot));
    if (string.IsNullOrWhiteSpace(systemName)) throw new ArgumentException("System name is required.", nameof(systemName));

    var manifest = await LoadBundleManifestAsync(bundleRoot, ct).ConfigureAwait(false);
    var controls = await LoadControlsAsync(bundleRoot, ct).ConfigureAwait(false);

    var package = new EmassPackage
    {
      PackageId = Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),
      GeneratedAt = _clock.Now,
      SystemName = systemName,
      SystemAcronym = systemAcronym,
      BundleRoot = bundleRoot,

      ControlCorrelationMatrix = GenerateCcm(controls),
      SystemSecurityPlan = GenerateSsp(controls, systemName, systemAcronym),
      Poam = await GeneratePoamAsync(bundleRoot, controls, ct).ConfigureAwait(false),
      EvidenceArtifacts = CollectEvidenceArtifacts(bundleRoot),
      ScanResults = CollectScanResults(bundleRoot),
      ComplianceSummary = await GenerateComplianceSummaryAsync(bundleRoot, ct).ConfigureAwait(false)
    };

    if (!string.IsNullOrWhiteSpace(previousPackagePath) && Directory.Exists(previousPackagePath))
    {
      package.ChangeLog = GenerateChangeLog(previousPackagePath, package);
    }

    return package;
  }

  public async Task SavePackageAsync(EmassPackage package, string outputDirectory, CancellationToken ct)
  {
    Directory.CreateDirectory(outputDirectory);

    var packageDir = Path.Combine(outputDirectory, $"eMASS_{package.SystemAcronym}_{package.PackageId}_{package.GeneratedAt:yyyyMMdd}");
    Directory.CreateDirectory(packageDir);

    await File.WriteAllTextAsync(
      Path.Combine(packageDir, "package-manifest.json"),
      JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = true }),
      ct).ConfigureAwait(false);

    await File.WriteAllTextAsync(
      Path.Combine(packageDir, "control-correlation-matrix.json"),
      JsonSerializer.Serialize(package.ControlCorrelationMatrix, new JsonSerializerOptions { WriteIndented = true }),
      ct).ConfigureAwait(false);

    await File.WriteAllTextAsync(
      Path.Combine(packageDir, "system-security-plan.json"),
      JsonSerializer.Serialize(package.SystemSecurityPlan, new JsonSerializerOptions { WriteIndented = true }),
      ct).ConfigureAwait(false);

    await File.WriteAllTextAsync(
      Path.Combine(packageDir, "poam.json"),
      JsonSerializer.Serialize(package.Poam, new JsonSerializerOptions { WriteIndented = true }),
      ct).ConfigureAwait(false);

    var evidenceDir = Path.Combine(packageDir, "evidence");
    Directory.CreateDirectory(evidenceDir);
    foreach (var artifact in package.EvidenceArtifacts)
    {
      if (File.Exists(artifact.SourcePath))
      {
        var destPath = Path.Combine(evidenceDir, artifact.FileName);
        File.Copy(artifact.SourcePath, destPath, true);
      }
    }

    if (package.ChangeLog != null)
    {
      await File.WriteAllTextAsync(
        Path.Combine(packageDir, "change-log.md"),
        package.ChangeLog,
        ct).ConfigureAwait(false);
    }

    var checksums = ComputeChecksums(packageDir);
    await File.WriteAllTextAsync(
      Path.Combine(packageDir, "sha256-checksums.txt"),
      checksums,
      ct).ConfigureAwait(false);

    if (_auditTrail != null)
    {
      await _auditTrail.RecordAsync(new AuditEntry
      {
        Timestamp = _clock.Now,
        User = Environment.UserName,
        Machine = Environment.MachineName,
        Action = "EmassPackage",
        Target = package.PackageId,
        Result = "Success",
        Detail = $"Generated eMASS package for {package.SystemName}"
      }, ct).ConfigureAwait(false);
    }
  }

  private static async Task<BundleManifest> LoadBundleManifestAsync(string bundleRoot, CancellationToken ct)
  {
    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath))
      return new BundleManifest();

    var json = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
    return JsonSerializer.Deserialize<BundleManifest>(json) ?? new BundleManifest();
  }

  private static async Task<IReadOnlyList<ControlRecord>> LoadControlsAsync(string bundleRoot, CancellationToken ct)
  {
    var controlsPath = Path.Combine(bundleRoot, "Manifest", "pack_controls.json");
    if (!File.Exists(controlsPath))
      return [];

    var json = await File.ReadAllTextAsync(controlsPath, ct).ConfigureAwait(false);
    return JsonSerializer.Deserialize<List<ControlRecord>>(json) ?? new List<ControlRecord>();
  }

  private static ControlCorrelationMatrix GenerateCcm(IReadOnlyList<ControlRecord> controls)
  {
    var ccm = new ControlCorrelationMatrix();

    foreach (var control in controls.OrderBy(c => c.ControlId))
    {
      ccm.Controls.Add(new CcmControlEntry
      {
        ControlId = control.ControlId,
        ControlName = control.Title,
        Applicability = DetermineApplicability(control),
        ImplementationStatus = control.IsManual ? "ManualReviewRequired" : "Planned",
        ResponsibleRole = "SystemOwner",
        ImplementationNarrative = control.Discussion ?? string.Empty,
        Parameters = ExtractParameters(control),
        Inheritance = "SystemSpecific"
      });
    }

    return ccm;
  }

  private static SystemSecurityPlan GenerateSsp(IReadOnlyList<ControlRecord> controls, string systemName, string systemAcronym)
  {
    return new SystemSecurityPlan
    {
      SystemName = systemName,
      SystemAcronym = systemAcronym,
      SystemDescription = $"{systemName} ({systemAcronym}) - STIG-hardened system",
      SystemType = "General Support System",
      SystemStatus = "Operational",
      AuthorizationBoundary = $"{systemAcronym} boundary includes all components within {systemName}",

      ImplementedControls = controls.Count,
      InheritedControls = 0,
      HybridControls = 0,

      ControlImplementations = controls.Select(c => new SspControlImplementation
      {
        ControlId = c.ControlId,
        ImplementationDescription = c.Title ?? string.Empty,
        ImplementationStatus = c.IsManual ? "ManualReviewRequired" : "Planned",
        ResponsibleRoles = new[] { "SystemOwner" },
        ImplementationEvidence = new List<string>()
      }).ToList()
    };
  }

  private async Task<PlanOfAction> GeneratePoamAsync(string bundleRoot, IReadOnlyList<ControlRecord> controls, CancellationToken ct)
  {
    var entries = new List<PoamEntry>();

    var failedControls = controls.Where(c =>
      string.Equals(c.Severity, "high", StringComparison.OrdinalIgnoreCase)
      || string.Equals(c.Severity, "cat i", StringComparison.OrdinalIgnoreCase)
      || c.IsManual)
      .ToList();

    foreach (var control in failedControls)
    {
      var entry = new PoamEntry
      {
        PoamId = $"POAM-{control.ControlId}",
        ControlId = control.ControlId,
        VulnerabilityDescription = control.Title ?? "Unknown vulnerability",
        RiskLevel = MapSeverityToRiskLevel(control.Severity),
        ScheduledCompletionDate = _clock.Now.AddMonths(3),
        Milestones = GenerateMilestones(control),
        Status = "Ongoing",
        Comments = control.Discussion ?? string.Empty
      };

      if (_exceptionRepo != null)
      {
        var exceptions = await _exceptionRepo.ListActiveByRuleAsync(bundleRoot, control.ControlId, ct).ConfigureAwait(false);
        if (exceptions.Count > 0)
        {
          entry.HasException = true;
          entry.ExceptionId = exceptions[0].ExceptionId;
          entry.Comments += $" [Exception: {exceptions[0].ExceptionId}]";
        }
      }

      entries.Add(entry);
    }

    return new PlanOfAction
    {
      GeneratedAt = _clock.Now,
      TotalEntries = entries.Count,
      CriticalEntries = entries.Count(e => e.RiskLevel == "High"),
      Entries = entries.OrderByDescending(e => e.RiskLevel).ToList()
    };
  }

  private static List<EvidenceArtifact> CollectEvidenceArtifacts(string bundleRoot)
  {
    var artifacts = new List<EvidenceArtifact>();
    var evidenceRoot = Path.Combine(bundleRoot, "Evidence");

    if (!Directory.Exists(evidenceRoot))
      return artifacts;

    foreach (var file in Directory.EnumerateFiles(evidenceRoot, "*.*", SearchOption.AllDirectories))
    {
      artifacts.Add(new EvidenceArtifact
      {
        FileName = Path.GetFileName(file),
        SourcePath = file,
        RelativePath = Path.GetRelativePath(bundleRoot, file),
        FileSize = new FileInfo(file).Length,
        Timestamp = File.GetLastWriteTimeUtc(file)
      });
    }

    return artifacts;
  }

  private static List<ScanResult> CollectScanResults(string bundleRoot)
  {
    var results = new List<ScanResult>();
    var verifyRoot = Path.Combine(bundleRoot, "Verify");

    if (!Directory.Exists(verifyRoot))
      return results;

    foreach (var file in Directory.EnumerateFiles(verifyRoot, "*.json", SearchOption.AllDirectories))
    {
      results.Add(new ScanResult
      {
        Tool = Path.GetFileNameWithoutExtension(file),
        ReportPath = file,
        Timestamp = File.GetLastWriteTimeUtc(file)
      });
    }

    return results;
  }

  private async Task<ComplianceSummary> GenerateComplianceSummaryAsync(string bundleRoot, CancellationToken ct)
  {
    var summary = new ComplianceSummary
    {
      GeneratedAt = _clock.Now,
      BundleRoot = bundleRoot
    };

    if (_trendRepo != null)
    {
      var latest = await _trendRepo.GetLatestSnapshotAsync(bundleRoot, ct).ConfigureAwait(false);
      if (latest != null)
      {
        summary.PassRate = latest.CompliancePercent;
        summary.TotalControls = latest.TotalCount;
        summary.CompliantControls = latest.PassCount;
        summary.NonCompliantControls = latest.FailCount + latest.ErrorCount;
        summary.NotApplicable = latest.NotApplicableCount;
        summary.NotReviewed = latest.NotReviewedCount;
      }
    }

    return summary;
  }

  private static string GenerateChangeLog(string previousPackagePath, EmassPackage currentPackage)
  {
    var sb = new StringBuilder();
    sb.AppendLine($"# eMASS Package Change Log");
    sb.AppendLine($"**Previous Package:** {previousPackagePath}");
    sb.AppendLine($"**Current Package:** {currentPackage.PackageId}");
    sb.AppendLine($"**Generated:** {currentPackage.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
    sb.AppendLine();
    sb.AppendLine("## Changes Since Last Package");
    var previousPackage = TryLoadPreviousPackage(previousPackagePath);
    if (previousPackage == null)
    {
      sb.AppendLine("- Previous package manifest could not be loaded; detailed comparison unavailable.");
      sb.AppendLine($"- Current package controls: {currentPackage.ControlCorrelationMatrix.Controls.Count}");
      sb.AppendLine($"- Current POA&M entries: {currentPackage.Poam.Entries.Count}");
      sb.AppendLine();
      sb.AppendLine("## Control Status Changes");
      sb.AppendLine("- Controls added: 0");
      sb.AppendLine("- Controls removed: 0");
      sb.AppendLine("- Controls changed: 0");
      return sb.ToString();
    }

    var previousControls = BuildControlLookup(previousPackage);
    var currentControls = BuildControlLookup(currentPackage);

    var addedControlIds = currentControls.Keys.Except(previousControls.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    var removedControlIds = previousControls.Keys.Except(currentControls.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
    var changedControlIds = currentControls.Keys
      .Intersect(previousControls.Keys, StringComparer.OrdinalIgnoreCase)
      .Where(id => !string.Equals(currentControls[id], previousControls[id], StringComparison.Ordinal))
      .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
      .ToList();

    sb.AppendLine($"- Controls compared: {currentControls.Count}");
    sb.AppendLine($"- Added controls: {addedControlIds.Count}");
    sb.AppendLine($"- Removed controls: {removedControlIds.Count}");
    sb.AppendLine($"- Modified controls: {changedControlIds.Count}");

    var previousPoam = BuildPoamLookup(previousPackage);
    var currentPoam = BuildPoamLookup(currentPackage);
    var newPoam = currentPoam.Keys.Except(previousPoam.Keys, StringComparer.OrdinalIgnoreCase).Count();
    var resolvedPoam = previousPoam.Keys.Except(currentPoam.Keys, StringComparer.OrdinalIgnoreCase).Count();
    var changedPoam = currentPoam.Keys
      .Intersect(previousPoam.Keys, StringComparer.OrdinalIgnoreCase)
      .Count(id => !string.Equals(currentPoam[id], previousPoam[id], StringComparison.Ordinal));

    sb.AppendLine($"- POA&M entries opened: {newPoam}");
    sb.AppendLine($"- POA&M entries resolved: {resolvedPoam}");
    sb.AppendLine($"- POA&M entries updated: {changedPoam}");

    AppendSampleIds(sb, "Added control IDs", addedControlIds);
    AppendSampleIds(sb, "Removed control IDs", removedControlIds);
    AppendSampleIds(sb, "Modified control IDs", changedControlIds);

    sb.AppendLine();
    sb.AppendLine("## Control Status Changes");
    sb.AppendLine($"- Controls added: {addedControlIds.Count}");
    sb.AppendLine($"- Controls removed: {removedControlIds.Count}");
    sb.AppendLine($"- Controls changed: {changedControlIds.Count}");

    return sb.ToString();
  }

  private static EmassPackage? TryLoadPreviousPackage(string previousPackagePath)
  {
    try
    {
      var directManifestPath = Path.Combine(previousPackagePath, "package-manifest.json");
      var manifestPath = File.Exists(directManifestPath)
        ? directManifestPath
        : Directory
          .EnumerateFiles(previousPackagePath, "package-manifest.json", SearchOption.AllDirectories)
          .OrderByDescending(File.GetLastWriteTimeUtc)
          .FirstOrDefault();

      if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
        return null;

      var json = File.ReadAllText(manifestPath);
      return JsonSerializer.Deserialize<EmassPackage>(json);
    }
    catch
    {
      return null;
    }
  }

  private static Dictionary<string, string> BuildControlLookup(EmassPackage package)
  {
    var controls = package.ControlCorrelationMatrix?.Controls ?? new List<CcmControlEntry>();
    return controls
      .Where(control => !string.IsNullOrWhiteSpace(control.ControlId))
      .GroupBy(control => control.ControlId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        group => group.Key,
        group => BuildControlSignature(group.First()),
        StringComparer.OrdinalIgnoreCase);
  }

  private static Dictionary<string, string> BuildPoamLookup(EmassPackage package)
  {
    var entries = package.Poam?.Entries ?? new List<PoamEntry>();
    return entries
      .Where(entry => !string.IsNullOrWhiteSpace(entry.ControlId))
      .GroupBy(entry => entry.ControlId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        group => group.Key,
        group => BuildPoamSignature(group.First()),
        StringComparer.OrdinalIgnoreCase);
  }

  private static string BuildControlSignature(CcmControlEntry control)
  {
    return string.Join("|",
      control.ControlName ?? string.Empty,
      control.Applicability ?? string.Empty,
      control.ImplementationStatus ?? string.Empty,
      control.ResponsibleRole ?? string.Empty,
      control.ImplementationNarrative ?? string.Empty,
      control.Inheritance ?? string.Empty);
  }

  private static string BuildPoamSignature(PoamEntry entry)
  {
    return string.Join("|",
      entry.Status ?? string.Empty,
      entry.RiskLevel ?? string.Empty,
      entry.VulnerabilityDescription ?? string.Empty,
      entry.ExceptionId ?? string.Empty,
      entry.ScheduledCompletionDate.UtcDateTime.ToString("O"));
  }

  private static void AppendSampleIds(StringBuilder sb, string heading, IReadOnlyList<string> ids)
  {
    if (ids.Count == 0)
      return;

    sb.AppendLine();
    sb.AppendLine($"### {heading}");
    foreach (var id in ids.Take(10))
      sb.AppendLine($"- {id}");

    if (ids.Count > 10)
      sb.AppendLine($"- ... ({ids.Count - 10} more)");
  }

  private static string DetermineApplicability(ControlRecord control)
  {
    return control.Applicability.ClassificationScope switch
    {
      ScopeTag.ClassifiedOnly => "ClassifiedOnly",
      ScopeTag.UnclassifiedOnly => "UnclassifiedOnly",
      ScopeTag.Both => "Applicable",
      _ => "Applicable"
    };
  }

  private static List<CcmParameter> ExtractParameters(ControlRecord control)
  {
    return new List<CcmParameter>();
  }

  private static string MapSeverityToRiskLevel(string? severity)
  {
    return severity?.ToLowerInvariant() switch
    {
      "high" or "cat i" or "i" => "High",
      "medium" or "cat ii" or "ii" => "Medium",
      "low" or "cat iii" or "iii" => "Low",
      _ => "Medium"
    };
  }

  private static List<Milestone> GenerateMilestones(ControlRecord control)
  {
    return new List<Milestone>
    {
      new() { Description = "Assess vulnerability", TargetDate = DateTimeOffset.UtcNow.AddDays(30) },
      new() { Description = "Implement remediation", TargetDate = DateTimeOffset.UtcNow.AddDays(60) },
      new() { Description = "Verify remediation", TargetDate = DateTimeOffset.UtcNow.AddDays(90) }
    };
  }

  private static string ComputeChecksums(string packageDir)
  {
    var files = Directory.EnumerateFiles(packageDir, "*", SearchOption.AllDirectories)
      .Where(path => !path.EndsWith("sha256-checksums.txt", StringComparison.OrdinalIgnoreCase))
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var sb = new StringBuilder();
    foreach (var file in files)
    {
      using var stream = File.OpenRead(file);
      var hash = SHA256.HashData(stream);
      var relative = Path.GetRelativePath(packageDir, file).Replace('\\', '/');
      sb.AppendLine($"{Convert.ToHexString(hash).ToLowerInvariant()}  {relative}");
    }

    return sb.ToString();
  }
}

public sealed class EmassPackage
{
  public string PackageId { get; set; } = string.Empty;
  public DateTimeOffset GeneratedAt { get; set; }
  public string SystemName { get; set; } = string.Empty;
  public string SystemAcronym { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;

  public ControlCorrelationMatrix ControlCorrelationMatrix { get; set; } = new();
  public SystemSecurityPlan SystemSecurityPlan { get; set; } = new();
  public PlanOfAction Poam { get; set; } = new();
  public IReadOnlyList<EvidenceArtifact> EvidenceArtifacts { get; set; } = [];
  public IReadOnlyList<ScanResult> ScanResults { get; set; } = [];
  public ComplianceSummary ComplianceSummary { get; set; } = new();
  public string? ChangeLog { get; set; }
}

public sealed class ControlCorrelationMatrix
{
  public List<CcmControlEntry> Controls { get; set; } = new();
}

public sealed class CcmControlEntry
{
  public string ControlId { get; set; } = string.Empty;
  public string ControlName { get; set; } = string.Empty;
  public string Applicability { get; set; } = string.Empty;
  public string ImplementationStatus { get; set; } = string.Empty;
  public string ResponsibleRole { get; set; } = string.Empty;
  public string ImplementationNarrative { get; set; } = string.Empty;
  public List<CcmParameter> Parameters { get; set; } = new();
  public string Inheritance { get; set; } = string.Empty;
}

public sealed class CcmParameter
{
  public string Id { get; set; } = string.Empty;
  public string Value { get; set; } = string.Empty;
}

public sealed class SystemSecurityPlan
{
  public string SystemName { get; set; } = string.Empty;
  public string SystemAcronym { get; set; } = string.Empty;
  public string SystemDescription { get; set; } = string.Empty;
  public string SystemType { get; set; } = string.Empty;
  public string SystemStatus { get; set; } = string.Empty;
  public string AuthorizationBoundary { get; set; } = string.Empty;

  public int ImplementedControls { get; set; }
  public int InheritedControls { get; set; }
  public int HybridControls { get; set; }

  public List<SspControlImplementation> ControlImplementations { get; set; } = new();
}

public sealed class SspControlImplementation
{
  public string ControlId { get; set; } = string.Empty;
  public string ImplementationDescription { get; set; } = string.Empty;
  public string ImplementationStatus { get; set; } = string.Empty;
  public IReadOnlyList<string> ResponsibleRoles { get; set; } = [];
  public List<string> ImplementationEvidence { get; set; } = new();
}

public sealed class PlanOfAction
{
  public DateTimeOffset GeneratedAt { get; set; }
  public int TotalEntries { get; set; }
  public int CriticalEntries { get; set; }
  public List<PoamEntry> Entries { get; set; } = new();
}

public sealed class PoamEntry
{
  public string PoamId { get; set; } = string.Empty;
  public string ControlId { get; set; } = string.Empty;
  public string VulnerabilityDescription { get; set; } = string.Empty;
  public string RiskLevel { get; set; } = string.Empty;
  public DateTimeOffset ScheduledCompletionDate { get; set; }
  public List<Milestone> Milestones { get; set; } = new();
  public string Status { get; set; } = string.Empty;
  public string Comments { get; set; } = string.Empty;
  public bool HasException { get; set; }
  public string? ExceptionId { get; set; }
}

public sealed class Milestone
{
  public string Description { get; set; } = string.Empty;
  public DateTimeOffset TargetDate { get; set; }
}

public sealed class EvidenceArtifact
{
  public string FileName { get; set; } = string.Empty;
  public string SourcePath { get; set; } = string.Empty;
  public string RelativePath { get; set; } = string.Empty;
  public long FileSize { get; set; }
  public DateTime Timestamp { get; set; }
}

public sealed class ScanResult
{
  public string Tool { get; set; } = string.Empty;
  public string ReportPath { get; set; } = string.Empty;
  public DateTime Timestamp { get; set; }
}

public sealed class ComplianceSummary
{
  public DateTimeOffset GeneratedAt { get; set; }
  public string BundleRoot { get; set; } = string.Empty;
  public int TotalControls { get; set; }
  public int CompliantControls { get; set; }
  public int NonCompliantControls { get; set; }
  public int NotApplicable { get; set; }
  public int NotReviewed { get; set; }
  public double PassRate { get; set; }
}

public sealed class BundleManifest
{
  public string? Name { get; set; }
  public string? Version { get; set; }
  public DateTimeOffset? CreatedAt { get; set; }
}
