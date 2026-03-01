using System.Xml;
using STIGForge.Core.Models;

namespace STIGForge.Content.Import;

public static class GpoParser
{
  /// <summary>
  /// Parses a full GPO package directory, extracting control records from all
  /// artifact types: ADMX policies, Registry.pol binary files, and GptTmpl.inf
  /// security templates. Detects OS applicability from the folder structure.
  /// </summary>
  public static GpoFullParseResult ParsePackage(string extractedRoot, string packName)
  {
    var allControls = new List<ControlRecord>();
    var warnings = new List<string>();

    // Detect available OS scopes
    var osScopes = GpoPackageExtractor.DetectOsScopes(extractedRoot);

    // Parse ADMX files
    var admxFiles = Directory.GetFiles(extractedRoot, "*.admx", SearchOption.AllDirectories);
    foreach (var admxFile in admxFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
    {
      try
      {
        var osTarget = InferOsTargetFromPath(admxFile, osScopes);
        allControls.AddRange(ParseAdmx(admxFile, packName, osTarget));
      }
      catch (Exception ex)
      {
        warnings.Add($"ADMX parse failed ({Path.GetFileName(admxFile)}): {ex.Message}");
      }
    }

    // Parse Registry.pol files
    var polFiles = Directory.GetFiles(extractedRoot, "*.pol", SearchOption.AllDirectories);
    foreach (var polFile in polFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
    {
      try
      {
        var osTarget = InferOsTargetFromPath(polFile, osScopes);
        var polResult = PolFileParser.Parse(polFile, packName, osTarget);
        allControls.AddRange(polResult.Controls);
        warnings.AddRange(polResult.Warnings);
      }
      catch (Exception ex)
      {
        warnings.Add($"POL parse failed ({Path.GetFileName(polFile)}): {ex.Message}");
      }
    }

    // Parse GptTmpl.inf security templates
    var infFiles = Directory.GetFiles(extractedRoot, "GptTmpl.inf", SearchOption.AllDirectories);
    foreach (var infFile in infFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
    {
      try
      {
        var osTarget = InferOsTargetFromPath(infFile, osScopes);
        var infResult = GptTmplParser.Parse(infFile, packName, osTarget);
        allControls.AddRange(infResult.Controls);
      }
      catch (Exception ex)
      {
        warnings.Add($"GptTmpl parse failed ({Path.GetFileName(infFile)}): {ex.Message}");
      }
    }

    return new GpoFullParseResult
    {
      Controls = allControls,
      OsScopes = osScopes,
      Warnings = warnings,
      AdmxFileCount = admxFiles.Length,
      PolFileCount = polFiles.Length,
      InfFileCount = infFiles.Length
    };
  }

  /// <summary>
  /// Parses a single ADMX file into control records (original API preserved for backward compatibility).
  /// </summary>
  public static IReadOnlyList<ControlRecord> Parse(string admxPath, string packName)
  {
    return ParseAdmx(admxPath, packName, OsTarget.Unknown);
  }

  internal static IReadOnlyList<ControlRecord> ParseAdmx(string admxPath, string packName, OsTarget osTarget)
  {
    if (!File.Exists(admxPath))
      throw new FileNotFoundException("ADMX file not found", admxPath);

    var settings = new XmlReaderSettings
    {
      DtdProcessing = DtdProcessing.Prohibit,
      XmlResolver = null,
      IgnoreWhitespace = true,
      Async = false
    };

    using var reader = XmlReader.Create(admxPath, settings);

    var records = new List<ControlRecord>();
    string? currentNamespace = null;

    while (reader.Read())
    {
      if (reader.NodeType != XmlNodeType.Element)
        continue;

      switch (reader.LocalName)
      {
        case "target":
          currentNamespace = reader.GetAttribute("namespace")?.Trim();
          break;
        case "policy":
          var record = ParsePolicy(reader, currentNamespace, packName, osTarget);
          if (record != null)
            records.Add(record);
          break;
      }
    }

    return records;
  }

  private static ControlRecord? ParsePolicy(XmlReader reader, string? policyNamespace, string packName, OsTarget osTarget)
  {
    var policyName = reader.GetAttribute("name")?.Trim();
    if (string.IsNullOrWhiteSpace(policyName))
      return null;

    var displayName = reader.GetAttribute("displayName")?.Trim();
    var key = reader.GetAttribute("key")?.Trim();
    var valueName = reader.GetAttribute("valueName")?.Trim();
    var policyClass = reader.GetAttribute("class")?.Trim();

    var title = CleanDisplayName(displayName) ?? policyName;
    var keyText = string.IsNullOrWhiteSpace(key) ? "unknown" : key;
    var valueText = string.IsNullOrWhiteSpace(valueName) ? "Enabled" : valueName;

    return new ControlRecord
    {
      ControlId = Guid.NewGuid().ToString("n"),
      ExternalIds = new ExternalIds
      {
        RuleId = policyName,
        BenchmarkId = policyNamespace
      },
      Title = title!,
      Severity = "medium",
      Discussion = $"Registry Key: {keyText}" +
        (string.IsNullOrWhiteSpace(policyClass) ? string.Empty : $" (Class: {policyClass})"),
      CheckText = $"Verify registry value '{valueText}' under '{keyText}'",
      FixText = $"Configure Group Policy '{policyName}'",
      IsManual = false,
      Applicability = new Applicability
      {
        OsTarget = osTarget,
        RoleTags = Array.Empty<RoleTemplate>(),
        ClassificationScope = ScopeTag.Unknown,
        Confidence = Confidence.Medium
      },
      Revision = new RevisionInfo
      {
        PackName = packName
      }
    };
  }

  private static OsTarget InferOsTargetFromPath(string filePath, IReadOnlyList<GpoOsScope> osScopes)
  {
    var normalized = filePath.Replace('\\', '/');
    foreach (var scope in osScopes)
    {
      var scopeNorm = scope.ScopePath.Replace('\\', '/');
      if (normalized.StartsWith(scopeNorm, StringComparison.OrdinalIgnoreCase))
        return scope.OsTarget;
    }

    // Fallback: try to detect from path segments
    return GpoPackageExtractor.MapFolderToOsTarget(
      ExtractOsFolderSegment(normalized));
  }

  private static string ExtractOsFolderSegment(string path)
  {
    var segments = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
    foreach (var segment in segments)
    {
      var target = GpoPackageExtractor.MapFolderToOsTarget(segment);
      if (target != OsTarget.Unknown)
        return segment;
    }
    return string.Empty;
  }

  private static string? CleanDisplayName(string? displayName)
  {
    if (string.IsNullOrWhiteSpace(displayName))
      return null;

    var value = (displayName ?? string.Empty).Trim();
    if (value.StartsWith("$(string.", StringComparison.OrdinalIgnoreCase) && value.EndsWith(")", StringComparison.Ordinal))
      return value;

    return value;
  }
}

public sealed class GpoFullParseResult
{
  public IReadOnlyList<ControlRecord> Controls { get; set; } = Array.Empty<ControlRecord>();
  public IReadOnlyList<GpoOsScope> OsScopes { get; set; } = Array.Empty<GpoOsScope>();
  public List<string> Warnings { get; set; } = new();
  public int AdmxFileCount { get; set; }
  public int PolFileCount { get; set; }
  public int InfFileCount { get; set; }
}
