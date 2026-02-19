using STIGForge.Export;

namespace STIGForge.UnitTests.Export;

public sealed class ExportAdapterRegistryTests
{
    private sealed class StubExportAdapter : IExportAdapter
    {
        public string FormatName { get; }
        public string[] SupportedExtensions => Array.Empty<string>();

        public StubExportAdapter(string formatName) => FormatName = formatName;

        public Task<ExportAdapterResult> ExportAsync(ExportAdapterRequest request, CancellationToken ct)
            => Task.FromResult(new ExportAdapterResult { Success = true, OutputPaths = new[] { "/fake/path" } });
    }

    [Fact]
    public void Register_NullAdapter_ThrowsArgumentNullException()
    {
        var registry = new ExportAdapterRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void TryResolve_RegisteredFormat_ReturnsAdapter()
    {
        var registry = new ExportAdapterRegistry();
        registry.Register(new StubExportAdapter("Test"));

        var adapter = registry.TryResolve("test"); // case-insensitive
        Assert.NotNull(adapter);
        Assert.Equal("Test", adapter.FormatName);
    }

    [Fact]
    public void TryResolve_UnknownFormat_ReturnsNull()
    {
        var registry = new ExportAdapterRegistry();
        registry.Register(new StubExportAdapter("Test"));

        var adapter = registry.TryResolve("unknown");
        Assert.Null(adapter);
    }

    [Fact]
    public void GetAll_ReturnsAllRegistered()
    {
        var registry = new ExportAdapterRegistry();
        registry.Register(new StubExportAdapter("Format1"));
        registry.Register(new StubExportAdapter("Format2"));

        var all = registry.GetAll();
        Assert.Equal(2, all.Count);
    }
}
