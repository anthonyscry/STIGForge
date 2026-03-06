using System.Text.Json;
using STIGForge.Core;

namespace STIGForge.Verify;

public static class VerifyReportReader
{
  public static VerifyReport LoadFromJson(string path)
  {
    var json = File.ReadAllText(path);
    var report = JsonSerializer.Deserialize<VerifyReport>(json, JsonOptions.CaseInsensitive);

    if (report == null)
      throw new InvalidOperationException("Invalid verify report JSON: " + path);

    return report;
  }
}
