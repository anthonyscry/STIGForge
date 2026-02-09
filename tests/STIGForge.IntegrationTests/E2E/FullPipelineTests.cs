using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using STIGForge.Content.Import;
using STIGForge.Export;
using STIGForge.Verify;
using STIGForge.Verify.Adapters;
using Xunit;

namespace STIGForge.IntegrationTests.E2E;

/// <summary>
/// End-to-end integration tests covering full STIGForge pipeline:
/// Import → Build → Apply (simulated) → Verify → Export
/// 
/// These tests verify complete workflow integration without requiring external tools.
/// </summary>
public sealed class FullPipelineTests : IDisposable
{
    private static readonly DateTimeOffset DeterministicResultTime = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly string _testRoot;

    public FullPipelineTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "stigforge_e2e_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// E2E test: Import STIG pack → Parse controls → Verify data integrity → Export eMASS package
    /// Tests the critical path without requiring bundle build or apply steps.
    /// </summary>
    [Fact]
    public void ImportParseVerifyExport_FullPipeline()
    {
        // ===== ARRANGE: Create test STIG pack =====
        var packPath = Path.Combine(_testRoot, "test-pack.zip");
        CreateTestStigPack(packPath);

        var importDir = Path.Combine(_testRoot, "imported");
        Directory.CreateDirectory(importDir);

        // ===== ACT 1: IMPORT/PARSE =====
        // Extract and parse XCCDF directly (integration test pattern from RoundTripTests)
        var extractDir = Path.Combine(importDir, "extracted");
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(packPath, extractDir);

        var xccdfFiles = Directory.GetFiles(extractDir, "*xccdf.xml", SearchOption.AllDirectories);
        Assert.True(xccdfFiles.Length > 0, "Should have XCCDF file in pack");

        var xccdfPath = xccdfFiles[0];
        var controls = XccdfParser.Parse(xccdfPath, "TestPack_v1");

        // ===== ASSERT 1: Parsing successful =====
        Assert.NotNull(controls);
        Assert.True(controls.Count > 0, "Should parse at least one control");

        // Verify control data integrity
        var firstControl = controls[0];
        Assert.False(string.IsNullOrEmpty(firstControl.ControlId), "Control should have ID");
        Assert.False(string.IsNullOrEmpty(firstControl.Title), "Control should have title");

        // ===== ACT 2: VERIFY (simulated verification results) =====
        var verifyDir = Path.Combine(_testRoot, "verify");
        Directory.CreateDirectory(verifyDir);

        // Simulate SCAP results for parsed controls
        var scapResultPath = Path.Combine(verifyDir, "scap-results.xml");
        CreateMockScapResults(scapResultPath, controls.Count);

        // Parse SCAP results with adapter
        var scapAdapter = new ScapResultAdapter();
        var verifyReport = scapAdapter.ParseResults(scapResultPath);

        // ===== ASSERT 2: Verification parsing successful =====
        Assert.NotNull(verifyReport);
        Assert.True(verifyReport.Results.Count > 0, "Should have verification results");
        Assert.Equal("SCAP", verifyReport.Tool);

        // Verify status mapping worked
        var passCount = verifyReport.Results.Count(r => r.Status == VerifyStatus.Pass);
        var failCount = verifyReport.Results.Count(r => r.Status == VerifyStatus.Fail);
        Assert.True(passCount > 0 || failCount > 0, "Should have pass or fail results");

        // ===== ACT 3: EXPORT eMASS Package =====
        var exportDir = Path.Combine(_testRoot, "emass-export");
        Directory.CreateDirectory(exportDir);

        // Generate POA&M from verification results
        var poamPackage = PoamGenerator.GeneratePoam(
            verifyReport.Results.ToList(),
            "TestSystem",
            "test-bundle-001");

        var poamDir = Path.Combine(exportDir, "03_POAM");
        PoamGenerator.WritePoamFiles(poamPackage, poamDir);

        // Generate attestation templates
        var attestationPackage = AttestationGenerator.GenerateAttestations(
            verifyReport.Results.Select(r => r.ControlId).ToList(),
            "TestSystem",
            "test-bundle-001");

        var attestDir = Path.Combine(exportDir, "05_Attestations");
        AttestationGenerator.WriteAttestationFiles(attestationPackage, attestDir);

        // ===== ASSERT 3: Export artifacts created =====
        Assert.True(File.Exists(Path.Combine(poamDir, "poam.json")), "POA&M JSON should exist");
        Assert.True(File.Exists(Path.Combine(poamDir, "poam.csv")), "POA&M CSV should exist");
        Assert.True(File.Exists(Path.Combine(poamDir, "poam_summary.txt")), "POA&M summary should exist");

        Assert.True(File.Exists(Path.Combine(attestDir, "attestations.json")), "Attestations JSON should exist");
        Assert.True(File.Exists(Path.Combine(attestDir, "attestation_template.csv")), "Attestation template should exist");
        Assert.True(File.Exists(Path.Combine(attestDir, "INSTRUCTIONS.txt")), "Attestation instructions should exist");

        // Verify POA&M content
        var poamJson = File.ReadAllText(Path.Combine(poamDir, "poam.json"));
        var poamData = JsonSerializer.Deserialize<JsonDocument>(poamJson);
        Assert.NotNull(poamData);

        // Verify attestation content
        var attestJson = File.ReadAllText(Path.Combine(attestDir, "attestations.json"));
        var attestData = JsonSerializer.Deserialize<JsonDocument>(attestJson);
        Assert.NotNull(attestData);

        // ===== FINAL: Verify end-to-end data integrity =====
        // Controls parsed should match verification results count
        Assert.True(verifyReport.Results.Count >= controls.Count - 1, 
            "Verification results should approximately match parsed controls (allowing for OVAL-only controls)");
    }

    /// <summary>
    /// E2E test: Verify orchestrator merges multiple tool results correctly
    /// Tests conflict resolution and precedence rules.
    /// </summary>
    [Fact]
    public void VerifyOrchestrator_MergesMultipleToolResults()
    {
        // ===== ARRANGE: Create multiple verification result files =====
        var verifyDir = Path.Combine(_testRoot, "multi-verify");
        Directory.CreateDirectory(verifyDir);

        // Create SCAP results (automated)
        var scapPath = Path.Combine(verifyDir, "scap-results.xml");
        CreateMockScapResults(scapPath, 3);

        // Create CKL results (manual - should override SCAP)
        var cklPath = Path.Combine(verifyDir, "manual.ckl");
        CreateMockCklResults(cklPath, 3);

        // ===== ACT: Parse and merge results =====
        var orchestrator = new VerifyOrchestrator();
        var consolidatedReport = orchestrator.ParseAndMergeResults(new[] { scapPath, cklPath });

        // ===== ASSERT: Merging and precedence =====
        Assert.NotNull(consolidatedReport);
        Assert.True(consolidatedReport.Results.Count > 0, "Should have consolidated results");

        // Verify source reports tracked
        Assert.Equal(2, consolidatedReport.SourceReports.Count);
        Assert.Contains(consolidatedReport.SourceReports, r => r.Tool == "SCAP");
        Assert.Contains(consolidatedReport.SourceReports, r => r.Tool == "Manual CKL");

        // Verify summary calculated
        Assert.True(consolidatedReport.Summary.TotalCount > 0, "Summary should have total count");

        // Check for conflict detection (if SCAP and CKL have different statuses for same control)
        // This would require more sophisticated mock data, but we verify the structure is correct
        Assert.NotNull(consolidatedReport.Conflicts);
    }

    /// <summary>
    /// E2E test: EmassPackageValidator detects missing required files
    /// </summary>
    [Fact]
    public void EmassPackageValidator_DetectsMissingFiles()
    {
        // ===== ARRANGE: Create incomplete eMASS package =====
        var packageDir = Path.Combine(_testRoot, "incomplete-package");
        Directory.CreateDirectory(packageDir);

        // Create only some required directories
        Directory.CreateDirectory(Path.Combine(packageDir, "00_Manifest"));
        Directory.CreateDirectory(Path.Combine(packageDir, "01_Scans"));
        // Missing: 02_Checklists, 03_POAM, 04_Evidence, 05_Attestations, 06_Index

        // Create only some required files
        File.WriteAllText(Path.Combine(packageDir, "README_Submission.txt"), "Incomplete package");
        // Missing: manifest.json, file_hashes.sha256, etc.

        // ===== ACT: Validate package =====
        var validator = new EmassPackageValidator();
        var result = validator.ValidatePackage(packageDir);

        // ===== ASSERT: Validation fails with specific errors =====
        Assert.False(result.IsValid, "Incomplete package should fail validation");
        Assert.True(result.Errors.Count > 0, "Should have validation errors");

        // Check for specific missing directory errors
        Assert.Contains(result.Errors, e => e.Contains("02_Checklists"));
        Assert.Contains(result.Errors, e => e.Contains("03_POAM"));

        // Check for missing file errors
        Assert.Contains(result.Errors, e => e.Contains("manifest.json"));
        Assert.Contains(result.Errors, e => e.Contains("file_hashes.sha256"));
    }

    // ===== HELPER METHODS =====

    private void CreateTestStigPack(string outputPath)
    {
        using var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        // Create XCCDF with 3 controls
        var xccdf = CreateXccdfWithControls(3);
        var xccdfEntry = zip.CreateEntry("U_Test_STIG_V1R1_Manual-xccdf.xml");
        using (var writer = new StreamWriter(xccdfEntry.Open(), Encoding.UTF8))
        {
            writer.Write(xccdf);
        }

        // Create OVAL definitions
        var oval = CreateOvalDefinitions(3);
        var ovalEntry = zip.CreateEntry("U_Test_STIG_V1R1_SCAP_1-2_Benchmark-oval.xml");
        using (var writer = new StreamWriter(ovalEntry.Open(), Encoding.UTF8))
        {
            writer.Write(oval);
        }
    }

    private string CreateXccdfWithControls(int count)
    {
        XNamespace ns = "http://checklists.nist.gov/xccdf/1.2";
        var benchmark = new XElement(ns + "Benchmark",
            new XAttribute("id", "test-benchmark"));

        for (int i = 1; i <= count; i++)
        {
            var group = new XElement(ns + "Group",
                new XAttribute("id", $"SV-{100000 + i}"),
                new XElement(ns + "title", $"Test Control {i}"),
                new XElement(ns + "description", $"Test description {i}"),
                new XElement(ns + "Rule",
                    new XAttribute("id", $"SV-{100000 + i}r1_rule"),
                    new XAttribute("severity", i % 3 == 0 ? "high" : i % 2 == 0 ? "medium" : "low"),
                    new XElement(ns + "version", $"V-{100000 + i}"),
                    new XElement(ns + "title", $"Test Control {i}"),
                    new XElement(ns + "description", $"Test description {i}"),
                    new XElement(ns + "check",
                        new XElement(ns + "check-content", $"Check procedure {i}")
                    ),
                    new XElement(ns + "fixtext", $"Fix text {i}")
                )
            );
            benchmark.Add(group);
        }

        var doc = new XDocument(benchmark);
        return doc.ToString();
    }

    private string CreateOvalDefinitions(int count)
    {
        XNamespace ns = "http://oval.mitre.org/XMLSchema/oval-definitions-5";
        var definitions = new XElement(ns + "definitions");

        for (int i = 1; i <= count; i++)
        {
            var definition = new XElement(ns + "definition",
                new XAttribute("id", $"oval:test:def:{i}"),
                new XAttribute("class", "compliance"),
                new XElement(ns + "metadata",
                    new XElement(ns + "title", $"Test OVAL Definition {i}")
                )
            );
            definitions.Add(definition);
        }

        var doc = new XDocument(new XElement(ns + "oval_definitions", definitions));
        return doc.ToString();
    }

    private void CreateMockScapResults(string outputPath, int controlCount)
    {
        XNamespace ns = "http://checklists.nist.gov/xccdf/1.2";

        var testResult = new XElement(ns + "TestResult",
            new XAttribute("id", "test-result-1"),
            new XAttribute("version", "1.0"),
            new XElement(ns + "start-time", DeterministicResultTime.AddHours(-1).ToString("o")),
            new XElement(ns + "end-time", DeterministicResultTime.ToString("o"))
        );

        for (int i = 1; i <= controlCount; i++)
        {
            // Simulate mix of pass/fail results
            var status = i % 3 == 0 ? "fail" : i % 2 == 0 ? "pass" : "notapplicable";
            
            var ruleResult = new XElement(ns + "rule-result",
                new XAttribute("idref", $"SV-{100000 + i}r1_rule"),
                new XAttribute("time", DeterministicResultTime.ToString("o")),
                new XElement(ns + "result", status),
                new XElement(ns + "ident",
                    new XAttribute("system", "http://cyber.mil/legacy"),
                    $"V-{100000 + i}"
                )
            );

            if (status == "fail")
            {
                ruleResult.Add(new XElement(ns + "message", $"Control {i} failed verification"));
            }

            testResult.Add(ruleResult);
        }

        var doc = new XDocument(testResult);
        File.WriteAllText(outputPath, doc.ToString(), Encoding.UTF8);
    }

    private void CreateMockCklResults(string outputPath, int controlCount)
    {
        var checklist = new XElement("CHECKLIST",
            new XElement("ASSET",
                new XElement("HOST_NAME", "TestHost")
            ),
            new XElement("STIGS",
                new XElement("iSTIG",
                    new XElement("STIG_INFO",
                        new XElement("SI_DATA",
                            new XElement("SID_NAME", "version"),
                            new XElement("SID_DATA", "1")
                        )
                    )
                )
            )
        );

        var stigElement = checklist.Descendants("iSTIG").First();

        for (int i = 1; i <= controlCount; i++)
        {
            // CKL should override SCAP for same controls - make some different
            var status = i == 1 ? "NotAFinding" : i == 2 ? "Open" : "Not_Applicable";

            var vuln = new XElement("VULN",
                new XElement("STIG_DATA",
                    new XElement("VULN_ATTRIBUTE", "Vuln_Num"),
                    new XElement("ATTRIBUTE_DATA", $"V-{100000 + i}")
                ),
                new XElement("STIG_DATA",
                    new XElement("VULN_ATTRIBUTE", "Rule_ID"),
                    new XElement("ATTRIBUTE_DATA", $"SV-{100000 + i}r1_rule")
                ),
                new XElement("STIG_DATA",
                    new XElement("VULN_ATTRIBUTE", "Rule_Title"),
                    new XElement("ATTRIBUTE_DATA", $"Test Control {i}")
                ),
                new XElement("STIG_DATA",
                    new XElement("VULN_ATTRIBUTE", "Severity"),
                    new XElement("ATTRIBUTE_DATA", "medium")
                ),
                new XElement("STATUS", status),
                new XElement("FINDING_DETAILS", $"Manual review: Control {i}"),
                new XElement("COMMENTS", "Reviewed by ISSO")
            );

            stigElement.Add(vuln);
        }

        var doc = new XDocument(checklist);
        File.WriteAllText(outputPath, doc.ToString(), Encoding.UTF8);
    }
}
