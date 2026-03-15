using STIGForge.Core.Models;

namespace STIGForge.Build;

public interface IBundleBuilder
{
  Task<BundleBuildResult> BuildAsync(BundleBuildRequest request, CancellationToken ct);
}
