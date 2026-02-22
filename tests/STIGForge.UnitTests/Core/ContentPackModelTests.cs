using STIGForge.Core.Models;
using Xunit;

namespace STIGForge.UnitTests.Core;

public sealed class ContentPackModelTests
{
    [Fact]
    public void BenchmarkIds_DefaultsToEmpty()
    {
        var pack = new ContentPack();
        Assert.NotNull(pack.BenchmarkIds);
        Assert.Empty(pack.BenchmarkIds);
    }

    [Fact]
    public void ApplicabilityTags_DefaultsToEmpty()
    {
        var pack = new ContentPack();
        Assert.NotNull(pack.ApplicabilityTags);
        Assert.Empty(pack.ApplicabilityTags);
    }

    [Fact]
    public void Version_DefaultsToEmpty()
    {
        var pack = new ContentPack();
        Assert.Equal(string.Empty, pack.Version);
    }

    [Fact]
    public void Release_DefaultsToEmpty()
    {
        var pack = new ContentPack();
        Assert.Equal(string.Empty, pack.Release);
    }

    [Fact]
    public void BenchmarkIds_CanBeAssigned()
    {
        var pack = new ContentPack
        {
            BenchmarkIds = new List<string> { "Windows_10_STIG", "Windows_Server_2022_STIG" }
        };
        Assert.Equal(2, pack.BenchmarkIds.Count);
        Assert.Contains("Windows_10_STIG", pack.BenchmarkIds);
        Assert.Contains("Windows_Server_2022_STIG", pack.BenchmarkIds);
    }

    [Fact]
    public void ApplicabilityTags_CanBeAssigned()
    {
        var pack = new ContentPack
        {
            ApplicabilityTags = new List<string> { "Win10", "Server2022", "MemberServer" }
        };
        Assert.Equal(3, pack.ApplicabilityTags.Count);
        Assert.Contains("Win10", pack.ApplicabilityTags);
    }

    [Fact]
    public void SchemaVersion_IsCanonicalVersion()
    {
        var pack = new ContentPack();
        Assert.Equal(CanonicalContract.Version, pack.SchemaVersion);
    }
}
