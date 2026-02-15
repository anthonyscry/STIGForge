namespace STIGForge.Core.Services;

public static class PackFormatClassifier
{
  public static bool IsAdmxTemplatePack(string? sourceLabel, string? packName)
  {
    var source = sourceLabel ?? string.Empty;
    var name = packName ?? string.Empty;

    if (source.IndexOf("/LocalPolicy", StringComparison.OrdinalIgnoreCase) >= 0)
      return false;

    if (source.IndexOf("/ADMX", StringComparison.OrdinalIgnoreCase) >= 0)
      return true;

    if (source.IndexOf("admx_template_import", StringComparison.OrdinalIgnoreCase) >= 0)
      return true;

    if (name.IndexOf("ADMX Templates", StringComparison.OrdinalIgnoreCase) >= 0)
      return true;

    if (name.IndexOf("STIG GPO Package", StringComparison.OrdinalIgnoreCase) >= 0)
      return false;

    if (source.IndexOf("gpo_lgpo_import", StringComparison.OrdinalIgnoreCase) >= 0)
      return false;

    return source.IndexOf("admx_import", StringComparison.OrdinalIgnoreCase) >= 0;
  }
}
