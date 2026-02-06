using System.Text;

namespace STIGForge.Apply.PowerStig;

/// <summary>
/// Exception thrown when PowerSTIG data validation fails.
/// </summary>
public sealed class ValidationException : Exception
{
  public ValidationException(string message) : base(message) { }
  public ValidationException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Serializes PowerSTIG data to .psd1 format files.
/// </summary>

/// <summary>
/// Serializes PowerSTIG data to .psd1 format files.
/// </summary>
public static class PowerStigDataWriter
{
  /// <summary>
  /// Writes PowerSTIG data to a .psd1 file.
  /// </summary>
  /// <param name="data">PowerSTIG data structure</param>
  /// <param name="outputPath">Output file path (should have .psd1 extension)</param>
  /// <exception cref="ValidationException">Thrown when data validation fails</exception>
  /// <exception cref="IOException">Thrown when file write fails</exception>
  public static void Write(string outputPath, PowerStigData data)
  {
    // Validate before writing
    var validationResult = PowerStigValidator.Validate(data);
    if (!validationResult.IsValid)
    {
      var errorMessages = string.Join("\n", validationResult.Errors);
      throw new ValidationException($"PowerSTIG data validation failed:\n{errorMessages}");
    }

    // Ensure directory exists
    var directory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
    {
      Directory.CreateDirectory(directory);
    }

    // Serialize to .psd1 format
    var psd1Content = SerializeToPsd1(data);

    // Write to temp file first, then move (atomic write)
    var tempPath = outputPath + ".tmp";
    File.WriteAllText(tempPath, psd1Content, new UTF8Encoding(false));

    // Delete existing file if present, then move (atomic write)
    if (File.Exists(outputPath))
    {
      File.Delete(outputPath);
    }
    File.Move(tempPath, outputPath);
  }

  /// <summary>
  /// Serializes PowerStigData to PowerShell .psd1 hashtable format.
  /// </summary>
  private static string SerializeToPsd1(PowerStigData data)
  {
    var sb = new StringBuilder(4096);

    // Start hashtable
    sb.AppendLine("@{");

    // StigVersion
    if (!string.IsNullOrWhiteSpace(data.StigVersion))
    {
      sb.Append("    StigVersion = \"");
      sb.Append(EscapeString(data.StigVersion));
      sb.AppendLine("\"");
    }

    // StigRelease
    if (!string.IsNullOrWhiteSpace(data.StigRelease))
    {
      sb.Append("    StigRelease = \"");
      sb.Append(EscapeString(data.StigRelease));
      sb.AppendLine("\"");
    }

    // GlobalSettings
    sb.AppendLine("    GlobalSettings = @{");
    foreach (var kv in data.GlobalSettings.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
    {
      sb.Append("        ");
      sb.Append(kv.Key);
      sb.Append(" = \"");
      sb.Append(EscapeString(kv.Value));
      sb.AppendLine("\"");
    }
    sb.AppendLine("    }");

    // RuleSettings
    sb.AppendLine("    RuleSettings = @(");
    for (int i = 0; i < data.RuleSettings.Count; i++)
    {
      var rule = data.RuleSettings[i];
      sb.AppendLine("        @{");
      sb.Append("            RuleId = \"");
      sb.Append(EscapeString(rule.RuleId));
      sb.AppendLine("\"");

      if (!string.IsNullOrWhiteSpace(rule.SettingName))
      {
        sb.Append("            SettingName = \"");
        sb.Append(EscapeString(rule.SettingName!));
        sb.AppendLine("\"");
      }
      else
      {
        sb.AppendLine("            SettingName = $null");
      }

      if (!string.IsNullOrWhiteSpace(rule.Value))
      {
        sb.Append("            Value = \"");
        sb.Append(EscapeString(rule.Value!));
        sb.AppendLine("\"");
      }
      else
      {
        sb.AppendLine("            Value = $null");
      }

      sb.Append("        }");
      if (i < data.RuleSettings.Count - 1)
      {
        sb.AppendLine(",");
      }
      else
      {
        sb.AppendLine();
      }
    }
    sb.AppendLine("    )");

    // End hashtable
    sb.AppendLine("}");

    return sb.ToString();
  }

  /// <summary>
  /// Escapes special characters for PowerShell string literals.
  /// Handles quotes, backslashes, dollar signs, and backticks.
  /// </summary>
  private static string EscapeString(string? value)
  {
    if (string.IsNullOrEmpty(value))
      return string.Empty;

    // Escape backticks first so escape markers are not double-escaped later
    value = value.Replace("`", "``");

    // Escape backslashes
    value = value.Replace("\\", "\\\\");

    // Escape quotes
    value = value.Replace("\"", "`\"");

    return value;
  }
}
