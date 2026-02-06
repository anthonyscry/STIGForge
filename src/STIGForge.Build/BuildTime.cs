namespace STIGForge.Build;

internal static class BuildTime
{
  private const string FixedTimestampEnv = "STIGFORGE_FIXED_TIMESTAMP";

  public static DateTimeOffset Now
  {
    get
    {
      var raw = Environment.GetEnvironmentVariable(FixedTimestampEnv);
      if (!string.IsNullOrWhiteSpace(raw) && DateTimeOffset.TryParse(raw, out var parsed))
        return parsed;

      return DateTimeOffset.UtcNow;
    }
  }
}
