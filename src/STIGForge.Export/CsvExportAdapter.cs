using System.Globalization;
using System.Text;
using STIGForge.Verify;

namespace STIGForge.Export;

/// <summary>
/// Export adapter that generates management-facing CSV compliance reports.
/// Output is RFC 4180 compliant with CRLF line endings and proper field escaping.
/// </summary>
public sealed class CsvExportAdapter : IExportAdapter
{
    private static readonly string[] Headers = new[]
    {
        "System Name",
        "Vulnerability ID",
        "Rule ID",
        "STIG Title",
        "Severity",
        "CAT Level",
        "Status",
        "Finding Details",
        "Comments",
        "Remediation Priority",
        "Tool",
        "Source File",
        "Verified At"
    };

    public string FormatName => "CSV";
    public string[] SupportedExtensions => new[] { ".csv" };

    public async Task<ExportAdapterResult> ExportAsync(
        ExportAdapterRequest request, CancellationToken ct)
    {
        var fileName = (request.FileNameStem ?? "stigforge_compliance_report") + ".csv";
        var outputPath = Path.Combine(request.OutputDirectory, fileName);
        var tempPath = outputPath + ".tmp";

        try
        {
            Directory.CreateDirectory(request.OutputDirectory);

            var systemName = GetSystemName(request);

            await Task.Run(() =>
            {
                var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                using var writer = new StreamWriter(tempPath, append: false, utf8NoBom);
                writer.NewLine = "\r\n";

                // Write header row
                writer.WriteLine(string.Join(",", Headers));

                // Write data rows
                foreach (var result in request.Results)
                {
                    var catLevel = MapSeverityToCatLevel(result.Severity);
                    var fields = new[]
                    {
                        EscapeCsvField(systemName),
                        EscapeCsvField(result.VulnId),
                        EscapeCsvField(result.RuleId),
                        EscapeCsvField(result.Title),
                        EscapeCsvField(result.Severity),
                        EscapeCsvField(catLevel),
                        EscapeCsvField(result.Status),
                        EscapeCsvField(result.FindingDetails),
                        EscapeCsvField(result.Comments),
                        EscapeCsvField(catLevel), // Remediation Priority = CAT Level
                        EscapeCsvField(result.Tool),
                        EscapeCsvField(result.SourceFile),
                        EscapeCsvField(result.VerifiedAt?.ToString("o"))
                    };
                    writer.WriteLine(string.Join(",", fields));
                }
            }, ct).ConfigureAwait(false);

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

    private static string GetSystemName(ExportAdapterRequest request)
    {
        if (request.Options != null &&
            request.Options.TryGetValue("system-name", out var name) &&
            !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (!string.IsNullOrWhiteSpace(request.BundleRoot))
        {
            return Path.GetFileName(request.BundleRoot);
        }

        return string.Empty;
    }

    internal static string MapSeverityToCatLevel(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return "Unknown";

        return severity!.Trim().ToLowerInvariant() switch
        {
            "high" => "CAT I",
            "medium" => "CAT II",
            "low" => "CAT III",
            _ => "Unknown"
        };
    }

    internal static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value!.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
