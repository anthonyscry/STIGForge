namespace STIGForge.Core;

/// <summary>
/// Shared CSV field escaping utility. Replaces duplicate Csv() helpers
/// across BundleBuilder, EmassExporter, CklExporter, PoamGenerator, etc.
/// </summary>
public static class CsvEscape
{
  private static readonly char[] CsvSpecialChars = { ',', '"', '\n', '\r' };

  /// <summary>
  /// Escape a value for inclusion in a CSV field.
  /// Wraps in double quotes and escapes inner quotes per RFC 4180.
  /// </summary>
  public static string Escape(string? value)
  {
    var v = value ?? string.Empty;
    if (v.IndexOfAny(CsvSpecialChars) >= 0)
      return "\"" + v.Replace("\"", "\"\"") + "\"";
    return v;
  }
}
