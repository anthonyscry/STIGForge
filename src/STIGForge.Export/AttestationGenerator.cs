using System.Text;
using System.Text.Json;

namespace STIGForge.Export;

/// <summary>
/// Generates attestation templates for manual control verification.
/// Creates structured forms for system owners to attest compliance for controls
/// that cannot be automatically verified.
/// </summary>
public static class AttestationGenerator
{
  /// <summary>
  /// Generate attestation templates for controls requiring manual verification.
  /// </summary>
  public static AttestationPackage GenerateAttestations(
    IReadOnlyList<string> controlIds,
    string systemName,
    string bundleId)
  {
    var attestations = new List<AttestationRecord>(controlIds.Count);

    foreach (var controlId in controlIds)
    {
      var record = new AttestationRecord
      {
        ControlId = controlId,
        SystemName = systemName,
        BundleId = bundleId,
        AttestationDate = null, // To be filled by system owner
        AttestorName = string.Empty,
        AttestorRole = string.Empty,
        ComplianceStatus = "Pending",
        ComplianceEvidence = string.Empty,
        Limitations = string.Empty,
        NextReviewDate = null
      };

      attestations.Add(record);
    }

    return new AttestationPackage
    {
      Attestations = attestations,
      GeneratedAt = DateTimeOffset.Now,
      SystemName = systemName
    };
  }

  /// <summary>
  /// Write attestation templates to JSON and human-readable form.
  /// </summary>
  public static void WriteAttestationFiles(AttestationPackage package, string outputDir)
  {
    Directory.CreateDirectory(outputDir);

    // JSON template (structured data)
    var jsonPath = Path.Combine(outputDir, "attestations.json");
    var json = JsonSerializer.Serialize(package, new JsonSerializerOptions
    {
      WriteIndented = true,
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
    File.WriteAllText(jsonPath, json, Encoding.UTF8);

    // CSV template (fillable spreadsheet)
    var csvPath = Path.Combine(outputDir, "attestation_template.csv");
    WriteAttestationCsv(package.Attestations, csvPath);

    // Instructions
    var instructionsPath = Path.Combine(outputDir, "INSTRUCTIONS.txt");
    WriteInstructions(instructionsPath, package.SystemName);
  }

  private static void WriteAttestationCsv(IReadOnlyList<AttestationRecord> attestations, string outputPath)
  {
    var sb = new StringBuilder(attestations.Count * 150 + 512);

    sb.AppendLine(string.Join(",",
      "Control ID",
      "Attestor Name",
      "Attestor Role",
      "Attestation Date (YYYY-MM-DD)",
      "Compliance Status (Compliant/NonCompliant/PartiallyCompliant)",
      "Compliance Evidence",
      "Known Limitations",
      "Next Review Date (YYYY-MM-DD)"));

    foreach (var att in attestations)
    {
      sb.AppendLine(string.Join(",",
        Csv(att.ControlId),
        Csv(string.Empty), // To be filled
        Csv(string.Empty), // To be filled
        Csv(string.Empty), // To be filled
        Csv("Pending"),
        Csv(string.Empty), // To be filled
        Csv(string.Empty), // To be filled
        Csv(string.Empty))); // To be filled
    }

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }

  private static void WriteInstructions(string outputPath, string systemName)
  {
    var sb = new StringBuilder(1024);
    sb.AppendLine("ATTESTATION INSTRUCTIONS");
    sb.AppendLine("========================");
    sb.AppendLine();
    sb.AppendLine($"System: {systemName}");
    sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd}");
    sb.AppendLine();
    sb.AppendLine("PURPOSE:");
    sb.AppendLine("This package contains controls that require manual attestation.");
    sb.AppendLine("System owners must review each control and provide attestation of compliance.");
    sb.AppendLine();
    sb.AppendLine("INSTRUCTIONS:");
    sb.AppendLine("1. Open 'attestation_template.csv' in Excel or text editor");
    sb.AppendLine("2. For each control:");
    sb.AppendLine("   a. Enter your name and role as attestor");
    sb.AppendLine("   b. Enter current date (YYYY-MM-DD format)");
    sb.AppendLine("   c. Set compliance status:");
    sb.AppendLine("      - Compliant: Control fully implemented");
    sb.AppendLine("      - NonCompliant: Control not implemented");
    sb.AppendLine("      - PartiallyCompliant: Control partially implemented");
    sb.AppendLine("   d. Provide evidence of compliance (file references, procedures, etc.)");
    sb.AppendLine("   e. Document any known limitations or exceptions");
    sb.AppendLine("   f. Set next review date (typically 90-180 days)");
    sb.AppendLine("3. Save completed file");
    sb.AppendLine("4. Include with eMASS submission package");
    sb.AppendLine();
    sb.AppendLine("COMPLIANCE STATUS VALUES:");
    sb.AppendLine("- Compliant: Control is fully implemented and operational");
    sb.AppendLine("- NonCompliant: Control is not implemented or not operational");
    sb.AppendLine("- PartiallyCompliant: Control is partially implemented or has limitations");
    sb.AppendLine();
    sb.AppendLine("EVIDENCE EXAMPLES:");
    sb.AppendLine("- Policy document references (e.g., 'Security Policy v2.1, Section 5.3')");
    sb.AppendLine("- Procedure references (e.g., 'Password Change Procedure, updated 2024-01-15')");
    sb.AppendLine("- Configuration screenshots (e.g., 'firewall_config_2024.png')");
    sb.AppendLine("- Audit logs (e.g., 'access_review_Q1_2024.xlsx')");
    sb.AppendLine("- Training records (e.g., 'security_training_roster_2024.pdf')");
    sb.AppendLine();
    sb.AppendLine("CONTACT:");
    sb.AppendLine("For questions about attestation requirements, contact your ISSO/ISSM.");

    File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
  }

  private static string Csv(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
      v = "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }
}

/// <summary>
/// Package containing attestation templates and metadata.
/// </summary>
public sealed class AttestationPackage
{
  public IReadOnlyList<AttestationRecord> Attestations { get; set; } = Array.Empty<AttestationRecord>();
  public DateTimeOffset GeneratedAt { get; set; }
  public string SystemName { get; set; } = string.Empty;
}

/// <summary>
/// Individual attestation record for a control.
/// </summary>
public sealed class AttestationRecord
{
  public string ControlId { get; set; } = string.Empty;
  public string SystemName { get; set; } = string.Empty;
  public string BundleId { get; set; } = string.Empty;
  public DateTimeOffset? AttestationDate { get; set; }
  public string AttestorName { get; set; } = string.Empty;
  public string AttestorRole { get; set; } = string.Empty;
  public string ComplianceStatus { get; set; } = string.Empty;
  public string ComplianceEvidence { get; set; } = string.Empty;
  public string Limitations { get; set; } = string.Empty;
  public DateTimeOffset? NextReviewDate { get; set; }
}
