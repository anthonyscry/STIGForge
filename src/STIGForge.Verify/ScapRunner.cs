using System.Diagnostics;

namespace STIGForge.Verify;

public sealed class ScapRunner
{
  public VerifyRunResult Run(string commandPath, string arguments, string? workingDirectory)
  {
    var resolvedCommandPath = ResolveCommandPath(commandPath);
    var resolvedWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
      ? Path.GetDirectoryName(resolvedCommandPath) ?? Environment.CurrentDirectory
      : workingDirectory;

    var psi = new ProcessStartInfo
    {
      FileName = resolvedCommandPath,
      Arguments = arguments ?? string.Empty,
      WorkingDirectory = resolvedWorkingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };

    var started = DateTimeOffset.Now;
    using var process = Process.Start(psi);
    if (process == null)
      throw new InvalidOperationException("Failed to start SCAP command.");

    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    if (!process.WaitForExit(30000))
    {
      process.Kill();
      throw new TimeoutException("Process did not exit within 30 seconds.");
    }

    return new VerifyRunResult
    {
      ExitCode = process.ExitCode,
      Output = output,
      Error = error,
      StartedAt = started,
      FinishedAt = DateTimeOffset.Now
    };
  }

  private static string ResolveCommandPath(string commandPath)
  {
    if (string.IsNullOrWhiteSpace(commandPath))
      throw new ArgumentException("Command path is required.", nameof(commandPath));

    var trimmedPath = commandPath.Trim();

    if (IsSimpleCommandName(trimmedPath))
    {
      if (IsUnsupportedSccGuiBinary(Path.GetFileName(trimmedPath)))
        throw new InvalidOperationException("SCC GUI binary is not supported for automation. Use cscc.exe or cscc-remote.exe.");

      if (IsSupportedCliBinary(Path.GetFileName(trimmedPath)))
        return trimmedPath;
    }

    if (File.Exists(trimmedPath))
    {
      var fileName = Path.GetFileName(trimmedPath);
      if (IsUnsupportedSccGuiBinary(fileName))
        throw new InvalidOperationException("SCC GUI binary is not supported for automation. Use cscc.exe or cscc-remote.exe.");

      if (IsSupportedCliBinary(fileName))
        return Path.GetFullPath(trimmedPath);

      return Path.GetFullPath(trimmedPath);
    }

    if (Directory.Exists(trimmedPath))
    {
      foreach (var name in SupportedCliBinaryNames)
      {
        var candidate = Path.Combine(trimmedPath, name);
        if (File.Exists(candidate))
          return candidate;
      }

      try
      {
        var match = Directory.EnumerateFiles(trimmedPath, "*", SearchOption.AllDirectories)
          .Where(path => IsSupportedCliBinary(Path.GetFileName(path)))
          .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
          .ThenBy(path => path.Length)
          .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(match))
          return match;

        var guiOnlyMatch = Directory.EnumerateFiles(trimmedPath, "*", SearchOption.AllDirectories)
          .Where(path => IsUnsupportedSccGuiBinary(Path.GetFileName(path)))
          .OrderBy(path => path.Count(ch => ch == Path.DirectorySeparatorChar || ch == Path.AltDirectorySeparatorChar))
          .ThenBy(path => path.Length)
          .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(guiOnlyMatch))
          throw new InvalidOperationException("Found scc.exe GUI binary, but automation requires cscc.exe or cscc-remote.exe.");
      }
      catch (UnauthorizedAccessException)
      {
      }
      catch (IOException)
      {
      }
    }

    if (!Path.HasExtension(trimmedPath))
    {
      var withExe = trimmedPath + ".exe";
      if (File.Exists(withExe))
      {
        var fileName = Path.GetFileName(withExe);
        if (IsUnsupportedSccGuiBinary(fileName))
          throw new InvalidOperationException("SCC GUI binary is not supported for automation. Use cscc.exe or cscc-remote.exe.");

        if (IsSupportedCliBinary(fileName))
          return Path.GetFullPath(withExe);
      }
    }

    throw new FileNotFoundException("SCAP command not found. Configure cscc.exe or cscc-remote.exe.", trimmedPath);
  }

  private static readonly string[] SupportedCliBinaryNames =
  [
    "cscc.exe",
    "cscc-remote.exe",
    "cscc",
    "cscc-remote"
  ];

  private static bool IsSupportedCliBinary(string? fileName)
  {
    if (string.IsNullOrWhiteSpace(fileName))
      return false;

    return SupportedCliBinaryNames.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase));
  }

  private static bool IsUnsupportedSccGuiBinary(string? fileName)
  {
    if (string.IsNullOrWhiteSpace(fileName))
      return false;

    return string.Equals(fileName, "scc.exe", StringComparison.OrdinalIgnoreCase)
      || string.Equals(fileName, "scc", StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsSimpleCommandName(string path)
  {
    return path.IndexOf(Path.DirectorySeparatorChar) < 0
      && path.IndexOf(Path.AltDirectorySeparatorChar) < 0;
  }
}
