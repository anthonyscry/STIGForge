using System.Security.Cryptography;
using System.Text;
using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Hashing;

public sealed class Sha256HashingService : IHashingService
{
  public async Task<string> Sha256FileAsync(string path, CancellationToken ct)
  {
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
    using var sha = SHA256.Create();
    var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  public Task<string> Sha256TextAsync(string content, CancellationToken ct)
  {
    var bytes = Encoding.UTF8.GetBytes(content);
    var hash = SHA256.HashData(bytes);
    return Task.FromResult(Convert.ToHexString(hash).ToLowerInvariant());
  }
}
