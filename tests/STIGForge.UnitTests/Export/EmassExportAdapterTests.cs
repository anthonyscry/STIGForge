using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Export;

namespace STIGForge.UnitTests.Export;

public sealed class EmassExportAdapterTests
{
    [Fact]
    public void FormatName_ReturnsEmass()
    {
        var emass = CreateEmassExporter();
        Assert.Equal("eMASS", emass.FormatName);
    }

    [Fact]
    public void SupportedExtensions_ReturnsEmpty()
    {
        var emass = CreateEmassExporter();
        Assert.Empty(emass.SupportedExtensions);
    }

    [Fact]
    public void ImplementsIExportAdapter()
    {
        Assert.True(typeof(IExportAdapter).IsAssignableFrom(typeof(EmassExporter)));
    }

    private static EmassExporter CreateEmassExporter()
    {
        return new EmassExporter(
            new FakePathBuilder(),
            new FakeHashingService());
    }

    private sealed class FakePathBuilder : IPathBuilder
    {
        public string GetAppDataRoot() => "/fake";
        public string GetContentPacksRoot() => "/fake/packs";
        public string GetPackRoot(string packId) => "/fake/packs/" + packId;
        public string GetBundleRoot(string bundleId) => "/fake/bundles/" + bundleId;
        public string GetLogsRoot() => "/fake/logs";
        public string GetImportRoot() => "/fake/import";
        public string GetImportInboxRoot() => "/fake/import/inbox";
        public string GetImportIndexPath() => "/fake/import/index.json";
        public string GetToolsRoot() => "/fake/tools";
        public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts)
            => "/fake/emass";
    }

    private sealed class FakeHashingService : IHashingService
    {
        public Task<string> Sha256FileAsync(string path, CancellationToken ct)
            => Task.FromResult("fakehash");
        public Task<string> Sha256TextAsync(string content, CancellationToken ct)
            => Task.FromResult("fakehash");
    }
}
