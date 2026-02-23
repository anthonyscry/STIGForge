using STIGForge.Core.Models;
using Xunit;

namespace STIGForge.UnitTests.Core;

public sealed class CanonicalSchemaTests
{
    [Fact]
    public void CanonicalContract_Version_Is_1_1_0()
    {
        Assert.Equal("1.1.0", CanonicalContract.Version);
    }

    [Fact]
    public void CanonicalContract_HasAllTypeConstants()
    {
        Assert.False(string.IsNullOrEmpty(CanonicalContract.ContentPackType));
        Assert.False(string.IsNullOrEmpty(CanonicalContract.ControlRecordType));
        Assert.False(string.IsNullOrEmpty(CanonicalContract.ProfileType));
        Assert.False(string.IsNullOrEmpty(CanonicalContract.OverlayType));
        Assert.False(string.IsNullOrEmpty(CanonicalContract.BundleManifestType));
        Assert.False(string.IsNullOrEmpty(CanonicalContract.VerificationResultType));
        Assert.False(string.IsNullOrEmpty(CanonicalContract.EvidenceRecordType));
        Assert.False(string.IsNullOrEmpty(CanonicalContract.ExportIndexEntryType));
    }

    [Fact]
    public void VerificationResult_HasSchemaVersion()
    {
        var result = new VerificationResult();
        Assert.Equal(CanonicalContract.Version, result.SchemaVersion);
    }

    [Fact]
    public void VerificationResult_DefaultsAreSafe()
    {
        var result = new VerificationResult();
        Assert.Equal(string.Empty, result.ControlId);
        Assert.Equal(string.Empty, result.Status);
        Assert.Null(result.VulnId);
        Assert.Null(result.RuleId);
        Assert.Null(result.VerifiedAt);
        Assert.Null(result.BenchmarkId);
    }

    [Fact]
    public void EvidenceRecord_HasSchemaVersion()
    {
        var record = new EvidenceRecord();
        Assert.Equal(CanonicalContract.Version, record.SchemaVersion);
    }

    [Fact]
    public void EvidenceRecord_DefaultsAreSafe()
    {
        var record = new EvidenceRecord();
        Assert.Equal(string.Empty, record.ControlId);
        Assert.Equal(string.Empty, record.Sha256);
        Assert.Null(record.RuleId);
        Assert.Null(record.RunId);
    }

    [Fact]
    public void ExportIndexEntry_HasSchemaVersion()
    {
        var entry = new ExportIndexEntry();
        Assert.Equal(CanonicalContract.Version, entry.SchemaVersion);
    }

    [Fact]
    public void ExportIndexEntry_DefaultsAreSafe()
    {
        var entry = new ExportIndexEntry();
        Assert.Equal(string.Empty, entry.FilePath);
        Assert.Equal(string.Empty, entry.ArtifactType);
        Assert.Equal(string.Empty, entry.Sha256);
    }

    [Fact]
    public void ContentPack_SchemaVersion_MatchesCanonical()
    {
        var pack = new ContentPack();
        Assert.Equal(CanonicalContract.Version, pack.SchemaVersion);
    }
}
