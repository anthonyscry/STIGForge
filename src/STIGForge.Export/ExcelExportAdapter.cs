using STIGForge.Reporting;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Export adapter that generates multi-tab Excel compliance workbooks (.xlsx).
/// Delegates workbook construction to ReportGenerator in STIGForge.Reporting.
/// </summary>
public sealed class ExcelExportAdapter : IExportAdapter
{
    public string FormatName => "Excel";
    public string[] SupportedExtensions => new[] { ".xlsx" };

    public async Task<ExportAdapterResult> ExportAsync(
        ExportAdapterRequest request, CancellationToken ct)
    {
        var fileName = (request.FileNameStem ?? "stigforge_compliance_report") + ".xlsx";
        var outputPath = Path.Combine(request.OutputDirectory, fileName);
        var tempPath = Path.Combine(request.OutputDirectory,
            Path.GetFileNameWithoutExtension(fileName) + "_tmp_" + Guid.NewGuid().ToString("N")[..8] + ".xlsx");

        try
        {
            Directory.CreateDirectory(request.OutputDirectory);

            var options = new Dictionary<string, string>();
            if (request.Options != null)
            {
                foreach (var kv in request.Options)
                    options[kv.Key] = kv.Value;
            }

            // Pass bundle root for system name fallback
            if (!string.IsNullOrWhiteSpace(request.BundleRoot) && !options.ContainsKey("bundle-root"))
                options["bundle-root"] = request.BundleRoot;

            var generator = new ReportGenerator();
            using var workbook = await generator.GenerateAsync(request.Results, options, ct)
                .ConfigureAwait(false);

            await Task.Run(() => workbook.SaveAs(tempPath), ct).ConfigureAwait(false);

            if (File.Exists(outputPath))
                File.Delete(outputPath);
            File.Move(tempPath, outputPath);

            return new ExportAdapterResult
            {
                Success = true,
                OutputPaths = new[] { outputPath }
            };
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best-effort cleanup */ }
            throw;
        }
    }
}
