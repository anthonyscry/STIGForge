using STIGForge.Core.Models;

namespace STIGForge.Verify;

public sealed class VerifyRunner
{
  public Task RunAsync(RunManifest manifest, CancellationToken ct)
  {
    return Task.CompletedTask;
  }
}
