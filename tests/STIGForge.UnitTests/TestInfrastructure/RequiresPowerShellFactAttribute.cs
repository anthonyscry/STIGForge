using System.Diagnostics;

namespace STIGForge.UnitTests.TestInfrastructure;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresPowerShellFactAttribute : FactAttribute
{
  public RequiresPowerShellFactAttribute()
  {
    if (!CanRunPowerShell())
      Skip = "PowerShell is unavailable in this environment.";
  }

  private static bool CanRunPowerShell()
  {
    try
    {
      using var process = Process.Start(new ProcessStartInfo
      {
        FileName = "powershell.exe",
        Arguments = "-NoProfile -Command \"exit 0\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
      });

      if (process == null)
        return false;

      if (!process.WaitForExit(5000))
      {
        try
        {
          process.Kill(true);
        }
        catch
        {
          // Best effort cleanup.
        }

        return false;
      }

      return process.ExitCode == 0;
    }
    catch
    {
      return false;
    }
  }
}
