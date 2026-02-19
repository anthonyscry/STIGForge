namespace STIGForge.UnitTests.TestInfrastructure;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresShellFactAttribute : FactAttribute
{
  public RequiresShellFactAttribute()
  {
    if (!HasSupportedShell())
      Skip = "No supported shell is available in this environment.";
  }

  private static bool HasSupportedShell()
  {
    if (OperatingSystem.IsWindows())
    {
      var commandPath = Environment.GetEnvironmentVariable("ComSpec") ?? string.Empty;
      return !string.IsNullOrWhiteSpace(commandPath) && File.Exists(commandPath);
    }

    return File.Exists("/bin/sh");
  }
}
