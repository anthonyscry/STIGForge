using STIGForge.Export;

namespace STIGForge.UnitTests.Export;

public sealed class CklExportAdapterTests
{
    [Fact]
    public void FormatName_ReturnsCKL()
    {
        var adapter = new CklExportAdapter();
        Assert.Equal("CKL", adapter.FormatName);
    }

    [Fact]
    public void SupportedExtensions_ContainsCklAndCklb()
    {
        var adapter = new CklExportAdapter();
        Assert.Contains(".ckl", adapter.SupportedExtensions);
        Assert.Contains(".cklb", adapter.SupportedExtensions);
    }

    [Fact]
    public void ImplementsIExportAdapter()
    {
        Assert.True(typeof(IExportAdapter).IsAssignableFrom(typeof(CklExportAdapter)));
    }

    [Fact]
    public async Task ExportAsync_EmptyBundleRoot_ReturnsFailure()
    {
        var adapter = new CklExportAdapter();
        var request = new ExportAdapterRequest { BundleRoot = string.Empty };

        var result = await adapter.ExportAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("BundleRoot", result.ErrorMessage);
    }
}
