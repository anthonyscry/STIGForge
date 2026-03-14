using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Verify;

public sealed class VerifyRunnerTests
{
    [Fact]
    public async Task RunAsync_Always_ThrowsNotImplementedException()
    {
        var runner = new VerifyRunner();
        var manifest = new RunManifest();

        var act = async () => await runner.RunAsync(manifest, CancellationToken.None);

        await act.Should().ThrowAsync<NotImplementedException>()
            .WithMessage("*VerifyRunner.RunAsync*");
    }

    [Fact]
    public async Task RunAsync_WithNullManifest_ThrowsNotImplementedException()
    {
        var runner = new VerifyRunner();

        var act = async () => await runner.RunAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task RunAsync_WithCancelledToken_ThrowsNotImplementedException()
    {
        var runner = new VerifyRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await runner.RunAsync(new RunManifest(), cts.Token);

        await act.Should().ThrowAsync<NotImplementedException>();
    }
}
