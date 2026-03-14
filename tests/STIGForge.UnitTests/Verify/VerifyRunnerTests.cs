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

        var ex = await Record.ExceptionAsync(() => runner.RunAsync(manifest, CancellationToken.None));

        ex.Should().BeOfType<NotImplementedException>();
        ex!.Message.Should().Contain("VerifyRunner.RunAsync");
    }

    [Fact]
    public async Task RunAsync_WithNullManifest_ThrowsNotImplementedException()
    {
        var runner = new VerifyRunner();

        var ex = await Record.ExceptionAsync(() => runner.RunAsync(null!, CancellationToken.None));

        ex.Should().BeOfType<NotImplementedException>();
    }

    [Fact]
    public async Task RunAsync_WithCancelledToken_ThrowsNotImplementedException()
    {
        var runner = new VerifyRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ex = await Record.ExceptionAsync(() => runner.RunAsync(new RunManifest(), cts.Token));

        ex.Should().BeOfType<NotImplementedException>();
    }
}
