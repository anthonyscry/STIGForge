using System.Diagnostics;
using System.Text.RegularExpressions;

namespace STIGForge.Core.Services;

public static partial class WindowsServiceCommandHelper
{
  private const int MaxServiceNameLength = 256;

  [GeneratedRegex("^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)]
  private static partial Regex ServiceNameRegex();

  public static void InstallService(string serviceName, string displayName, string executablePath)
  {
    ValidateServiceName(serviceName);
    if (!OperatingSystem.IsWindows())
      return;

    using var process = Process.Start(CreateScProcessStartInfo(
      $"create {serviceName} binPath= \"{executablePath}\" displayName= \"{displayName}\" start= auto"));
    process?.WaitForExit();
  }

  public static void UninstallService(string serviceName)
  {
    ValidateServiceName(serviceName);
    if (!OperatingSystem.IsWindows())
      return;

    using var process = Process.Start(CreateScProcessStartInfo($"delete {serviceName}"));
    process?.WaitForExit();
  }

  public static string QueryServiceStatus(string serviceName)
  {
    ValidateServiceName(serviceName);
    if (!OperatingSystem.IsWindows())
      return "Unsupported on non-Windows host";

    using var process = Process.Start(CreateScProcessStartInfo($"query {serviceName}"));
    process?.WaitForExit();
    return (process?.StandardOutput.ReadToEnd() ?? string.Empty).Trim();
  }

  public static void ValidateServiceName(string name)
  {
    if (string.IsNullOrWhiteSpace(name))
      throw new ArgumentException("Value cannot be null or empty.", nameof(name));
    if (name.Length > MaxServiceNameLength)
      throw new ArgumentException($"Service name must be {MaxServiceNameLength} characters or fewer.", nameof(name));
    if (!ServiceNameRegex().IsMatch(name))
      throw new ArgumentException("Service name contains invalid characters.", nameof(name));
  }

  private static ProcessStartInfo CreateScProcessStartInfo(string arguments)
  {
    var scPath = Path.Combine(Environment.SystemDirectory, "sc.exe");
    return new ProcessStartInfo
    {
      FileName = scPath,
      Arguments = arguments,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
  }
}
