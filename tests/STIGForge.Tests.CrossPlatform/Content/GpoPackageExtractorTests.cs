using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class GpoPackageExtractorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private string ExtractedRoot => System.IO.Path.Combine(_temp.Path, "extracted");
    private string ApplyRoot => System.IO.Path.Combine(_temp.Path, "apply");

    private void EnsureExtractedRoot() => Directory.CreateDirectory(ExtractedRoot);

    private void CreateAdmxFolder(string osFolder, params string[] admxNames)
    {
        var admxRoot = System.IO.Path.Combine(ExtractedRoot, "ADMX Templates", osFolder);
        Directory.CreateDirectory(admxRoot);
        foreach (var name in admxNames)
        {
            var fullPath = System.IO.Path.Combine(admxRoot, name);
            var dir = System.IO.Path.GetDirectoryName(fullPath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(fullPath, $"<!-- {name} -->");
        }
    }

    private void CreateRegistryPol(string osFolder)
    {
        var polDir = System.IO.Path.Combine(ExtractedRoot, ".Support Files", "Local Policies",
            osFolder, "DomainSysvol", "GPO", "Machine");
        Directory.CreateDirectory(polDir);
        File.WriteAllBytes(System.IO.Path.Combine(polDir, "Registry.pol"), new byte[] { 0x50, 0x52, 0x65, 0x67 });
    }

    private void CreateGptTmpl(string osFolder)
    {
        var infDir = System.IO.Path.Combine(ExtractedRoot, ".Support Files", "Local Policies",
            osFolder, "DomainSysvol", "GPO", "Machine", "microsoft", "windows nt", "secedit");
        Directory.CreateDirectory(infDir);
        File.WriteAllText(System.IO.Path.Combine(infDir, "GptTmpl.inf"), "[Version]\nSignature=$CHICAGO$");
    }

    // ── MapFolderToOsTarget ───────────────────────────────────────────────────

    [Theory]
    [InlineData("Windows 11", OsTarget.Win11)]
    [InlineData("Win11", OsTarget.Win11)]
    [InlineData("Windows_11", OsTarget.Win11)]
    [InlineData("Windows 10", OsTarget.Win10)]
    [InlineData("Win10", OsTarget.Win10)]
    [InlineData("Windows Server 2022 Member Server", OsTarget.Server2022)]
    [InlineData("2022 Member Server", OsTarget.Server2022)]
    [InlineData("Windows Server 2019", OsTarget.Server2019)]
    [InlineData("Something Else", OsTarget.Unknown)]
    [InlineData("", OsTarget.Unknown)]
    public void MapFolderToOsTarget_KnownNames_ReturnExpectedTarget(string folderName, OsTarget expected)
    {
        GpoPackageExtractor.MapFolderToOsTarget(folderName).Should().Be(expected);
    }

    [Fact]
    public void MapFolderToOsTarget_NullInput_ReturnsUnknown()
    {
        GpoPackageExtractor.MapFolderToOsTarget(null!).Should().Be(OsTarget.Unknown);
    }

    [Fact]
    public void MapFolderToOsTarget_WhitespaceOnly_ReturnsUnknown()
    {
        GpoPackageExtractor.MapFolderToOsTarget("   ").Should().Be(OsTarget.Unknown);
    }

    // ── StageForApply ────────────────────────────────────────────────────────

    [Fact]
    public void StageForApply_NonExistentRoot_ThrowsDirectoryNotFoundException()
    {
        var act = () => GpoPackageExtractor.StageForApply(
            System.IO.Path.Combine(_temp.Path, "no-such-dir"),
            ApplyRoot,
            OsTarget.Win10);

        act.Should().Throw<DirectoryNotFoundException>()
           .WithMessage("*GPO extraction root not found*");
    }

    [Fact]
    public void StageForApply_EmptyDirectory_ReturnsZeroCounts()
    {
        EnsureExtractedRoot();

        var result = GpoPackageExtractor.StageForApply(ExtractedRoot, ApplyRoot, OsTarget.Win10);

        result.AdmxFileCount.Should().Be(0);
        result.PolFilePath.Should().BeNull();
        result.SecurityTemplatePath.Should().BeNull();
        result.DomainGpoCount.Should().Be(0);
        result.HasAnyArtifacts.Should().BeFalse();
    }

    [Fact]
    public void StageForApply_WithAdmxFiles_StagesAndCountsCorrectly()
    {
        EnsureExtractedRoot();
        CreateAdmxFolder("Windows 10", "custom.admx", "lang/en-US/custom.adml");

        var result = GpoPackageExtractor.StageForApply(ExtractedRoot, ApplyRoot, OsTarget.Win10);

        result.AdmxFileCount.Should().Be(1);
        result.HasAnyArtifacts.Should().BeTrue();
        var stagedAdmx = System.IO.Path.Combine(ApplyRoot, "ADMX Templates", "custom.admx");
        File.Exists(stagedAdmx).Should().BeTrue();
    }

    [Fact]
    public void StageForApply_AdmxForDifferentOs_NotStaged()
    {
        EnsureExtractedRoot();
        CreateAdmxFolder("Windows 11", "w11.admx");

        var result = GpoPackageExtractor.StageForApply(ExtractedRoot, ApplyRoot, OsTarget.Win10);

        result.AdmxFileCount.Should().Be(0);
    }

    [Fact]
    public void StageForApply_WithRegistryPol_StagesPolFile()
    {
        EnsureExtractedRoot();
        CreateRegistryPol("Windows 10");

        var result = GpoPackageExtractor.StageForApply(ExtractedRoot, ApplyRoot, OsTarget.Win10);

        result.PolFilePath.Should().NotBeNullOrEmpty();
        File.Exists(result.PolFilePath).Should().BeTrue();
        System.IO.Path.GetFileName(result.PolFilePath).Should().Be("Registry.pol");
    }

    [Fact]
    public void StageForApply_WithGptTmplInf_StagesSecurityTemplate()
    {
        EnsureExtractedRoot();
        CreateGptTmpl("Windows 10");

        var result = GpoPackageExtractor.StageForApply(ExtractedRoot, ApplyRoot, OsTarget.Win10);

        result.SecurityTemplatePath.Should().NotBeNullOrEmpty();
        File.Exists(result.SecurityTemplatePath).Should().BeTrue();
        System.IO.Path.GetFileName(result.SecurityTemplatePath).Should().Be("GptTmpl.inf");
    }

    [Fact]
    public void StageForApply_WithAllArtifacts_HasAnyArtifactsIsTrue()
    {
        EnsureExtractedRoot();
        CreateAdmxFolder("Windows 10", "test.admx");
        CreateRegistryPol("Windows 10");
        CreateGptTmpl("Windows 10");

        var result = GpoPackageExtractor.StageForApply(ExtractedRoot, ApplyRoot, OsTarget.Win10);

        result.HasAnyArtifacts.Should().BeTrue();
    }

    [Fact]
    public void StageForApply_CreatesApplyRootIfMissing()
    {
        EnsureExtractedRoot();
        var freshApplyRoot = System.IO.Path.Combine(_temp.Path, "fresh-apply");

        GpoPackageExtractor.StageForApply(ExtractedRoot, freshApplyRoot, OsTarget.Win10);

        Directory.Exists(freshApplyRoot).Should().BeTrue();
    }

    // ── DetectOsScopes ────────────────────────────────────────────────────────

    [Fact]
    public void DetectOsScopes_EmptyDirectory_ReturnsEmpty()
    {
        EnsureExtractedRoot();

        var scopes = GpoPackageExtractor.DetectOsScopes(ExtractedRoot);

        scopes.Should().BeEmpty();
    }

    [Fact]
    public void DetectOsScopes_WithAdmxTemplatesSubfolders_DetectsScopes()
    {
        EnsureExtractedRoot();
        CreateAdmxFolder("Windows 10", "test.admx");
        CreateAdmxFolder("Windows 11", "test.admx");

        var scopes = GpoPackageExtractor.DetectOsScopes(ExtractedRoot);

        scopes.Should().Contain(s => s.OsTarget == OsTarget.Win10);
        scopes.Should().Contain(s => s.OsTarget == OsTarget.Win11);
    }

    [Fact]
    public void DetectOsScopes_UnknownFolderNames_ExcludesUnknown()
    {
        EnsureExtractedRoot();
        var unknownDir = System.IO.Path.Combine(ExtractedRoot, "ADMX Templates", "SomeOtherOS");
        Directory.CreateDirectory(unknownDir);

        var scopes = GpoPackageExtractor.DetectOsScopes(ExtractedRoot);

        scopes.Should().NotContain(s => s.OsTarget == OsTarget.Unknown);
    }

    // ── GpoStagingResult model ────────────────────────────────────────────────

    [Fact]
    public void GpoStagingResult_HasAnyArtifacts_FalseWhenAllEmpty()
    {
        var result = new GpoStagingResult();
        result.HasAnyArtifacts.Should().BeFalse();
    }

    [Fact]
    public void GpoStagingResult_HasAnyArtifacts_TrueWhenAdmxCountPositive()
    {
        var result = new GpoStagingResult { AdmxFileCount = 1 };
        result.HasAnyArtifacts.Should().BeTrue();
    }

    [Fact]
    public void GpoStagingResult_HasDomainGpos_TrueWhenCountPositive()
    {
        var result = new GpoStagingResult { DomainGpoCount = 3 };
        result.HasDomainGpos.Should().BeTrue();
    }
}
