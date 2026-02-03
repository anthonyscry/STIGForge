using System.Security.Cryptography;
using System.Text;
using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Hashing;

public sealed class Sha256HashingService : IHashingService
{
  public Task<string> Sha256FileAsync(string path, CancellationToken ct)
  {
    using var stream = File.OpenRead(path);
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(stream);
    return Task.FromResult(ToHex(hash));
  }

  public Task<string> Sha256TextAsync(string content, CancellationToken ct)
  {
    using var sha = SHA256.Create();
    var bytes = Encoding.UTF8.GetBytes(content);
    var hash = sha.ComputeHash(bytes);
    return Task.FromResult(ToHex(hash));
  }

  private static string ToHex(byte[] bytes)
  {
    return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
  }
}
