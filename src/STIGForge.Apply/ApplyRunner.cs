using STIGForge.Core.Models;

namespace STIGForge.Apply;

public sealed class ApplyRunner
{
  public Task RunAsync(RunManifest manifest, CancellationToken ct)
  {
    return Task.CompletedTask;
  }
}
