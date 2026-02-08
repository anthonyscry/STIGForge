using System.Runtime.Versioning;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.IntegrationTests.Storage;

[SupportedOSPlatform("windows")]
public class DpapiCredentialStoreIntegrationTests : IDisposable
{
    private readonly string _testRoot;

    public DpapiCredentialStoreIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "stigforge_test_" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    private sealed class TempPathBuilder : IPathBuilder
    {
        private readonly string _root;
        public TempPathBuilder(string root) => _root = root;
        public string GetAppDataRoot() => _root;
        public string GetContentPacksRoot() => Path.Combine(_root, "packs");
        public string GetPackRoot(string packId) => Path.Combine(_root, "packs", packId);
        public string GetBundleRoot(string bundleId) => Path.Combine(_root, "bundles", bundleId);
        public string GetLogsRoot() => Path.Combine(_root, "logs");
        public string GetEmassExportRoot(string systemName, string os, string role, string profileName, string packName, DateTimeOffset ts)
            => Path.Combine(_root, "emass");
    }

    [Fact]
    public void SaveAndLoad_RoundTrip()
    {
        var store = new DpapiCredentialStore(new TempPathBuilder(_testRoot));

        store.Save("server01.example.com", "admin", "P@ssw0rd!");

        var cred = store.Load("server01.example.com");

        cred.Should().NotBeNull();
        cred!.Value.Username.Should().Be("admin");
        cred!.Value.Password.Should().Be("P@ssw0rd!");
    }

    [Fact]
    public void Load_UnknownHost_ReturnsNull()
    {
        var store = new DpapiCredentialStore(new TempPathBuilder(_testRoot));

        var cred = store.Load("unknown-host-xyz");

        cred.Should().BeNull();
    }

    [Fact]
    public void Remove_ExistingHost_ReturnsTrue()
    {
        var store = new DpapiCredentialStore(new TempPathBuilder(_testRoot));

        store.Save("remove-me.local", "user1", "pass1");

        var removed = store.Remove("remove-me.local");

        removed.Should().BeTrue();

        var cred = store.Load("remove-me.local");
        cred.Should().BeNull();
    }

    [Fact]
    public void ListHosts_ReturnsAllSaved()
    {
        var store = new DpapiCredentialStore(new TempPathBuilder(_testRoot));

        store.Save("host-a", "userA", "passA");
        store.Save("host-b", "userB", "passB");
        store.Save("host-c", "userC", "passC");

        var hosts = store.ListHosts();

        hosts.Should().HaveCount(3);
        hosts.Should().Contain("host-a");
        hosts.Should().Contain("host-b");
        hosts.Should().Contain("host-c");
    }
}
