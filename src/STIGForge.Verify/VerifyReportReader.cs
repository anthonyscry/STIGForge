using System.Text.Json;

namespace STIGForge.Verify;

public static class VerifyReportReader
{
  public static VerifyReport LoadFromJson(string path)
  {
    var json = File.ReadAllText(path);
    var report = JsonSerializer.Deserialize<VerifyReport>(json, new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });

    if (report == null)
      throw new InvalidOperationException("Invalid verify report JSON: " + path);

    return report;
  }
}
