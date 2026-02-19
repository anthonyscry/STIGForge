using STIGForge.Export;

namespace STIGForge.UnitTests.Export;

public sealed class ExportOrchestratorTests
{
    private sealed class StubExportAdapter : IExportAdapter
    {
        public string FormatName { get; }
        public string[] SupportedExtensions => Array.Empty<string>();
        public bool WasInvoked { get; private set; }

        public StubExportAdapter(string formatName) => FormatName = formatName;

        public Task<ExportAdapterResult> ExportAsync(ExportAdapterRequest request, CancellationToken ct)
        {
            WasInvoked = true;
            return Task.FromResult(new ExportAdapterResult { Success = true, OutputPaths = new[] { "/fake/path" } });
        }
    }

    [Fact]
    public async Task ExportAsync_KnownFormat_DelegatesToAdapter()
    {
        var registry = new ExportAdapterRegistry();
        var stub = new StubExportAdapter("Test");
        registry.Register(stub);
        var orchestrator = new ExportOrchestrator(registry);

        var result = await orchestrator.ExportAsync("Test", new ExportAdapterRequest(), CancellationToken.None);

        Assert.True(stub.WasInvoked);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExportAsync_UnknownFormat_ReturnsFailureResult()
    {
        var registry = new ExportAdapterRegistry();
        var orchestrator = new ExportOrchestrator(registry);

        var result = await orchestrator.ExportAsync("nonexistent", new ExportAdapterRequest(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("nonexistent", result.ErrorMessage);
    }
}
