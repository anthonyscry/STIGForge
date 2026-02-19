using ClosedXML.Excel;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Export;

public sealed class ExcelExportAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ExcelExportAdapter _adapter;

    public ExcelExportAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge_excel_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _adapter = new ExcelExportAdapter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void FormatName_Is_Excel()
    {
        Assert.Equal("Excel", _adapter.FormatName);
    }

    [Fact]
    public void SupportedExtensions_Contains_Xlsx()
    {
        Assert.Contains(".xlsx", _adapter.SupportedExtensions);
    }

    [Fact]
    public async Task ExportAsync_ProducesFileWithFourSheets()
    {
        var results = CreateSampleResults();

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "four_sheets_test",
            BundleRoot = "/bundles/test-system"
        }, CancellationToken.None);

        Assert.True(result.Success);
        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        Assert.Equal(4, workbook.Worksheets.Count);

        var sheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();
        Assert.Contains("Summary", sheetNames);
        Assert.Contains("All Controls", sheetNames);
        Assert.Contains("Open Findings", sheetNames);
        Assert.Contains("Coverage", sheetNames);
    }

    [Fact]
    public async Task ExportAsync_AllControlsTab_HasCorrectRowCount()
    {
        var results = CreateSampleResults(); // 4 results

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "rowcount_test"
        }, CancellationToken.None);

        Assert.True(result.Success);
        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        var ws = workbook.Worksheet("All Controls");
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        Assert.Equal(5, lastRow); // 1 header + 4 data rows
    }

    [Fact]
    public async Task ExportAsync_AllControlsTab_HasCorrectHeaders()
    {
        var results = CreateSampleResults();

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "headers_test"
        }, CancellationToken.None);

        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        var ws = workbook.Worksheet("All Controls");

        Assert.Equal("System Name", ws.Cell(1, 1).GetString());
        Assert.Equal("Vulnerability ID", ws.Cell(1, 2).GetString());
        Assert.Equal("Rule ID", ws.Cell(1, 3).GetString());
        Assert.Equal("STIG Title", ws.Cell(1, 4).GetString());
        Assert.Equal("Severity", ws.Cell(1, 5).GetString());
        Assert.Equal("CAT Level", ws.Cell(1, 6).GetString());
        Assert.Equal("Status", ws.Cell(1, 7).GetString());
        Assert.Equal("Finding Details", ws.Cell(1, 8).GetString());
        Assert.Equal("Comments", ws.Cell(1, 9).GetString());
        Assert.Equal("Remediation Priority", ws.Cell(1, 10).GetString());
        Assert.Equal("Tool", ws.Cell(1, 11).GetString());
        Assert.Equal("Source File", ws.Cell(1, 12).GetString());
        Assert.Equal("Verified At", ws.Cell(1, 13).GetString());
    }

    [Fact]
    public async Task ExportAsync_AllControlsTab_SortsBySeverityThenVulnId()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-300", Severity = "low", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-100", Severity = "high", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-200", Severity = "medium", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-050", Severity = "high", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "sort_test"
        }, CancellationToken.None);

        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        var ws = workbook.Worksheet("All Controls");

        // high first (sorted by VulnId within severity), then medium, then low
        Assert.Equal("V-050", ws.Cell(2, 2).GetString());
        Assert.Equal("V-100", ws.Cell(3, 2).GetString());
        Assert.Equal("V-200", ws.Cell(4, 2).GetString());
        Assert.Equal("V-300", ws.Cell(5, 2).GetString());
    }

    [Fact]
    public async Task ExportAsync_OpenFindingsTab_ContainsOnlyFailOpen()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-1", Severity = "high", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-2", Severity = "medium", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-3", Severity = "low", Status = "Open", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-4", Severity = "high", Status = "Not_Applicable", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "open_only_test"
        }, CancellationToken.None);

        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        var ws = workbook.Worksheet("Open Findings");
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        Assert.Equal(3, lastRow); // header + 2 open findings (Fail + Open)

        // Verify the VulnIds present are only Fail/Open ones
        var vulnIds = new List<string>();
        for (int r = 2; r <= lastRow; r++)
            vulnIds.Add(ws.Cell(r, 2).GetString());

        Assert.Contains("V-1", vulnIds);
        Assert.Contains("V-3", vulnIds);
        Assert.DoesNotContain("V-2", vulnIds);
        Assert.DoesNotContain("V-4", vulnIds);
    }

    [Fact]
    public async Task ExportAsync_SummaryTab_HasCorrectMetrics()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-1", Severity = "high", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-2", Severity = "high", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-3", Severity = "medium", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-4", Severity = "low", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "summary_test"
        }, CancellationToken.None);

        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        var ws = workbook.Worksheet("Summary");

        // Title
        Assert.Equal("STIGForge Compliance Report", ws.Cell(1, 1).GetString());

        // Metrics: Total=4, Pass=3, Fail=1
        Assert.Equal("Total Controls", ws.Cell(6, 1).GetString());
        Assert.Equal(4, (int)ws.Cell(6, 2).GetDouble());
        Assert.Equal("Pass", ws.Cell(7, 1).GetString());
        Assert.Equal(3, (int)ws.Cell(7, 2).GetDouble());
        Assert.Equal("Fail", ws.Cell(8, 1).GetString());
        Assert.Equal(1, (int)ws.Cell(8, 2).GetDouble());

        // Pass rate: 75%
        Assert.Equal("Pass Rate (%)", ws.Cell(11, 1).GetString());
        Assert.Equal(75.0, ws.Cell(11, 2).GetDouble());
    }

    [Fact]
    public async Task ExportAsync_CoverageTab_HasSeverityBreakdown()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-1", Severity = "high", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-2", Severity = "high", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-3", Severity = "medium", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-4", Severity = "low", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "coverage_test"
        }, CancellationToken.None);

        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        var ws = workbook.Worksheet("Coverage");

        // Header
        Assert.Equal("Severity", ws.Cell(1, 1).GetString());
        Assert.Equal("Pass Rate (%)", ws.Cell(1, 7).GetString());

        // CAT I row: 2 total, 1 pass, 1 fail
        Assert.Equal("CAT I", ws.Cell(2, 1).GetString());
        Assert.Equal(2, (int)ws.Cell(2, 2).GetDouble());
        Assert.Equal(1, (int)ws.Cell(2, 3).GetDouble());
        Assert.Equal(1, (int)ws.Cell(2, 4).GetDouble());

        // CAT II row: 1 total, 1 pass
        Assert.Equal("CAT II", ws.Cell(3, 1).GetString());
        Assert.Equal(1, (int)ws.Cell(3, 2).GetDouble());

        // CAT III row: 1 total, 1 pass
        Assert.Equal("CAT III", ws.Cell(4, 1).GetString());
        Assert.Equal(1, (int)ws.Cell(4, 2).GetDouble());
    }

    [Fact]
    public async Task ExportAsync_CoverageTab_HasTotalsRow()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-1", Severity = "high", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-2", Severity = "medium", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-3", Severity = "low", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "totals_test"
        }, CancellationToken.None);

        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        var ws = workbook.Worksheet("Coverage");

        // Totals row is at row 6 (header + 4 severity levels + totals)
        Assert.Equal("Total", ws.Cell(6, 1).GetString());
        Assert.Equal(3, (int)ws.Cell(6, 2).GetDouble()); // total
        Assert.Equal(2, (int)ws.Cell(6, 3).GetDouble()); // pass
        Assert.Equal(1, (int)ws.Cell(6, 4).GetDouble()); // fail
    }

    [Fact]
    public async Task ExportAsync_EmptyResults_ProducesValidWorkbook()
    {
        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = Array.Empty<ControlResult>(),
            OutputDirectory = _tempDir,
            FileNameStem = "empty_test"
        }, CancellationToken.None);

        Assert.True(result.Success);
        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        Assert.Equal(4, workbook.Worksheets.Count);

        // Summary metrics should show zeros
        var summary = workbook.Worksheet("Summary");
        Assert.Equal(0, (int)summary.Cell(6, 2).GetDouble()); // Total = 0

        // All Controls should have header only
        var allControls = workbook.Worksheet("All Controls");
        var lastRow = allControls.LastRowUsed()?.RowNumber() ?? 0;
        Assert.Equal(1, lastRow); // header only
    }

    [Fact]
    public async Task ExportAsync_PartialFileDeletedOnError()
    {
        var existingFile = Path.Combine(_tempDir, "blocker.txt");
        File.WriteAllText(existingFile, "I am a file, not a directory");
        var badOutputDir = Path.Combine(existingFile, "nested", "impossible");

        var ex = await Assert.ThrowsAnyAsync<IOException>(async () =>
        {
            await _adapter.ExportAsync(new ExportAdapterRequest
            {
                Results = new List<ControlResult>
                {
                    new() { VulnId = "V-1", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" }
                },
                OutputDirectory = badOutputDir,
                FileNameStem = "should_not_exist"
            }, CancellationToken.None);
        });
        Assert.NotNull(ex);

        // No temp xlsx files should remain (fail-closed cleanup)
        var xlsxFiles = Directory.GetFiles(_tempDir, "*_tmp_*.xlsx", SearchOption.AllDirectories);
        Assert.Empty(xlsxFiles);
    }

    [Fact]
    public async Task ExportAsync_SystemNameFromOptions()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-1", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var options = new Dictionary<string, string> { ["system-name"] = "PROD-WEB-01" };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "sysname_test",
            BundleRoot = "/bundles/some-bundle",
            Options = options
        }, CancellationToken.None);

        using var workbook = new XLWorkbook(result.OutputPaths[0]);
        var ws = workbook.Worksheet("All Controls");
        Assert.Equal("PROD-WEB-01", ws.Cell(2, 1).GetString());
    }

    private static List<ControlResult> CreateSampleResults()
    {
        return new List<ControlResult>
        {
            new()
            {
                VulnId = "V-220697", RuleId = "SV-220697r569187_rule",
                Title = "Windows 10 Account Lockout", Status = "Pass",
                Severity = "high", Tool = "SCAP", SourceFile = "test.xml",
                VerifiedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                VulnId = "V-220698", RuleId = "SV-220698r569190_rule",
                Title = "Windows 10 Password Length", Status = "Fail",
                Severity = "medium", FindingDetails = "Registry key not set",
                Tool = "SCAP", SourceFile = "test.xml",
                VerifiedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                VulnId = "V-220699", RuleId = "SV-220699r569193_rule",
                Title = "Windows 10 Audit Policy", Status = "Pass",
                Severity = "low", Tool = "SCAP", SourceFile = "test.xml",
                VerifiedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                VulnId = "V-220700", RuleId = "SV-220700r569196_rule",
                Title = "Windows 10 Remote Desktop", Status = "Open",
                Severity = "high", FindingDetails = "RDP enabled without NLA",
                Tool = "SCAP", SourceFile = "test.xml",
                VerifiedAt = DateTimeOffset.UtcNow
            }
        };
    }
}
