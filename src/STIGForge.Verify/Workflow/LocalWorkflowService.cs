using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;

namespace STIGForge.Verify;

public sealed class LocalWorkflowService : ILocalWorkflowService
{
  private readonly IVerificationWorkflowService _verificationWorkflowService;
  private readonly ScannerEvidenceMapper _scannerEvidenceMapper;

  public LocalWorkflowService(
    IVerificationWorkflowService verificationWorkflowService,
    ScannerEvidenceMapper scannerEvidenceMapper)
  {
    _verificationWorkflowService = verificationWorkflowService;
    _scannerEvidenceMapper = scannerEvidenceMapper;
  }

  public async Task<LocalWorkflowResult> RunAsync(LocalWorkflowRequest request, CancellationToken ct)
  {
    if (request == null)
      throw new ArgumentNullException(nameof(request));

    if (string.IsNullOrWhiteSpace(request.OutputRoot))
      throw new ArgumentException("OutputRoot is required.", nameof(request));

    if (string.IsNullOrWhiteSpace(request.ImportRoot))
      throw new ArgumentException("ImportRoot is required.", nameof(request));

    ct.ThrowIfCancellationRequested();

    Directory.CreateDirectory(request.OutputRoot);

    var diagnostics = new List<string>();

    var canonicalChecklist = BuildCanonicalChecklist(request.ImportRoot, diagnostics, ct);

    if (canonicalChecklist.Count == 0)
      throw new InvalidOperationException("Import stage did not produce canonical checklist items.");

    var verificationResult = await _verificationWorkflowService.RunAsync(new VerificationWorkflowRequest
    {
      OutputRoot = request.OutputRoot,
      ConsolidatedToolLabel = "Evaluate-STIG"
    }, ct).ConfigureAwait(false);

    diagnostics.AddRange(verificationResult.Diagnostics);

    var findings = LoadScannerFindings(verificationResult.ConsolidatedJsonPath, diagnostics);
    var mapResult = _scannerEvidenceMapper.Map(canonicalChecklist, findings);
    diagnostics.AddRange(mapResult.Diagnostics);

    var mission = new LocalWorkflowMission
    {
      CanonicalChecklist = canonicalChecklist,
      ScannerEvidence = mapResult.ScannerEvidence.ToList(),
      Unmapped = mapResult.Unmapped.ToList()
    };

    var missionPath = Path.Combine(request.OutputRoot, "mission.json");
    await WriteMissionAsync(missionPath, mission, ct).ConfigureAwait(false);

    return new LocalWorkflowResult
    {
      Mission = mission,
      Diagnostics = diagnostics
    };
  }

  private static IReadOnlyList<ControlResult> LoadScannerFindings(string? consolidatedJsonPath, ICollection<string> diagnostics)
  {
    var path = consolidatedJsonPath?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
      diagnostics.Add("Scan stage did not produce consolidated-results.json; continuing with empty scanner findings.");
      return Array.Empty<ControlResult>();
    }

    try
    {
      var report = VerifyReportReader.LoadFromJson(path);
      return report.Results;
    }
    catch (Exception ex)
    {
      diagnostics.Add("Failed to read consolidated scanner report: " + ex.Message);
      return Array.Empty<ControlResult>();
    }
  }

  private static IReadOnlyList<LocalWorkflowChecklistItem> BuildCanonicalChecklist(string importRoot, ICollection<string> diagnostics, CancellationToken ct)
  {
    if (!Directory.Exists(importRoot))
    {
      diagnostics.Add("Import folder not found: " + importRoot);
      return Array.Empty<LocalWorkflowChecklistItem>();
    }

    var canonical = new List<LocalWorkflowChecklistItem>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var zips = Directory.GetFiles(importRoot, "*.zip", SearchOption.AllDirectories)
      .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
      .ToList();

    foreach (var zipPath in zips)
    {
      ct.ThrowIfCancellationRequested();

      try
      {
        using var archive = System.IO.Compression.ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
          if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            continue;

          ProjectChecklistEntry(entry, canonical, seen);
        }
      }
      catch (Exception ex)
      {
        diagnostics.Add(Path.GetFileName(zipPath) + ": canonical projection failed (" + ex.Message + ")");
      }
    }

    return canonical
      .OrderBy(i => i.StigId, StringComparer.OrdinalIgnoreCase)
      .ThenBy(i => i.RuleId, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static void ProjectChecklistEntry(
    System.IO.Compression.ZipArchiveEntry entry,
    ICollection<LocalWorkflowChecklistItem> canonical,
    ISet<string> seen)
  {
    var benchmark = ReadBenchmarkRoot(entry);
    if (benchmark == null)
      return;

    var stigId = benchmark.Attribute("id")?.Value?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(stigId))
      return;

    foreach (var rule in benchmark.Descendants().Where(e => string.Equals(e.Name.LocalName, "Rule", StringComparison.OrdinalIgnoreCase)))
    {
      var ruleId = rule.Attribute("id")?.Value?.Trim() ?? string.Empty;
      if (string.IsNullOrWhiteSpace(ruleId))
        continue;

      var key = stigId + "|" + ruleId;
      if (!seen.Add(key))
        continue;

      canonical.Add(new LocalWorkflowChecklistItem
      {
        StigId = stigId,
        RuleId = ruleId
      });
    }
  }

  private static XElement? ReadBenchmarkRoot(System.IO.Compression.ZipArchiveEntry entry)
  {
    try
    {
      using var stream = entry.Open();
      var settings = new XmlReaderSettings
      {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        XmlResolver = null
      };

      using var reader = XmlReader.Create(stream, settings);
      var document = XDocument.Load(reader, LoadOptions.None);
      if (document.Root == null)
        return null;

      if (string.Equals(document.Root.Name.LocalName, "Benchmark", StringComparison.OrdinalIgnoreCase))
        return document.Root;

      return document.Root
        .Descendants()
        .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Benchmark", StringComparison.OrdinalIgnoreCase));
    }
    catch
    {
      return null;
    }
  }

  private static async Task WriteMissionAsync(string missionPath, LocalWorkflowMission mission, CancellationToken ct)
  {
    var json = JsonSerializer.Serialize(mission, new JsonSerializerOptions
    {
      WriteIndented = true
    });

    await File.WriteAllTextAsync(missionPath, json, ct).ConfigureAwait(false);
  }
}
