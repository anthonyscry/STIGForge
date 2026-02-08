using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using STIGForge.Core.Abstractions;
#if NET8_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace STIGForge.Infrastructure.Storage;

/// <summary>
/// Credential store backed by Windows DPAPI (CurrentUser scope).
/// Each credential is stored as an encrypted .cred file in the credentials directory.
/// </summary>
#if NET8_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class DpapiCredentialStore : ICredentialStore
{
  private readonly string _credDir;

  public DpapiCredentialStore(IPathBuilder pathBuilder)
  {
    _credDir = Path.Combine(pathBuilder.GetAppDataRoot(), "credentials");
  }

  public void Save(string targetHost, string username, string password)
  {
    if (string.IsNullOrWhiteSpace(targetHost)) throw new ArgumentException("Target host is required.", nameof(targetHost));
    if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));

    Directory.CreateDirectory(_credDir);

    var json = JsonSerializer.Serialize(new CredentialPayload { U = username, P = password });
    var plainBytes = Encoding.UTF8.GetBytes(json);
    var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

    var filePath = GetCredentialPath(targetHost);
    File.WriteAllBytes(filePath, encryptedBytes);
  }

  public (string Username, string Password)? Load(string targetHost)
  {
    var filePath = GetCredentialPath(targetHost);
    if (!File.Exists(filePath)) return null;

    var encryptedBytes = File.ReadAllBytes(filePath);
    var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
    var json = Encoding.UTF8.GetString(plainBytes);
    var payload = JsonSerializer.Deserialize<CredentialPayload>(json);

    if (payload == null || string.IsNullOrEmpty(payload.U))
      return null;

    return (payload.U, payload.P);
  }

  public bool Remove(string targetHost)
  {
    var filePath = GetCredentialPath(targetHost);
    if (!File.Exists(filePath)) return false;

    File.Delete(filePath);
    return true;
  }

  public IReadOnlyList<string> ListHosts()
  {
    if (!Directory.Exists(_credDir))
      return Array.Empty<string>();

    var files = Directory.GetFiles(_credDir, "*.cred");
    var hosts = new List<string>(files.Length);
    foreach (var file in files)
      hosts.Add(Path.GetFileNameWithoutExtension(file));
    return hosts;
  }

  private string GetCredentialPath(string targetHost)
  {
    // Sanitize hostname for safe file naming
    var safe = new StringBuilder(targetHost.Length);
    foreach (var c in targetHost)
    {
      if (char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_')
        safe.Append(c);
      else
        safe.Append('_');
    }
    return Path.Combine(_credDir, safe.ToString() + ".cred");
  }

  private sealed class CredentialPayload
  {
    public string U { get; set; } = string.Empty;
    public string P { get; set; } = string.Empty;
  }
}
