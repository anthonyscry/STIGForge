using STIGForge.Core.Models;

namespace STIGForge.Apply;

public interface IApplyRunner
{
  Task<ApplyResult> RunAsync(ApplyRequest request, CancellationToken ct);
}
