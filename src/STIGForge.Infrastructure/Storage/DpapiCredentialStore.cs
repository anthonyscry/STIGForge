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
  // Application-specific entropy prevents other same-user processes from decrypting
  // STIGForge credential files, adding defense-in-depth over null entropy.
  private static readonly byte[] AppEntropy = "STIGForge-CredentialStore-v1"u8.ToArray();

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

    var json = JsonSerializer.Serialize(new CredentialPayload { H = targetHost, U = username, P = password });
    var plainBytes = Encoding.UTF8.GetBytes(json);
    byte[] encryptedBytes;
    try
    {
      encryptedBytes = ProtectedData.Protect(plainBytes, AppEntropy, DataProtectionScope.CurrentUser);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(plainBytes);
    }

    var filePath = GetCredentialPath(targetHost);
    File.WriteAllBytes(filePath, encryptedBytes);
  }

  public (string Username, string Password)? Load(string targetHost)
  {
    var filePath = GetCredentialPath(targetHost);

    // One-time migration: rename legacy sanitized-name files to new SHA-256-hash names
    if (!File.Exists(filePath))
    {
      var legacyPath = GetLegacyCredentialPath(targetHost);
      if (File.Exists(legacyPath))
        File.Move(legacyPath, filePath);
    }

    if (!File.Exists(filePath)) return null;

    var encryptedBytes = File.ReadAllBytes(filePath);
    var plainBytes = ProtectedData.Unprotect(encryptedBytes, AppEntropy, DataProtectionScope.CurrentUser);
    try
    {
      var json = Encoding.UTF8.GetString(plainBytes);
      var payload = JsonSerializer.Deserialize<CredentialPayload>(json);

      if (payload == null || string.IsNullOrEmpty(payload.U))
        return null;

      return (payload.U, payload.P);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(plainBytes);
    }
  }

  public bool Remove(string targetHost)
  {
    var filePath = GetCredentialPath(targetHost);

    // Also clean up legacy file if present (covers case where Load hasn't migrated it yet)
    var legacyPath = GetLegacyCredentialPath(targetHost);
    if (File.Exists(legacyPath))
      File.Delete(legacyPath);

    if (!File.Exists(filePath)) return false;

    File.Delete(filePath);
    return true;
  }

  public IReadOnlyList<string> ListHosts()
  {
    if (!Directory.Exists(_credDir))
      return [];

    var hosts = new List<string>();
    foreach (var file in Directory.EnumerateFiles(_credDir, "*.cred"))
    {
      try
      {
        var encryptedBytes = File.ReadAllBytes(file);
        var plainBytes = ProtectedData.Unprotect(encryptedBytes, AppEntropy, DataProtectionScope.CurrentUser);
        try
        {
          var json = Encoding.UTF8.GetString(plainBytes);
          var payload = JsonSerializer.Deserialize<CredentialPayload>(json);
          if (!string.IsNullOrEmpty(payload?.H))
            hosts.Add(payload.H);
        }
        finally
        {
          CryptographicOperations.ZeroMemory(plainBytes);
        }
      }
      catch
      {
        // Skip files that cannot be decrypted (different user, corruption, etc.)
      }
    }
    return hosts;
  }

  private string GetCredentialPath(string targetHost)
    => Path.Combine(_credDir, GetCredentialFileName(targetHost));

  private string GetLegacyCredentialPath(string targetHost)
  {
    var safe = new StringBuilder(targetHost.Length);
    foreach (var c in targetHost)
      safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '.' || c == '_' ? c : '_');
    return Path.Combine(_credDir, safe + ".cred");
  }

  private static string GetCredentialFileName(string hostName)
  {
    var hashBytes = SHA256.HashData(
        Encoding.UTF8.GetBytes(hostName.ToLowerInvariant()));
    return Convert.ToHexString(hashBytes).ToLowerInvariant() + ".cred";
  }

  private sealed class CredentialPayload
  {
    public string H { get; set; } = string.Empty;  // hostname
    public string U { get; set; } = string.Empty;
    public string P { get; set; } = string.Empty;
  }
}
