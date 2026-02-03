namespace STIGForge.Evidence;

public sealed class EvidenceCollector
{
  public Task CollectAsync(string controlId, CancellationToken ct)
  {
    return Task.CompletedTask;
  }
}
