using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;

namespace STIGForge.Export;

/// <summary>
/// Imports filled attestation CSV files back into attestation JSON packages.
/// Used after system owners complete the attestation_template.csv in Excel.
/// </summary>
public static class AttestationImporter
{
  /// <summary>
  /// Merge filled attestation CSV into existing attestation JSON in the eMASS package.
  /// </summary>
  public static AttestationImportResult ImportAttestations(string packageRoot, string csvFilePath, IAuditTrailService? audit = null)
  {
    if (!Directory.Exists(packageRoot))
      throw new DirectoryNotFoundException("Package root not found: " + packageRoot);
    if (!File.Exists(csvFilePath))
      throw new FileNotFoundException("Attestation CSV not found", csvFilePath);

    var attestDir = Path.Combine(packageRoot, "05_Attestations");
    var attestPath = Path.Combine(attestDir, "attestations.json");

    if (!File.Exists(attestPath))
      throw new FileNotFoundException("attestations.json not found in package", attestPath);

    // Parse CSV
    var csvRows = ParseAttestationCsv(csvFilePath);

    // Load existing attestations
    var json = File.ReadAllText(attestPath);
    var package = JsonSerializer.Deserialize<AttestationPackage>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (package?.Attestations == null)
      throw new InvalidOperationException("Invalid attestations.json format.");

    // We need a mutable list
    var attestations = package.Attestations.ToList();
    var updated = 0;
    var skipped = 0;
    var notFound = 0;
    var notFoundControls = new List<string>();

    foreach (var row in csvRows)
    {
      if (string.IsNullOrWhiteSpace(row.ControlId))
      {
        skipped++;
        continue;
      }

      if (string.IsNullOrWhiteSpace(row.ComplianceStatus) ||
          string.Equals(row.ComplianceStatus, "Pending", StringComparison.OrdinalIgnoreCase))
      {
        skipped++;
        continue;
      }

      var match = attestations.FirstOrDefault(a =>
        string.Equals(a.ControlId, row.ControlId, StringComparison.OrdinalIgnoreCase));

      if (match == null)
      {
        notFound++;
        notFoundControls.Add(row.ControlId);
        continue;
      }

      // Update fields from CSV
      match.AttestorName = row.AttestorName ?? string.Empty;
      match.AttestorRole = row.AttestorRole ?? string.Empty;
      match.ComplianceStatus = row.ComplianceStatus ?? string.Empty;
      match.ComplianceEvidence = row.ComplianceEvidence ?? string.Empty;
      match.Limitations = row.Limitations ?? string.Empty;

      if (DateTimeOffset.TryParse(row.AttestationDate, out var attDate))
        match.AttestationDate = attDate;

      if (DateTimeOffset.TryParse(row.NextReviewDate, out var reviewDate))
        match.NextReviewDate = reviewDate;

      updated++;
    }

    // Write updated attestations back
    var updatedPackage = new AttestationPackage
    {
      Attestations = attestations,
      GeneratedAt = package.GeneratedAt,
      SystemName = package.SystemName
    };

    var updatedJson = JsonSerializer.Serialize(updatedPackage, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    File.WriteAllText(attestPath, updatedJson, Encoding.UTF8);

    // Record audit entry
    if (audit != null)
    {
      try
      {
        audit.RecordAsync(new AuditEntry
        {
          Action = "import-attestations",
          Target = packageRoot,
          Result = "success",
          Detail = $"Updated={updated}, Skipped={skipped}, NotFound={notFound}",
          User = Environment.UserName,
          Machine = Environment.MachineName,
          Timestamp = DateTimeOffset.Now
        }, CancellationToken.None).GetAwaiter().GetResult();
      }
      catch { /* Audit failure should not block import */ }
    }

    return new AttestationImportResult
    {
      Updated = updated,
      Skipped = skipped,
      NotFound = notFound,
      NotFoundControls = notFoundControls
    };
  }

  /// <summary>
  /// Parse attestation CSV file into structured rows.
  /// </summary>
  public static List<AttestationCsvRow> ParseAttestationCsv(string csvPath)
  {
    var rows = new List<AttestationCsvRow>();
    var lines = File.ReadAllLines(csvPath);
    if (lines.Length < 2) return rows;

    // Parse header to find column indices
    var headers = ParseCsvLine(lines[0]);
    var controlIdIdx = FindColumnIndex(headers, "Control ID");
    var attestorNameIdx = FindColumnIndex(headers, "Attestor Name");
    var attestorRoleIdx = FindColumnIndex(headers, "Attestor Role");
    var attestDateIdx = FindColumnIndex(headers, "Attestation Date");
    var statusIdx = FindColumnIndex(headers, "Compliance Status");
    var evidenceIdx = FindColumnIndex(headers, "Compliance Evidence");
    var limitationsIdx = FindColumnIndex(headers, "Known Limitations");
    var reviewDateIdx = FindColumnIndex(headers, "Next Review Date");

    if (controlIdIdx < 0) return rows; // Can't map without ControlId column

    for (var i = 1; i < lines.Length; i++)
    {
      var line = lines[i];
      if (string.IsNullOrWhiteSpace(line)) continue;

      var parts = ParseCsvLine(line);
      var controlId = GetField(parts, controlIdIdx);
      if (string.IsNullOrWhiteSpace(controlId)) continue;

      rows.Add(new AttestationCsvRow
      {
        ControlId = controlId,
        AttestorName = GetField(parts, attestorNameIdx),
        AttestorRole = GetField(parts, attestorRoleIdx),
        AttestationDate = GetField(parts, attestDateIdx),
        ComplianceStatus = GetField(parts, statusIdx),
        ComplianceEvidence = GetField(parts, evidenceIdx),
        Limitations = GetField(parts, limitationsIdx),
        NextReviewDate = GetField(parts, reviewDateIdx)
      });
    }

    return rows;
  }

  private static int FindColumnIndex(string[] headers, string namePrefix)
  {
    for (var i = 0; i < headers.Length; i++)
    {
      if (headers[i].Trim().StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
        return i;
    }
    return -1;
  }

  private static string? GetField(string[] parts, int index)
  {
    if (index < 0 || index >= parts.Length) return null;
    var value = parts[index].Trim();
    return string.IsNullOrWhiteSpace(value) ? null : value;
  }

  private static string[] ParseCsvLine(string line)
  {
    var list = new List<string>();
    var sb = new StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
      var ch = line[i];
      if (ch == '"')
      {
        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
        {
          sb.Append('"');
          i++;
        }
        else
        {
          inQuotes = !inQuotes;
        }
      }
      else if (ch == ',' && !inQuotes)
      {
        list.Add(sb.ToString());
        sb.Clear();
      }
      else
      {
        sb.Append(ch);
      }
    }

    list.Add(sb.ToString());
    return list.ToArray();
  }
}

/// <summary>
/// Result of importing attestation CSV into JSON.
/// </summary>
public sealed class AttestationImportResult
{
  public int Updated { get; set; }
  public int Skipped { get; set; }
  public int NotFound { get; set; }
  public List<string> NotFoundControls { get; set; } = new();
}

/// <summary>
/// Parsed row from attestation CSV file.
/// </summary>
public sealed class AttestationCsvRow
{
  public string ControlId { get; set; } = string.Empty;
  public string? AttestorName { get; set; }
  public string? AttestorRole { get; set; }
  public string? AttestationDate { get; set; }
  public string? ComplianceStatus { get; set; }
  public string? ComplianceEvidence { get; set; }
  public string? Limitations { get; set; }
  public string? NextReviewDate { get; set; }
}
