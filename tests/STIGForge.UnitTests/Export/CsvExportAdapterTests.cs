using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Export;

public sealed class CsvExportAdapterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly CsvExportAdapter _adapter;

    public CsvExportAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge_csv_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _adapter = new CsvExportAdapter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void FormatName_Is_CSV()
    {
        Assert.Equal("CSV", _adapter.FormatName);
    }

    [Fact]
    public void SupportedExtensions_Contains_Csv()
    {
        Assert.Contains(".csv", _adapter.SupportedExtensions);
    }

    [Fact]
    public async Task ExportAsync_ProducesCsvWithHeaderRow()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-220697",
                RuleId = "SV-220697r569187_rule",
                Title = "Windows 10 Account Lockout",
                Status = "Pass",
                Severity = "high",
                VerifiedAt = DateTimeOffset.UtcNow,
                Tool = "SCAP",
                SourceFile = "test.xml"
            },
            new()
            {
                VulnId = "V-220698",
                RuleId = "SV-220698r569190_rule",
                Title = "Windows 10 Password Length",
                Status = "Fail",
                Severity = "medium",
                FindingDetails = "Registry key not set",
                VerifiedAt = DateTimeOffset.UtcNow,
                Tool = "SCAP",
                SourceFile = "test.xml"
            }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "header_test",
            BundleRoot = "/bundles/test-system"
        }, CancellationToken.None);

        Assert.True(result.Success);
        var lines = File.ReadAllLines(result.OutputPaths[0]);
        Assert.True(lines.Length >= 1);

        var header = lines[0];
        Assert.Contains("System Name", header);
        Assert.Contains("Vulnerability ID", header);
        Assert.Contains("Rule ID", header);
        Assert.Contains("STIG Title", header);
        Assert.Contains("Severity", header);
        Assert.Contains("CAT Level", header);
        Assert.Contains("Status", header);
        Assert.Contains("Finding Details", header);
        Assert.Contains("Comments", header);
        Assert.Contains("Remediation Priority", header);
        Assert.Contains("Tool", header);
        Assert.Contains("Source File", header);
        Assert.Contains("Verified At", header);
    }

    [Fact]
    public async Task ExportAsync_ProducesCsvWithCorrectRowCount()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-1", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-2", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-3", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "rowcount_test"
        }, CancellationToken.None);

        Assert.True(result.Success);
        var content = File.ReadAllText(result.OutputPaths[0]);
        // Split by CRLF, filter empty trailing line
        var lines = content.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length); // header + 3 data rows
    }

    [Fact]
    public async Task ExportAsync_EscapesCommasInFields()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-1",
                FindingDetails = "Missing key, value not set",
                Status = "Fail",
                Tool = "SCAP",
                SourceFile = "t.xml"
            }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "comma_test"
        }, CancellationToken.None);

        var content = File.ReadAllText(result.OutputPaths[0]);
        Assert.Contains("\"Missing key, value not set\"", content);
    }

    [Fact]
    public async Task ExportAsync_EscapesDoubleQuotesInFields()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-1",
                Comments = "Set \"AllowTelemetry\" to 0",
                Status = "Fail",
                Tool = "SCAP",
                SourceFile = "t.xml"
            }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "quote_test"
        }, CancellationToken.None);

        var content = File.ReadAllText(result.OutputPaths[0]);
        // Double quotes inside must be doubled and the field must be quoted
        Assert.Contains("\"Set \"\"AllowTelemetry\"\" to 0\"", content);
    }

    [Fact]
    public async Task ExportAsync_EscapesNewlinesInFields()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-1",
                FindingDetails = "Line one\nLine two",
                Status = "Fail",
                Tool = "SCAP",
                SourceFile = "t.xml"
            }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "newline_test"
        }, CancellationToken.None);

        var content = File.ReadAllText(result.OutputPaths[0]);
        Assert.Contains("\"Line one\nLine two\"", content);
    }

    [Fact]
    public async Task ExportAsync_EmptyResults_ProducesHeaderOnly()
    {
        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = Array.Empty<ControlResult>(),
            OutputDirectory = _tempDir,
            FileNameStem = "empty_test"
        }, CancellationToken.None);

        Assert.True(result.Success);
        var content = File.ReadAllText(result.OutputPaths[0]);
        var lines = content.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines); // header only
        Assert.Contains("System Name", lines[0]);
    }

    [Fact]
    public async Task ExportAsync_NullFields_WrittenAsEmpty()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = null,
                RuleId = null,
                Title = null,
                Status = null,
                Severity = null,
                FindingDetails = null,
                Comments = null,
                VerifiedAt = null,
                Tool = string.Empty,
                SourceFile = string.Empty
            }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "null_test"
        }, CancellationToken.None);

        Assert.True(result.Success);
        var content = File.ReadAllText(result.OutputPaths[0]);
        var lines = content.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); // header + 1 data row

        var dataRow = lines[1];
        // All fields should be empty or "Unknown" for CAT Level
        Assert.Contains("Unknown", dataRow);
        // Should not contain the literal string "null"
        Assert.DoesNotContain("null", dataRow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_CatLevelMapping()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-1", Severity = "high", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-2", Severity = "medium", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-3", Severity = "low", Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" },
            new() { VulnId = "V-4", Severity = null, Status = "Fail", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "cat_test"
        }, CancellationToken.None);

        var content = File.ReadAllText(result.OutputPaths[0]);
        var lines = content.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("CAT I", lines[1]);   // high
        Assert.Contains("CAT II", lines[2]);  // medium
        Assert.Contains("CAT III", lines[3]); // low
        Assert.Contains("Unknown", lines[4]); // null
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
            FileNameStem = "sysname_opt_test",
            BundleRoot = "/bundles/some-bundle",
            Options = options
        }, CancellationToken.None);

        var content = File.ReadAllText(result.OutputPaths[0]);
        var lines = content.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        // System Name should be PROD-WEB-01, not "some-bundle"
        Assert.StartsWith("PROD-WEB-01,", lines[1]);
    }

    [Fact]
    public async Task ExportAsync_SystemNameFromBundleRoot()
    {
        var results = new List<ControlResult>
        {
            new() { VulnId = "V-1", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" }
        };

        var result = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "sysname_bundle_test",
            BundleRoot = "/bundles/my-test-system"
        }, CancellationToken.None);

        var content = File.ReadAllText(result.OutputPaths[0]);
        var lines = content.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("my-test-system,", lines[1]);
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

        var tmpFiles = Directory.GetFiles(_tempDir, "*.tmp", SearchOption.AllDirectories);
        Assert.Empty(tmpFiles);
    }
}
