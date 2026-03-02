using STIGForge.Core.Models;

namespace STIGForge.Core.Abstractions;

public interface IReleaseCheckRepository
{
  Task SaveAsync(ReleaseCheck check, CancellationToken ct);
  Task<ReleaseCheck?> GetAsync(string checkId, CancellationToken ct);
  Task<IReadOnlyList<ReleaseCheck>> ListByBaselineAsync(string baselinePackId, int limit, CancellationToken ct);
  Task<ReleaseCheck?> GetLatestAsync(string baselinePackId, CancellationToken ct);
}
