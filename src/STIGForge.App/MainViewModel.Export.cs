using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text.Json;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.App;

public partial class MainViewModel
{
  [ObservableProperty] private string poamSystemName = "";
  [ObservableProperty] private string poamStatus = "";
  [ObservableProperty] private string cklHostName = "";
  [ObservableProperty] private string cklOutputFormat = "CKL";
  [ObservableProperty] private string cklStatus = "";

  // Quick Export
  [ObservableProperty] private string selectedExportFormat = "";
  [ObservableProperty] private string quickExportSystemName = "";
  [ObservableProperty] private string quickExportFileName = "";
  [ObservableProperty] private string quickExportStatus = "";

  private readonly ExportAdapterRegistry _exportRegistry = new();

  public IReadOnlyList<string> ExportFormatNames { get; private set; } = Array.Empty<string>();

  public IReadOnlyList<string> CklOutputFormats { get; } = new[]
  {
    "CKL",
    "CKLB",
    "CKL + CSV",
    "CKLB + CSV"
  };

  private void InitializeExportRegistry()
  {
    _exportRegistry.Register(new CklExportAdapter());
    _exportRegistry.Register(new XccdfExportAdapter());
    _exportRegistry.Register(new CsvExportAdapter());
    _exportRegistry.Register(new ExcelExportAdapter());
    ExportFormatNames = _exportRegistry.GetAll().Select(a => a.FormatName).ToList();
    OnPropertyChanged(nameof(ExportFormatNames));
  }

  [RelayCommand]
  private async Task QuickExportAsync()
  {
    if (IsBusy) return;
    try
    {
      IsBusy = true;
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        QuickExportStatus = "Select a bundle first.";
        return;
      }

      if (string.IsNullOrWhiteSpace(SelectedExportFormat))
      {
        QuickExportStatus = "Select an export format.";
        return;
      }

      var adapter = _exportRegistry.TryResolve(SelectedExportFormat);
      if (adapter == null)
      {
        QuickExportStatus = "Unknown format: " + SelectedExportFormat;
        return;
      }

      QuickExportStatus = "Exporting " + SelectedExportFormat + "...";

      // Load verify results from consolidated-results.json
      var resultsPath = Path.Combine(BundleRoot, "Verify", "consolidated-results.json");
      IReadOnlyList<ControlResult> results;
      if (File.Exists(resultsPath))
      {
        var report = VerifyReportReader.LoadFromJson(resultsPath);
        results = report.Results;
      }
      else
      {
        QuickExportStatus = "No verify results found. Run Verify first.";
        return;
      }

      var outputDir = Path.Combine(BundleRoot, "Export");
      var options = new Dictionary<string, string>();
      if (!string.IsNullOrWhiteSpace(QuickExportSystemName))
        options["system-name"] = QuickExportSystemName;

      var request = new ExportAdapterRequest
      {
        BundleRoot = BundleRoot,
        Results = results,
        OutputDirectory = outputDir,
        FileNameStem = string.IsNullOrWhiteSpace(QuickExportFileName) ? null : QuickExportFileName,
        Options = options
      };

      var result = await adapter.ExportAsync(request, _cts.Token);

      if (result.Success)
      {
        var paths = string.Join(", ", result.OutputPaths);
        QuickExportStatus = "Export complete: " + paths;
        if (result.OutputPaths.Count > 0)
          LastOutputPath = result.OutputPaths[0];
      }
      else
      {
        QuickExportStatus = "Export failed: " + (result.ErrorMessage ?? "Unknown error");
      }
    }
    catch (Exception ex)
    {
      QuickExportStatus = "Export failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task ExportPoam()
  {
    try
    {
      if (string.IsNullOrWhiteSpace(BundleRoot))
      {
        StatusText = "Select a bundle first.";
        return;
      }

      IsBusy = true;
      StatusText = "Exporting POA&M...";

      await Task.Run(() =>
      {
        var request = new PoamExportRequest
        {
          BundleRoot = BundleRoot,
          SystemName = string.IsNullOrWhiteSpace(PoamSystemName) ? "STIGForge" : PoamSystemName
        };

        var result = StandalonePoamExporter.ExportPoam(request);
        PoamStatus = result.Message;
      }, _cts.Token);

      StatusText = "POA&M export complete.";
    }
    catch (Exception ex)
    {
      PoamStatus = "Failed: " + ex.Message;
      StatusText = "POA&M export failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  [RelayCommand]
  private async Task ExportCkl()
  {
    try
    {
      var bundleRoots = ResolveChecklistBundleRoots();
      if (bundleRoots.Count == 0)
      {
        StatusText = "No bundle roots available for checklist export. Build or select bundles first.";
        return;
      }

      IsBusy = true;
      StatusText = "Exporting CKL...";

      await Task.Run(() =>
      {
        var includeCsv = CklOutputFormat.IndexOf("CSV", StringComparison.OrdinalIgnoreCase) >= 0;
        var format = CklOutputFormat.StartsWith("CKLB", StringComparison.OrdinalIgnoreCase)
          ? CklFileFormat.Cklb
          : CklFileFormat.Ckl;

        var request = new CklExportRequest
        {
          BundleRoot = bundleRoots[0],
          BundleRoots = bundleRoots,
          HostName = string.IsNullOrWhiteSpace(CklHostName) ? null : CklHostName,
          FileFormat = format,
          IncludeCsv = includeCsv
        };

        var result = CklExporter.ExportCkl(request);
        var outputLabel = result.OutputPaths.Count > 0
          ? string.Join(" | ", result.OutputPaths)
          : result.OutputPath;
        CklStatus = result.Message + (string.IsNullOrWhiteSpace(outputLabel) ? "" : $" ({outputLabel})");
        if (result.OutputPaths.Count > 0)
          LastOutputPath = result.OutputPaths[0];
      }, _cts.Token);

      StatusText = "CKL export complete.";
    }
    catch (Exception ex)
    {
      CklStatus = "Failed: " + ex.Message;
      StatusText = "CKL export failed: " + ex.Message;
    }
    finally
    {
      IsBusy = false;
    }
  }

  private List<string> ResolveChecklistBundleRoots()
  {
    var bundleRoots = new List<string>();
    if (!string.IsNullOrWhiteSpace(BundleRoot) && Directory.Exists(BundleRoot))
      bundleRoots.Add(BundleRoot);

    foreach (var recent in RecentBundles)
    {
      if (!string.IsNullOrWhiteSpace(recent) && Directory.Exists(recent))
        bundleRoots.Add(recent);
    }

    bundleRoots = bundleRoots
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    var applicableStigPackIds = ContentPacks
      .Where(p => ApplicablePackIds.Contains(p.PackId)
        && string.Equals(ResolvePackFormat(p), "STIG", StringComparison.OrdinalIgnoreCase))
      .Select(p => p.PackId)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (applicableStigPackIds.Count == 0)
      return bundleRoots;

    var matched = bundleRoots
      .Where(path => TryReadBundlePackId(path, out var packId)
        && applicableStigPackIds.Contains(packId))
      .ToList();

    return matched;
  }

  private static bool TryReadBundlePackId(string bundleRoot, out string packId)
  {
    packId = string.Empty;
    if (string.IsNullOrWhiteSpace(bundleRoot) || !Directory.Exists(bundleRoot))
      return false;

    var manifestPath = Path.Combine(bundleRoot, "Manifest", "manifest.json");
    if (!File.Exists(manifestPath))
      return false;

    try
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
      if (!doc.RootElement.TryGetProperty("run", out var run) || run.ValueKind != JsonValueKind.Object)
        return false;

      if (!run.TryGetProperty("packId", out var packIdElement) || packIdElement.ValueKind != JsonValueKind.String)
        return false;

      packId = packIdElement.GetString() ?? string.Empty;
      return !string.IsNullOrWhiteSpace(packId);
    }
    catch
    {
      return false;
    }
  }
}
