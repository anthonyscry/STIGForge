using ClosedXML.Excel;
using STIGForge.Verify;

namespace STIGForge.Reporting;

/// <summary>
/// Generates a multi-tab Excel compliance report workbook using ClosedXML.
/// Tabs: Summary, All Controls, Open Findings, Coverage.
/// </summary>
public sealed class ReportGenerator
{
    private static readonly string[] DataHeaders = new[]
    {
        "System Name", "Vulnerability ID", "Rule ID", "STIG Title",
        "Severity", "CAT Level", "Status", "Finding Details",
        "Comments", "Remediation Priority", "Tool", "Source File", "Verified At"
    };

    public Task<XLWorkbook> GenerateAsync(
        IReadOnlyList<ControlResult> results,
        IReadOnlyDictionary<string, string> options,
        CancellationToken ct)
    {
        var workbook = new XLWorkbook();

        var systemName = GetSystemName(options);
        var sorted = results
            .OrderBy(r => SeverityOrder(r.Severity))
            .ThenBy(r => r.VulnId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        BuildSummaryTab(workbook, sorted, systemName);
        BuildAllControlsTab(workbook, sorted, systemName);
        BuildOpenFindingsTab(workbook, sorted, systemName);
        BuildCoverageTab(workbook, sorted);

        return Task.FromResult(workbook);
    }

    private void BuildSummaryTab(XLWorkbook workbook, List<ControlResult> sorted, string systemName)
    {
        var ws = workbook.Worksheets.Add("Summary");

        // Title
        ws.Cell(1, 1).Value = "STIGForge Compliance Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 2).Merge();

        // System and timestamp
        ws.Cell(2, 1).Value = "System:";
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(2, 2).Value = systemName;
        ws.Cell(3, 1).Value = "Generated:";
        ws.Cell(3, 1).Style.Font.Bold = true;
        ws.Cell(3, 2).Value = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

        // Metrics block
        int total = sorted.Count;
        int pass = sorted.Count(r => IsStatus(r.Status, "Pass"));
        int fail = sorted.Count(r => IsStatus(r.Status, "Fail") || IsStatus(r.Status, "Open"));
        int notApplicable = sorted.Count(r => IsStatus(r.Status, "Not_Applicable") || IsStatus(r.Status, "NotApplicable") || IsStatus(r.Status, "notapplicable"));
        int notReviewed = sorted.Count(r => IsStatus(r.Status, "Not_Reviewed") || IsStatus(r.Status, "NotReviewed") || IsStatus(r.Status, "notreviewed"));
        double passRate = total > 0 ? (double)pass / total * 100 : 0;

        ws.Cell(5, 1).Value = "Metric";
        ws.Cell(5, 2).Value = "Value";
        ws.Cell(5, 1).Style.Font.Bold = true;
        ws.Cell(5, 2).Style.Font.Bold = true;

        ws.Cell(6, 1).Value = "Total Controls";
        ws.Cell(6, 2).Value = total;
        ws.Cell(7, 1).Value = "Pass";
        ws.Cell(7, 2).Value = pass;
        ws.Cell(8, 1).Value = "Fail";
        ws.Cell(8, 2).Value = fail;
        ws.Cell(9, 1).Value = "Not Applicable";
        ws.Cell(9, 2).Value = notApplicable;
        ws.Cell(10, 1).Value = "Not Reviewed";
        ws.Cell(10, 2).Value = notReviewed;
        ws.Cell(11, 1).Value = "Pass Rate (%)";
        ws.Cell(11, 2).Value = Math.Round(passRate, 1);

        // Severity breakdown
        ws.Cell(13, 1).Value = "Severity Breakdown";
        ws.Cell(13, 1).Style.Font.Bold = true;
        ws.Range(13, 1, 13, 2).Merge();

        var catICnt = sorted.Count(r => MapSeverityToCatLevel(r.Severity) == "CAT I");
        var catIICnt = sorted.Count(r => MapSeverityToCatLevel(r.Severity) == "CAT II");
        var catIIICnt = sorted.Count(r => MapSeverityToCatLevel(r.Severity) == "CAT III");

        ws.Cell(14, 1).Value = "CAT I (High)";
        ws.Cell(14, 2).Value = catICnt;
        ws.Cell(15, 1).Value = "CAT II (Medium)";
        ws.Cell(15, 2).Value = catIICnt;
        ws.Cell(16, 1).Value = "CAT III (Low)";
        ws.Cell(16, 2).Value = catIIICnt;

        // Open findings by severity
        ws.Cell(18, 1).Value = "Open Findings by Severity";
        ws.Cell(18, 1).Style.Font.Bold = true;
        ws.Range(18, 1, 18, 2).Merge();

        var openResults = sorted.Where(r => IsStatus(r.Status, "Fail") || IsStatus(r.Status, "Open")).ToList();
        ws.Cell(19, 1).Value = "CAT I Open";
        ws.Cell(19, 2).Value = openResults.Count(r => MapSeverityToCatLevel(r.Severity) == "CAT I");
        ws.Cell(20, 1).Value = "CAT II Open";
        ws.Cell(20, 2).Value = openResults.Count(r => MapSeverityToCatLevel(r.Severity) == "CAT II");
        ws.Cell(21, 1).Value = "CAT III Open";
        ws.Cell(21, 2).Value = openResults.Count(r => MapSeverityToCatLevel(r.Severity) == "CAT III");

        ws.Columns().AdjustToContents();
    }

    private void BuildAllControlsTab(XLWorkbook workbook, List<ControlResult> sorted, string systemName)
    {
        var ws = workbook.Worksheets.Add("All Controls");
        WriteDataHeaders(ws);
        WriteDataRows(ws, sorted, systemName, startRow: 2);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents();
    }

    private void BuildOpenFindingsTab(XLWorkbook workbook, List<ControlResult> sorted, string systemName)
    {
        var ws = workbook.Worksheets.Add("Open Findings");
        var openOnly = sorted
            .Where(r => IsStatus(r.Status, "Fail") || IsStatus(r.Status, "Open"))
            .ToList();

        WriteDataHeaders(ws);
        WriteDataRows(ws, openOnly, systemName, startRow: 2);
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents();
    }

    private void BuildCoverageTab(XLWorkbook workbook, List<ControlResult> sorted)
    {
        var ws = workbook.Worksheets.Add("Coverage");

        // Headers
        string[] headers = { "Severity", "Total", "Pass", "Fail", "Not Applicable", "Not Reviewed", "Pass Rate (%)" };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }

        // Severity levels
        var levels = new[] { "CAT I", "CAT II", "CAT III", "Unknown" };
        int row = 2;
        int grandTotal = 0, grandPass = 0, grandFail = 0, grandNA = 0, grandNR = 0;

        foreach (var level in levels)
        {
            var group = sorted.Where(r => MapSeverityToCatLevel(r.Severity) == level).ToList();
            int total = group.Count;
            int pass = group.Count(r => IsStatus(r.Status, "Pass"));
            int fail = group.Count(r => IsStatus(r.Status, "Fail") || IsStatus(r.Status, "Open"));
            int na = group.Count(r => IsStatus(r.Status, "Not_Applicable") || IsStatus(r.Status, "NotApplicable") || IsStatus(r.Status, "notapplicable"));
            int nr = group.Count(r => IsStatus(r.Status, "Not_Reviewed") || IsStatus(r.Status, "NotReviewed") || IsStatus(r.Status, "notreviewed"));
            double rate = total > 0 ? (double)pass / total * 100 : 0;

            ws.Cell(row, 1).Value = level;
            ws.Cell(row, 2).Value = total;
            ws.Cell(row, 3).Value = pass;
            ws.Cell(row, 4).Value = fail;
            ws.Cell(row, 5).Value = na;
            ws.Cell(row, 6).Value = nr;
            ws.Cell(row, 7).Value = Math.Round(rate, 1);

            grandTotal += total;
            grandPass += pass;
            grandFail += fail;
            grandNA += na;
            grandNR += nr;
            row++;
        }

        // Totals row
        double grandRate = grandTotal > 0 ? (double)grandPass / grandTotal * 100 : 0;
        ws.Cell(row, 1).Value = "Total";
        ws.Cell(row, 2).Value = grandTotal;
        ws.Cell(row, 3).Value = grandPass;
        ws.Cell(row, 4).Value = grandFail;
        ws.Cell(row, 5).Value = grandNA;
        ws.Cell(row, 6).Value = grandNR;
        ws.Cell(row, 7).Value = Math.Round(grandRate, 1);

        // Bold totals row
        for (int c = 1; c <= headers.Length; c++)
            ws.Cell(row, c).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();
    }

    private void WriteDataHeaders(IXLWorksheet ws)
    {
        for (int c = 0; c < DataHeaders.Length; c++)
        {
            ws.Cell(1, c + 1).Value = DataHeaders[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
        }
    }

    private void WriteDataRows(IXLWorksheet ws, List<ControlResult> results, string systemName, int startRow)
    {
        int row = startRow;
        foreach (var r in results)
        {
            var catLevel = MapSeverityToCatLevel(r.Severity);
            ws.Cell(row, 1).Value = systemName;
            ws.Cell(row, 2).Value = r.VulnId ?? string.Empty;
            ws.Cell(row, 3).Value = r.RuleId ?? string.Empty;
            ws.Cell(row, 4).Value = r.Title ?? string.Empty;
            ws.Cell(row, 5).Value = r.Severity ?? string.Empty;
            ws.Cell(row, 6).Value = catLevel;
            ws.Cell(row, 7).Value = r.Status ?? string.Empty;
            ws.Cell(row, 8).Value = r.FindingDetails ?? string.Empty;
            ws.Cell(row, 9).Value = r.Comments ?? string.Empty;
            ws.Cell(row, 10).Value = catLevel; // Remediation Priority = CAT Level
            ws.Cell(row, 11).Value = r.Tool ?? string.Empty;
            ws.Cell(row, 12).Value = r.SourceFile ?? string.Empty;
            ws.Cell(row, 13).Value = r.VerifiedAt?.ToString("o") ?? string.Empty;
            row++;
        }
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

    private static string GetSystemName(IReadOnlyDictionary<string, string> options)
    {
        if (options.TryGetValue("system-name", out var name) && !string.IsNullOrWhiteSpace(name))
            return name;

        if (options.TryGetValue("bundle-root", out var bundleRoot) && !string.IsNullOrWhiteSpace(bundleRoot))
            return Path.GetFileName(bundleRoot);

        return string.Empty;
    }

    private static int SeverityOrder(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
            return 3;

        return severity!.Trim().ToLowerInvariant() switch
        {
            "high" => 0,
            "medium" => 1,
            "low" => 2,
            _ => 3
        };
    }

    private static bool IsStatus(string? status, string expected)
    {
        return string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);
    }
}
