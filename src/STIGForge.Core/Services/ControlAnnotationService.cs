using System.IO;
using System.Text.Json;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public class ControlAnnotationService
{
  private static readonly JsonSerializerOptions JsonOpts = new()
  {
    WriteIndented = true,
    PropertyNameCaseInsensitive = true
  };

  public string? GetNotes(string bundleRoot, string ruleId)
  {
    var file = LoadFile(bundleRoot);
    return file.Annotations.FirstOrDefault(a =>
      string.Equals(a.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))?.Notes;
  }

  public void SaveNotes(string bundleRoot, string ruleId, string? vulnId, string notes)
  {
    var file = LoadFile(bundleRoot);
    var existing = file.Annotations.FirstOrDefault(a =>
      string.Equals(a.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));

    if (existing != null)
    {
      existing.Notes = notes;
      existing.UpdatedAt = DateTimeOffset.UtcNow;
    }
    else
    {
      file.Annotations.Add(new ControlAnnotation
      {
        RuleId = ruleId,
        VulnId = vulnId,
        Notes = notes,
        UpdatedAt = DateTimeOffset.UtcNow
      });
    }

    SaveFile(bundleRoot, file);
  }

  private AnnotationFile LoadFile(string bundleRoot)
  {
    var path = GetFilePath(bundleRoot);
    if (!File.Exists(path))
      return new AnnotationFile();

    try
    {
      var json = File.ReadAllText(path);
      return JsonSerializer.Deserialize<AnnotationFile>(json, JsonOpts) ?? new AnnotationFile();
    }
    catch
    {
      return new AnnotationFile();
    }
  }

  private void SaveFile(string bundleRoot, AnnotationFile file)
  {
    var path = GetFilePath(bundleRoot);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var json = JsonSerializer.Serialize(file, JsonOpts);
    File.WriteAllText(path, json);
  }

  private static string GetFilePath(string bundleRoot) =>
    Path.Combine(bundleRoot, ".stigforge", "annotations.json");
}
