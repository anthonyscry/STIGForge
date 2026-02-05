using System.Security.Cryptography;
using System.Text;
using STIGForge.Core.Models;

namespace STIGForge.Core.Services;

public static class ControlFingerprint
{
  public static string Compute(ControlRecord control)
  {
    var text = string.Join("|", new[]
    {
      control.ExternalIds.RuleId ?? string.Empty,
      control.ExternalIds.VulnId ?? string.Empty,
      control.Title ?? string.Empty,
      control.Severity ?? string.Empty,
      control.Discussion ?? string.Empty,
      control.CheckText ?? string.Empty,
      control.FixText ?? string.Empty,
      control.IsManual ? "manual" : "auto"
    });

    using var sha = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(text);
    var hash = sha.ComputeHash(bytes);
    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
  }
}
