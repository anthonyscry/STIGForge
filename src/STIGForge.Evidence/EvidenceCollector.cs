using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace STIGForge.Evidence;

public sealed class EvidenceCollector
{
  public EvidenceWriteResult WriteEvidence(EvidenceWriteRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.BundleRoot))
      throw new ArgumentException("BundleRoot is required.");

    var bundleRoot = request.BundleRoot.Trim();
    if (!Directory.Exists(bundleRoot))
      throw new DirectoryNotFoundException("Bundle root not found: " + bundleRoot);

    var controlKey = ResolveControlKey(request);
    var evidenceDir = Path.Combine(bundleRoot, "Evidence", "by_control", controlKey);
    Directory.CreateDirectory(evidenceDir);

    var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
    var typeLabel = request.Type.ToString().ToLowerInvariant();
    var suffix = Guid.NewGuid().ToString("n").Substring(0, 6);
    var baseName = "evidence_" + stamp + "_" + typeLabel + "_" + suffix;

    string evidencePath;
    if (!string.IsNullOrWhiteSpace(request.SourceFilePath))
    {
      var ext = Path.GetExtension(request.SourceFilePath);
      if (string.IsNullOrWhiteSpace(ext))
        ext = ResolveExtension(request.FileExtension, request.Type);

      evidencePath = Path.Combine(evidenceDir, baseName + ext);
      File.Copy(request.SourceFilePath!, evidencePath, true);
    }
    else
    {
      var ext = ResolveExtension(request.FileExtension, request.Type);
      evidencePath = Path.Combine(evidenceDir, baseName + ext);
      File.WriteAllText(evidencePath, request.ContentText ?? string.Empty, Encoding.UTF8);
    }

    var sha = ComputeSha256(evidencePath);
    var metadataPath = Path.Combine(evidenceDir, baseName + ".json");

    var metadata = new EvidenceMetadata
    {
      ControlId = request.ControlId,
      RuleId = request.RuleId,
      Title = request.Title,
      Type = request.Type.ToString(),
      Source = request.Source,
      Command = request.Command,
      OriginalPath = request.SourceFilePath,
      TimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
      Host = Environment.MachineName,
      User = Environment.UserName,
      BundleRoot = bundleRoot,
      Sha256 = sha,
      Tags = request.Tags,
      RunId = request.RunId,
      StepName = request.StepName,
      SupersedesEvidenceId = request.SupersedesEvidenceId
    };

    var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(metadataPath, json, Encoding.UTF8);

    return new EvidenceWriteResult
    {
      EvidenceDir = evidenceDir,
      EvidencePath = evidencePath,
      MetadataPath = metadataPath,
      Sha256 = sha,
      EvidenceId = baseName
    };
  }

  private static string ResolveControlKey(EvidenceWriteRequest request)
  {
    if (!string.IsNullOrWhiteSpace(request.RuleId))
      return Sanitize(request.RuleId!);
    if (!string.IsNullOrWhiteSpace(request.ControlId))
      return Sanitize(request.ControlId!);
    return "UNKNOWN";
  }

  private static string ResolveExtension(string? explicitExt, EvidenceArtifactType type)
  {
    if (!string.IsNullOrWhiteSpace(explicitExt))
    {
      var ext = explicitExt!.StartsWith(".") ? explicitExt : "." + explicitExt;
      return ext.ToLowerInvariant();
    }

    return type == EvidenceArtifactType.Screenshot ? ".png" : ".txt";
  }

  private static string ComputeSha256(string path)
  {
    using var stream = File.OpenRead(path);
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(stream);
    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
  }

  private static string Sanitize(string value)
  {
    var result = value;
    foreach (var c in Path.GetInvalidFileNameChars())
      result = result.Replace(c.ToString(), string.Empty);
    return result.Replace(" ", string.Empty);
  }
}
