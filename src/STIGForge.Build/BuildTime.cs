namespace STIGForge.Build;

internal static class BuildTime
{
  private const string FixedTimestampEnv = "STIGFORGE_FIXED_TIMESTAMP";

  private static DateTimeOffset? _override;

  public static DateTimeOffset Now
  {
    get
    {
      if (_override.HasValue)
        return _override.Value;

      var raw = Environment.GetEnvironmentVariable(FixedTimestampEnv);
      if (!string.IsNullOrWhiteSpace(raw) && DateTimeOffset.TryParse(raw, out var parsed))
        return parsed;

      return DateTimeOffset.UtcNow;
    }
  }

  /// <summary>
  /// Pins BuildTime.Now to a fixed value for deterministic test builds.
  /// </summary>
  public static void Seed(DateTimeOffset value) => _override = value;

  /// <summary>
  /// Resets BuildTime.Now to real clock behavior.
  /// </summary>
  public static void Reset() => _override = null;
}
