using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Export;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Export;

public sealed class AttestationImporterTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static string WriteAttestationJson(TempDirectory tmp, IEnumerable<object> attestations)
    {
        var attestDir = Path.Combine(tmp.Path, "05_Attestations");
        Directory.CreateDirectory(attestDir);
        var path = Path.Combine(attestDir, "attestations.json");
        var package = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            systemName = "TestSystem",
            attestations
        };
        File.WriteAllText(path, JsonSerializer.Serialize(package));
        return path;
    }

    private static string WriteCsv(TempDirectory tmp, string content)
    {
        var csvPath = tmp.File("attestation.csv");
        File.WriteAllText(csvPath, content);
        return csvPath;
    }

    private static object MakeRecord(string controlId, string status = "Compliant") =>
        new
        {
            controlId,
            attestorName = string.Empty,
            attestorRole = string.Empty,
            complianceStatus = status,
            complianceEvidence = string.Empty,
            limitations = string.Empty
        };

    private static string StandardCsvHeader =>
        "Control ID,Attestor Name,Attestor Role,Attestation Date,Compliance Status,Compliance Evidence,Known Limitations,Next Review Date";

    // ── constructor/argument guards ───────────────────────────────────────────

    [Fact]
    public void ImportAttestations_ThrowsDirectoryNotFoundException_WhenPackageRootMissing()
    {
        Action act = () => AttestationImporter.ImportAttestations("/no/such/dir", "/some/file.csv");

        act.Should().Throw<DirectoryNotFoundException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void ImportAttestations_ThrowsFileNotFoundException_WhenCsvMissing()
    {
        using var tmp = new TempDirectory();

        Action act = () => AttestationImporter.ImportAttestations(tmp.Path, "/no/such/file.csv");

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ImportAttestations_ThrowsFileNotFoundException_WhenAttestationsJsonMissing()
    {
        using var tmp = new TempDirectory();
        var csvPath = WriteCsv(tmp, StandardCsvHeader + "\nAC-1,John,Owner,2025-01-01,Compliant,,," );

        Action act = () => AttestationImporter.ImportAttestations(tmp.Path, csvPath);

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*attestations.json*");
    }

    // ── happy path  -  CSV updates records ─────────────────────────────────────

    [Fact]
    public void ImportAttestations_UpdatesMatchingRecord_AndReturnsUpdatedCount()
    {
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("AC-1", ""), MakeRecord("AC-2", "")]);

        var csv = StandardCsvHeader + "\n" +
                  "AC-1,Jane Doe,System Owner,2025-06-01,Compliant,Evidence text,,2026-06-01";
        var csvPath = WriteCsv(tmp, csv);

        var result = AttestationImporter.ImportAttestations(tmp.Path, csvPath);

        result.Updated.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.NotFound.Should().Be(0);
    }

    [Fact]
    public void ImportAttestations_ReflectsUpdatedFields_InAttestatationsJson()
    {
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("AC-3")]);

        var csv = StandardCsvHeader + "\n" +
                  "AC-3,Alice,ISSO,2025-07-15,Compliant,Audit log review,,2026-07-15";
        var csvPath = WriteCsv(tmp, csv);

        AttestationImporter.ImportAttestations(tmp.Path, csvPath);

        var updatedJson = File.ReadAllText(Path.Combine(tmp.Path, "05_Attestations", "attestations.json"));
        updatedJson.Should().Contain("Alice");
        updatedJson.Should().Contain("ISSO");
        updatedJson.Should().Contain("Audit log review");
    }

    // ── skipping logic ────────────────────────────────────────────────────────

    [Fact]
    public void ImportAttestations_SkipsRow_WhenComplianceStatusIsEmpty()
    {
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("AC-1")]);

        var csv = StandardCsvHeader + "\n" +
                  "AC-1,Jane,,,,,,";
        var csvPath = WriteCsv(tmp, csv);

        var result = AttestationImporter.ImportAttestations(tmp.Path, csvPath);

        result.Skipped.Should().Be(1);
        result.Updated.Should().Be(0);
    }

    [Fact]
    public void ImportAttestations_SkipsRow_WhenComplianceStatusIsPending()
    {
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("AC-1")]);

        var csv = StandardCsvHeader + "\n" +
                  "AC-1,Jane,Role,,Pending,,,";
        var csvPath = WriteCsv(tmp, csv);

        var result = AttestationImporter.ImportAttestations(tmp.Path, csvPath);

        result.Skipped.Should().Be(1);
    }

    [Fact]
    public void ImportAttestations_SilentlyIgnoresRow_WhenControlIdIsBlank()
    {
        // ParseAttestationCsv filters out blank-ControlId rows before import,
        // so they are not counted as skipped  -  they produce zero side-effects.
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("AC-1")]);

        var csv = StandardCsvHeader + "\n" +
                  ",Jane,Role,,Compliant,,,";
        var csvPath = WriteCsv(tmp, csv);

        var result = AttestationImporter.ImportAttestations(tmp.Path, csvPath);

        result.Updated.Should().Be(0);
        result.NotFound.Should().Be(0);
        // Row is silently dropped at parse stage, not counted in any bucket
        (result.Skipped + result.NotFound + result.Updated).Should().Be(0);
    }

    // ── not-found reporting ───────────────────────────────────────────────────

    [Fact]
    public void ImportAttestations_TracksNotFoundControl_WhenNoMatchInPackage()
    {
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("AU-9")]);

        var csv = StandardCsvHeader + "\n" +
                  "CM-999,Alice,ISSO,,Compliant,,,";
        var csvPath = WriteCsv(tmp, csv);

        var result = AttestationImporter.ImportAttestations(tmp.Path, csvPath);

        result.NotFound.Should().Be(1);
        result.NotFoundControls.Should().Contain("CM-999");
    }

    // ── async path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportAttestationsAsync_ReturnsResult_WhenSuccessful()
    {
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("SI-2")]);

        var csv = StandardCsvHeader + "\n" +
                  "SI-2,Bob,Engineer,2025-05-01,Compliant,Scanned,None,2026-05-01";
        var csvPath = WriteCsv(tmp, csv);

        var result = await AttestationImporter.ImportAttestationsAsync(tmp.Path, csvPath);

        result.Updated.Should().Be(1);
    }

    [Fact]
    public async Task ImportAttestationsAsync_ThrowsOperationCanceledException_WhenAlreadyCancelled()
    {
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("SI-2")]);
        var csvPath = WriteCsv(tmp, StandardCsvHeader + "\nSI-2,Bob,Engineer,,Compliant,,,");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => AttestationImporter.ImportAttestationsAsync(tmp.Path, csvPath, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ImportAttestationsAsync_CallsAudit_WhenAuditServiceProvided()
    {
        using var tmp = new TempDirectory();
        WriteAttestationJson(tmp, [MakeRecord("AC-17")]);

        var csv = StandardCsvHeader + "\n" +
                  "AC-17,Carol,ISSO,,Compliant,Evidence,,";
        var csvPath = WriteCsv(tmp, csv);

        var audit = new Mock<IAuditTrailService>();
        audit.Setup(a => a.RecordAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await AttestationImporter.ImportAttestationsAsync(tmp.Path, csvPath, audit.Object);

        // Audit is fire-and-forget; give it a brief window to complete
        await Task.Delay(100);
        audit.Verify(a => a.RecordAsync(
            It.Is<AuditEntry>(e => e.Action == "import-attestations"),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ── CSV parsing ───────────────────────────────────────────────────────────

    [Fact]
    public void ParseAttestationCsv_ReturnsEmpty_WhenOnlyHeaderRow()
    {
        using var tmp = new TempDirectory();
        var csvPath = WriteCsv(tmp, StandardCsvHeader);

        var rows = AttestationImporter.ParseAttestationCsv(csvPath);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void ParseAttestationCsv_ReturnsEmpty_WhenNoControlIdColumn()
    {
        using var tmp = new TempDirectory();
        var csvPath = WriteCsv(tmp, "Name,Role\nAlice,ISSO");

        var rows = AttestationImporter.ParseAttestationCsv(csvPath);

        rows.Should().BeEmpty();
    }

    [Fact]
    public void ParseAttestationCsv_ParsesMultipleRows_Correctly()
    {
        using var tmp = new TempDirectory();
        var content = StandardCsvHeader + "\n" +
                      "AC-1,Alice,ISSO,2025-01-01,Compliant,Evidence,None,2026-01-01\n" +
                      "AC-2,Bob,Manager,2025-02-01,NonCompliant,Evidence2,Limitation,2026-02-01";
        var csvPath = WriteCsv(tmp, content);

        var rows = AttestationImporter.ParseAttestationCsv(csvPath);

        rows.Should().HaveCount(2);
        rows[0].ControlId.Should().Be("AC-1");
        rows[0].AttestorName.Should().Be("Alice");
        rows[0].ComplianceStatus.Should().Be("Compliant");
        rows[1].ControlId.Should().Be("AC-2");
        rows[1].ComplianceStatus.Should().Be("NonCompliant");
    }

    [Fact]
    public void ParseAttestationCsv_HandlesQuotedFields_WithCommasInside()
    {
        using var tmp = new TempDirectory();
        var content = StandardCsvHeader + "\n" +
                      "\"AC-5\",\"Doe, Jane\",ISSO,,Compliant,\"See artifact, note 1\",,";
        var csvPath = WriteCsv(tmp, content);

        var rows = AttestationImporter.ParseAttestationCsv(csvPath);

        rows.Should().HaveCount(1);
        rows[0].AttestorName.Should().Be("Doe, Jane");
        rows[0].ComplianceEvidence.Should().Be("See artifact, note 1");
    }

    [Fact]
    public void ParseAttestationCsv_SkipsBlankLines()
    {
        using var tmp = new TempDirectory();
        var content = StandardCsvHeader + "\nAC-1,Alice,ISSO,,Compliant,,,\n\nAC-2,Bob,Engineer,,Compliant,,,";
        var csvPath = WriteCsv(tmp, content);

        var rows = AttestationImporter.ParseAttestationCsv(csvPath);

        rows.Should().HaveCount(2);
    }
}
