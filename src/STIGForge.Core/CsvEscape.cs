namespace STIGForge.Core;

/// <summary>
/// Shared CSV field escaping utility. Replaces duplicate Csv() helpers
/// across BundleBuilder, EmassExporter, CklExporter, PoamGenerator, etc.
/// </summary>
public static class CsvEscape
{
  private static readonly char[] CsvSpecialChars = { ',', '"', '\n', '\r' };

  // Characters that trigger formula execution in spreadsheet applications.
  private static readonly char[] FormulaStartChars = { '=', '+', '-', '@', '\t' };

  /// <summary>
  /// Escape a value for inclusion in a CSV field.
  /// Wraps in double quotes and escapes inner quotes per RFC 4180.
  /// Also prefixes formula-injection characters (=, +, -, @, tab) with a
  /// single quote to prevent macro execution when opened in Excel/LibreOffice.
  /// </summary>
  public static string Escape(string? value)
  {
    var v = value ?? string.Empty;

    // Neutralize spreadsheet formula injection before any further processing.
    if (v.Length > 0 && Array.IndexOf(FormulaStartChars, v[0]) >= 0)
      v = "'" + v;

    if (v.IndexOfAny(CsvSpecialChars) >= 0)
      return "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }
}
