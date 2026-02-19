using System.Xml.Linq;
using STIGForge.Export;
using STIGForge.Verify;
using STIGForge.Verify.Adapters;

namespace STIGForge.UnitTests.Export;

public sealed class XccdfExportAdapterTests : IDisposable
{
    private static readonly XNamespace XccdfNs = "http://checklists.nist.gov/xccdf/1.2";
    private readonly string _tempDir;
    private readonly XccdfExportAdapter _adapter;

    public XccdfExportAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge_xccdf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _adapter = new XccdfExportAdapter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void FormatName_Is_XCCDF()
    {
        Assert.Equal("XCCDF", _adapter.FormatName);
    }

    [Fact]
    public void SupportedExtensions_Contains_Xml()
    {
        Assert.Contains(".xml", _adapter.SupportedExtensions);
    }

    [Fact]
    public async Task ExportAsync_ProducesValidXccdfXml()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-220697",
                RuleId = "SV-220697r569187_rule",
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
            FileNameStem = "test_output"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Single(result.OutputPaths);
        var outputPath = result.OutputPaths[0];
        Assert.True(File.Exists(outputPath));

        var doc = XDocument.Load(outputPath);
        Assert.NotNull(doc.Root);
        Assert.Equal("Benchmark", doc.Root!.Name.LocalName);
        Assert.Equal(XccdfNs, doc.Root.Name.Namespace);

        var testResult = doc.Descendants(XccdfNs + "TestResult").FirstOrDefault();
        Assert.NotNull(testResult);
        var ruleResults = testResult!.Elements(XccdfNs + "rule-result").ToList();
        Assert.Equal(2, ruleResults.Count);
    }

    [Fact]
    public async Task ExportAsync_RoundTrip_ScapResultAdapterCanParse()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-100001",
                RuleId = "SV-100001r1_rule",
                Status = "Pass",
                Severity = "high",
                VerifiedAt = DateTimeOffset.UtcNow,
                Tool = "SCAP",
                SourceFile = "test.xml"
            },
            new()
            {
                VulnId = "V-100002",
                RuleId = "SV-100002r1_rule",
                Status = "Fail",
                Severity = "medium",
                FindingDetails = "Finding detail text",
                VerifiedAt = DateTimeOffset.UtcNow,
                Tool = "SCAP",
                SourceFile = "test.xml"
            },
            new()
            {
                VulnId = "V-100003",
                RuleId = "SV-100003r1_rule",
                Status = "NotApplicable",
                Severity = "low",
                VerifiedAt = DateTimeOffset.UtcNow,
                Tool = "SCAP",
                SourceFile = "test.xml"
            }
        };

        var exportResult = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "roundtrip_test"
        }, CancellationToken.None);

        Assert.True(exportResult.Success);
        var outputPath = exportResult.OutputPaths[0];

        var scapAdapter = new ScapResultAdapter();
        Assert.True(scapAdapter.CanHandle(outputPath));

        var parsed = scapAdapter.ParseResults(outputPath);
        Assert.Equal(3, parsed.Results.Count);
    }

    [Fact]
    public async Task ExportAsync_AllElementsHaveXccdfNamespace()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = "V-999",
                RuleId = "SV-999r1_rule",
                Status = "Pass",
                Severity = "high",
                FindingDetails = "Some detail",
                VerifiedAt = DateTimeOffset.UtcNow,
                Tool = "SCAP",
                SourceFile = "test.xml"
            }
        };

        var exportResult = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "ns_test"
        }, CancellationToken.None);

        var doc = XDocument.Load(exportResult.OutputPaths[0]);
        foreach (var element in doc.Descendants())
        {
            Assert.Equal(XccdfNs, element.Name.Namespace);
        }
    }

    [Fact]
    public async Task ExportAsync_EmptyResults_ProducesValidXml()
    {
        var exportResult = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = Array.Empty<ControlResult>(),
            OutputDirectory = _tempDir,
            FileNameStem = "empty_test"
        }, CancellationToken.None);

        Assert.True(exportResult.Success);
        var outputPath = exportResult.OutputPaths[0];
        Assert.True(File.Exists(outputPath));

        var doc = XDocument.Load(outputPath);
        Assert.Equal("Benchmark", doc.Root!.Name.LocalName);
        Assert.Equal(XccdfNs, doc.Root.Name.Namespace);

        var testResult = doc.Descendants(XccdfNs + "TestResult").FirstOrDefault();
        Assert.NotNull(testResult);
        var ruleResults = testResult!.Elements(XccdfNs + "rule-result").ToList();
        Assert.Empty(ruleResults);

        // ScapResultAdapter should still handle it
        var scapAdapter = new ScapResultAdapter();
        Assert.True(scapAdapter.CanHandle(outputPath));
    }

    [Fact]
    public async Task ExportAsync_NullFields_HandledGracefully()
    {
        var results = new List<ControlResult>
        {
            new()
            {
                VulnId = null,
                RuleId = null,
                Status = null,
                Severity = null,
                FindingDetails = null,
                Comments = null,
                Title = null,
                VerifiedAt = null,
                Tool = string.Empty,
                SourceFile = string.Empty
            }
        };

        var exportResult = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "null_fields_test"
        }, CancellationToken.None);

        Assert.True(exportResult.Success);
        var doc = XDocument.Load(exportResult.OutputPaths[0]);

        var ruleResult = doc.Descendants(XccdfNs + "rule-result").First();
        Assert.Equal("unknown", ruleResult.Attribute("idref")?.Value);
        Assert.Equal("unknown", ruleResult.Element(XccdfNs + "result")?.Value);
        Assert.Null(ruleResult.Attribute("time"));
        Assert.Null(ruleResult.Attribute("weight"));
        // No ident or message elements when VulnId/FindingDetails are null
        Assert.Null(ruleResult.Element(XccdfNs + "ident"));
        Assert.Null(ruleResult.Element(XccdfNs + "message"));
    }

    [Fact]
    public async Task ExportAsync_StatusMapping_RoundTripsCorrectly()
    {
        var statuses = new[] { "Pass", "Fail", "NotApplicable", "NotReviewed", "Informational", "Error" };
        var results = statuses.Select((s, i) => new ControlResult
        {
            VulnId = $"V-{i + 1}",
            RuleId = $"SV-{i + 1}r1_rule",
            Status = s,
            Severity = "medium",
            VerifiedAt = DateTimeOffset.UtcNow,
            Tool = "SCAP",
            SourceFile = "test.xml"
        }).ToList();

        var exportResult = await _adapter.ExportAsync(new ExportAdapterRequest
        {
            Results = results,
            OutputDirectory = _tempDir,
            FileNameStem = "status_test"
        }, CancellationToken.None);

        var scapAdapter = new ScapResultAdapter();
        var parsed = scapAdapter.ParseResults(exportResult.OutputPaths[0]);
        Assert.Equal(statuses.Length, parsed.Results.Count);
    }

    [Fact]
    public async Task ExportAsync_PartialFileDeletedOnError()
    {
        // Use a non-existent nested path under a file (not a directory) to cause write failure
        var existingFile = Path.Combine(_tempDir, "blocker.txt");
        File.WriteAllText(existingFile, "I am a file, not a directory");
        var badOutputDir = Path.Combine(existingFile, "nested", "impossible");

        var ex = await Assert.ThrowsAnyAsync<IOException>(async () =>
        {
            await _adapter.ExportAsync(new ExportAdapterRequest
            {
                Results = new List<ControlResult>
                {
                    new() { VulnId = "V-1", RuleId = "SV-1r1_rule", Status = "Pass", Tool = "SCAP", SourceFile = "t.xml" }
                },
                OutputDirectory = badOutputDir,
                FileNameStem = "should_not_exist"
            }, CancellationToken.None);
        });
        Assert.NotNull(ex);

        // Verify no .tmp file exists anywhere under temp dir
        var tmpFiles = Directory.GetFiles(_tempDir, "*.tmp", SearchOption.AllDirectories);
        Assert.Empty(tmpFiles);
    }
}
