using System.Runtime.Versioning;
using FluentAssertions;
using Moq;
using STIGForge.Core.Abstractions;
using STIGForge.Infrastructure.Storage;

namespace STIGForge.UnitTests.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStoreTests : IDisposable
{
  private readonly string _tempDir;
  private readonly DpapiCredentialStore _store;

  public DpapiCredentialStoreTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-cred-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
    Directory.CreateDirectory(_tempDir);

    var mockPaths = new Mock<IPathBuilder>();
    mockPaths.Setup(p => p.GetAppDataRoot()).Returns(_tempDir);
    _store = new DpapiCredentialStore(mockPaths.Object);
  }

  [Fact]
  public void Save_and_Load_roundtrips_successfully()
  {
    _store.Save("server01", "admin", "P@ssw0rd!");
    var cred = _store.Load("server01");

    cred.Should().NotBeNull();
    cred!.Value.Username.Should().Be("admin");
    cred!.Value.Password.Should().Be("P@ssw0rd!");
  }

  [Fact]
  public void Load_returns_null_for_unknown_host()
  {
    var cred = _store.Load("nonexistent-host");
    cred.Should().BeNull();
  }

  [Fact]
  public void Remove_returns_true_for_existing_credential()
  {
    _store.Save("server02", "user", "pass");
    var removed = _store.Remove("server02");
    removed.Should().BeTrue();

    // Verify it's actually gone
    _store.Load("server02").Should().BeNull();
  }

  [Fact]
  public void Remove_returns_false_for_nonexistent_credential()
  {
    var removed = _store.Remove("no-such-host");
    removed.Should().BeFalse();
  }

  [Fact]
  public void ListHosts_returns_all_saved_hosts()
  {
    _store.Save("alpha", "u1", "p1");
    _store.Save("bravo", "u2", "p2");
    _store.Save("charlie", "u3", "p3");

    var hosts = _store.ListHosts();
    hosts.Should().HaveCount(3);
    hosts.Should().Contain("alpha");
    hosts.Should().Contain("bravo");
    hosts.Should().Contain("charlie");
  }

  [Fact]
  public void ListHosts_returns_empty_when_no_credentials()
  {
    var hosts = _store.ListHosts();
    hosts.Should().BeEmpty();
  }

  [Fact]
  public void Save_overwrites_existing_credential()
  {
    _store.Save("server03", "olduser", "oldpass");
    _store.Save("server03", "newuser", "newpass");

    var cred = _store.Load("server03");
    cred.Should().NotBeNull();
    cred!.Value.Username.Should().Be("newuser");
    cred!.Value.Password.Should().Be("newpass");
  }

  [Fact]
  public void Save_throws_for_empty_host()
  {
    var act = () => _store.Save("", "user", "pass");
    act.Should().Throw<ArgumentException>();
  }

  [Fact]
  public void Save_throws_for_empty_username()
  {
    var act = () => _store.Save("host", "", "pass");
    act.Should().Throw<ArgumentException>();
  }

  public void Dispose()
  {
    try
    {
      if (Directory.Exists(_tempDir))
        Directory.Delete(_tempDir, true);
    }
    catch { /* best-effort cleanup */ }
  }
}
