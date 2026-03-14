using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.Tests.CrossPlatform.Verify;

public sealed class VerifyRunnerTests
{
    [Fact]
    public async Task RunAsync_ThrowsNotImplementedException()
    {
        var runner = new VerifyRunner();
        var manifest = new RunManifest { RunId = "test-run" };

        await Assert.ThrowsAsync<NotImplementedException>(
            () => runner.RunAsync(manifest, CancellationToken.None));
    }
}
