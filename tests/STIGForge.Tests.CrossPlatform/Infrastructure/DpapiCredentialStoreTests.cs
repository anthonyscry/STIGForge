using System.Reflection;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Storage;

#pragma warning disable CA1416 // Platform compatibility — non-DPAPI methods work on all OSes; DPAPI tests are Skip'd

namespace STIGForge.Tests.CrossPlatform.Infrastructure;

public class DpapiCredentialStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<IPathBuilder> _mockPathBuilder;
    private readonly DpapiCredentialStore _sut;

    public DpapiCredentialStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _mockPathBuilder = new Mock<IPathBuilder>();
        _mockPathBuilder.Setup(p => p.GetAppDataRoot()).Returns(_tempDir);
        _sut = new DpapiCredentialStore(_mockPathBuilder.Object);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void ListHosts_EmptyDirectory_ReturnsEmpty()
    {
        // Credentials directory does not exist
        var hosts = _sut.ListHosts();
        hosts.Should().BeEmpty();
    }

    [Fact]
    public void Remove_NonExistentHost_ReturnsFalse()
    {
        var result = _sut.Remove("nonexistent.host.example.com");
        result.Should().BeFalse();
    }

    [Fact]
    public void GetCredentialPath_DifferentHostnames_ProduceDifferentPaths()
    {
        var method = typeof(DpapiCredentialStore)
            .GetMethod("GetCredentialPath", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("GetCredentialPath private method must exist");

        var path1 = (string)method!.Invoke(_sut, new object[] { "host1.example.com" })!;
        var path2 = (string)method!.Invoke(_sut, new object[] { "host2.example.com" })!;

        path1.Should().NotBe(path2);
    }

    [Fact(Skip = "Windows only")]
    public void Save_And_Load_RoundTrips()
    {
        _sut.Save("myhost.local", "alice", "s3cr3t");
        var loaded = _sut.Load("myhost.local");

        loaded.Should().NotBeNull();
        loaded!.Value.Username.Should().Be("alice");
        loaded!.Value.Password.Should().Be("s3cr3t");
    }

    [Fact(Skip = "Windows only")]
    public void Remove_ExistingCredential_ReturnsTrueAndDeletes()
    {
        _sut.Save("remove-target.local", "bob", "pass");
        _sut.ListHosts().Should().Contain("remove-target_local"); // sanitised filename

        var removed = _sut.Remove("remove-target.local");
        removed.Should().BeTrue();
        _sut.Load("remove-target.local").Should().BeNull();
    }
}
