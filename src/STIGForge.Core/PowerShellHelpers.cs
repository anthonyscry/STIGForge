using System.Text;

namespace STIGForge.Core;

/// <summary>
/// Shared PowerShell argument construction helpers.
/// Centralised to prevent duplication and ensure consistent injection prevention.
/// </summary>
public static class PowerShellHelpers
{
  /// <summary>
  /// Wraps a value in PowerShell single quotes, escaping embedded single quotes
  /// by doubling them. This is the canonical quoting method for user-supplied
  /// values that will appear in PowerShell command strings.
  /// </summary>
  public static string SingleQuote(string? value)
  {
    return "'" + (value ?? string.Empty).Replace("'", "''") + "'";
  }

  /// <summary>
  /// Builds a <c>-NoProfile -ExecutionPolicy Bypass -EncodedCommand …</c> argument
  /// string suitable for <c>powershell.exe</c>. The command is Base64-encoded using
  /// UTF-16LE (the encoding PowerShell expects for <c>-EncodedCommand</c>).
  /// </summary>
  public static string BuildEncodedCommandArgs(string command)
  {
    var bytes = Encoding.Unicode.GetBytes(command);
    var encoded = Convert.ToBase64String(bytes);
    return "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encoded;
  }
}
