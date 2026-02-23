using System.Text.Json;
using STIGForge.Export;
using Xunit;

namespace STIGForge.UnitTests.Export;

public class AttestationImporterTests
{
  [Fact]
  public void Import_MergesCsvIntoJson()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "att_import_" + Guid.NewGuid().ToString("N"));
    try
    {
      SetupPackageWithAttestations(tempDir, new[]
      {
        ("V-1001", "Pending"),
        ("V-1002", "Pending"),
        ("V-1003", "Pending")
      });

      var csvPath = WriteCsv(tempDir, new[]
      {
        ("V-1001", "Compliant", "John Doe", "ISSO"),
        ("V-1002", "NonCompliant", "Jane Doe", "ISSM")
      });

      var result = AttestationImporter.ImportAttestations(tempDir, csvPath);

      Assert.Equal(2, result.Updated);
      Assert.Equal(0, result.NotFound);

      // Verify JSON was updated
      var updated = LoadAttestations(tempDir);
      Assert.Equal("Compliant", updated.First(a => a.ControlId == "V-1001").ComplianceStatus);
      Assert.Equal("NonCompliant", updated.First(a => a.ControlId == "V-1002").ComplianceStatus);
      Assert.Equal("Pending", updated.First(a => a.ControlId == "V-1003").ComplianceStatus);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void Import_UpdatesAllFields()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "att_import_" + Guid.NewGuid().ToString("N"));
    try
    {
      SetupPackageWithAttestations(tempDir, new[] { ("V-2001", "Pending") });

      var csvLines = new[]
      {
        "Control ID,Attestor Name,Attestor Role,Attestation Date (YYYY-MM-DD),Compliance Status (Compliant/NonCompliant/PartiallyCompliant),Compliance Evidence,Known Limitations,Next Review Date (YYYY-MM-DD)",
        "V-2001,Alice Smith,Security Admin,2026-02-20,Compliant,Policy doc v3.1,None known,2026-08-20"
      };
      var csvPath = Path.Combine(tempDir, "filled.csv");
      File.WriteAllLines(csvPath, csvLines);

      var result = AttestationImporter.ImportAttestations(tempDir, csvPath);
      Assert.Equal(1, result.Updated);

      var updated = LoadAttestations(tempDir);
      var record = updated.First(a => a.ControlId == "V-2001");
      Assert.Equal("Alice Smith", record.AttestorName);
      Assert.Equal("Security Admin", record.AttestorRole);
      Assert.Equal("Compliant", record.ComplianceStatus);
      Assert.Equal("Policy doc v3.1", record.ComplianceEvidence);
      Assert.Equal("None known", record.Limitations);
      Assert.NotNull(record.AttestationDate);
      Assert.NotNull(record.NextReviewDate);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void Import_SkipsEmptyRows()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "att_import_" + Guid.NewGuid().ToString("N"));
    try
    {
      SetupPackageWithAttestations(tempDir, new[] { ("V-3001", "Pending") });

      var csvLines = new[]
      {
        "Control ID,Attestor Name,Attestor Role,Attestation Date (YYYY-MM-DD),Compliance Status (Compliant/NonCompliant/PartiallyCompliant),Compliance Evidence,Known Limitations,Next Review Date (YYYY-MM-DD)",
        ",,,,,,,",
        "  ,,,,,,,",
        "V-3001,Bob,Admin,2026-02-20,Compliant,Evidence,None,2026-08-20"
      };
      var csvPath = Path.Combine(tempDir, "filled.csv");
      File.WriteAllLines(csvPath, csvLines);

      var result = AttestationImporter.ImportAttestations(tempDir, csvPath);
      Assert.Equal(1, result.Updated);
      Assert.Equal(0, result.NotFound);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void Import_ReportsNotFoundControls()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "att_import_" + Guid.NewGuid().ToString("N"));
    try
    {
      SetupPackageWithAttestations(tempDir, new[] { ("V-4001", "Pending") });

      var csvPath = WriteCsv(tempDir, new[]
      {
        ("V-4001", "Compliant", "John", "ISSO"),
        ("V-9999", "NonCompliant", "Jane", "ISSM")
      });

      var result = AttestationImporter.ImportAttestations(tempDir, csvPath);
      Assert.Equal(1, result.Updated);
      Assert.Equal(1, result.NotFound);
      Assert.Contains("V-9999", result.NotFoundControls);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void Import_SkipsPendingStatusInCsv()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "att_import_" + Guid.NewGuid().ToString("N"));
    try
    {
      SetupPackageWithAttestations(tempDir, new[] { ("V-5001", "Pending") });

      var csvPath = WriteCsv(tempDir, new[]
      {
        ("V-5001", "Pending", "John", "ISSO")
      });

      var result = AttestationImporter.ImportAttestations(tempDir, csvPath);
      Assert.Equal(0, result.Updated);
      Assert.True(result.Skipped > 0);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  [Fact]
  public void ParseAttestationCsv_ParsesHeaderCorrectly()
  {
    var tempDir = Path.Combine(Path.GetTempPath(), "att_csv_" + Guid.NewGuid().ToString("N"));
    try
    {
      Directory.CreateDirectory(tempDir);
      var csvLines = new[]
      {
        "Control ID,Attestor Name,Attestor Role,Attestation Date (YYYY-MM-DD),Compliance Status (Compliant/NonCompliant/PartiallyCompliant),Compliance Evidence,Known Limitations,Next Review Date (YYYY-MM-DD)",
        "V-100,Alice,Admin,2026-01-01,Compliant,Evidence text,None,2026-07-01"
      };
      var csvPath = Path.Combine(tempDir, "test.csv");
      File.WriteAllLines(csvPath, csvLines);

      var rows = AttestationImporter.ParseAttestationCsv(csvPath);
      Assert.Single(rows);
      Assert.Equal("V-100", rows[0].ControlId);
      Assert.Equal("Alice", rows[0].AttestorName);
      Assert.Equal("Compliant", rows[0].ComplianceStatus);
    }
    finally
    {
      if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
  }

  private static void SetupPackageWithAttestations(string root, (string controlId, string status)[] attestations)
  {
    var attestDir = Path.Combine(root, "05_Attestations");
    Directory.CreateDirectory(attestDir);

    var records = attestations.Select(a => new AttestationRecord
    {
      ControlId = a.controlId,
      ComplianceStatus = a.status,
      SystemName = "TestSystem",
      BundleId = "test-bundle"
    }).ToList();

    var package = new AttestationPackage
    {
      Attestations = records,
      GeneratedAt = DateTimeOffset.Now,
      SystemName = "TestSystem"
    };

    File.WriteAllText(Path.Combine(attestDir, "attestations.json"),
      JsonSerializer.Serialize(package, new JsonSerializerOptions
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
      }));
  }

  private static string WriteCsv(string root, (string controlId, string status, string name, string role)[] rows)
  {
    var lines = new List<string>
    {
      "Control ID,Attestor Name,Attestor Role,Attestation Date (YYYY-MM-DD),Compliance Status (Compliant/NonCompliant/PartiallyCompliant),Compliance Evidence,Known Limitations,Next Review Date (YYYY-MM-DD)"
    };
    foreach (var r in rows)
      lines.Add($"{r.controlId},{r.name},{r.role},2026-02-20,{r.status},Evidence for {r.controlId},None,2026-08-20");

    var csvPath = Path.Combine(root, "filled_attestations.csv");
    File.WriteAllLines(csvPath, lines);
    return csvPath;
  }

  private static List<AttestationRecord> LoadAttestations(string root)
  {
    var json = File.ReadAllText(Path.Combine(root, "05_Attestations", "attestations.json"));
    var package = JsonSerializer.Deserialize<AttestationPackage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    return package?.Attestations?.ToList() ?? new List<AttestationRecord>();
  }
}
