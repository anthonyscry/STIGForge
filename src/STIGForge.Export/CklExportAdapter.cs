namespace STIGForge.Export;

public sealed class CklExportAdapter : IExportAdapter
{
    public string FormatName => "CKL";
    public string[] SupportedExtensions => new[] { ".ckl", ".cklb" };

    public Task<ExportAdapterResult> ExportAsync(
        ExportAdapterRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BundleRoot))
            return Task.FromResult(new ExportAdapterResult
            {
                Success = false,
                ErrorMessage = "BundleRoot is required for CKL export."
            });

        request.Options.TryGetValue("format", out var fmtStr);
        var format = string.Equals(fmtStr, "cklb", StringComparison.OrdinalIgnoreCase)
            ? CklFileFormat.Cklb : CklFileFormat.Ckl;

        request.Options.TryGetValue("include-csv", out var csvStr);
        var includeCsv = string.Equals(csvStr, "true", StringComparison.OrdinalIgnoreCase);

        var result = CklExporter.ExportCkl(new CklExportRequest
        {
            BundleRoot = request.BundleRoot,
            OutputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? null : request.OutputDirectory,
            FileName = request.FileNameStem,
            FileFormat = format,
            IncludeCsv = includeCsv
        });

        return Task.FromResult(new ExportAdapterResult
        {
            Success = true,
            OutputPaths = result.OutputPaths.ToArray(),
            Warnings = result.ControlCount == 0
                ? new[] { result.Message } : Array.Empty<string>()
        });
    }
}
