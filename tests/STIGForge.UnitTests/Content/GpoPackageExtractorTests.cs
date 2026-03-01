using FluentAssertions;
using STIGForge.Content.Import;
using STIGForge.Core.Models;

namespace STIGForge.UnitTests.Content;

public sealed class GpoPackageExtractorTests : IDisposable
{
    private readonly string _tempDir;

    public GpoPackageExtractorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "stigforge-gpoext-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Theory]
    [InlineData("Windows 11", OsTarget.Win11)]
    [InlineData("Windows 10", OsTarget.Win10)]
    [InlineData("Windows Server 2022 Member Server", OsTarget.Server2022)]
    [InlineData("Windows Server 2019 Member Server", OsTarget.Server2019)]
    [InlineData("Win11", OsTarget.Win11)]
    [InlineData("Something Else", OsTarget.Unknown)]
    public void MapFolderToOsTarget_MapsCorrectly(string folderName, OsTarget expected)
    {
        GpoPackageExtractor.MapFolderToOsTarget(folderName).Should().Be(expected);
    }

    [Fact]
    public void DetectOsScopes_WithAdmxAndLocalPolicies_DetectsAllScopes()
    {
        var root = CreateGpoPackageStructure(new[] { "Windows 11", "Windows Server 2022 Member Server" });

        var scopes = GpoPackageExtractor.DetectOsScopes(root);

        scopes.Should().Contain(s => s.OsTarget == OsTarget.Win11);
        scopes.Should().Contain(s => s.OsTarget == OsTarget.Server2022);
    }

    [Fact]
    public void StageForApply_Win11Target_StagesOnlyWin11Artifacts()
    {
        var root = CreateGpoPackageStructure(new[] { "Windows 11", "Windows Server 2022 Member Server" });
        var applyRoot = Path.Combine(_tempDir, "apply");

        var result = GpoPackageExtractor.StageForApply(root, applyRoot, OsTarget.Win11);

        result.HasAnyArtifacts.Should().BeTrue();
        result.AdmxFileCount.Should().Be(1);
        result.PolFilePath.Should().NotBeNullOrWhiteSpace();
        result.SecurityTemplatePath.Should().NotBeNullOrWhiteSpace();

        // Verify ADMX was staged
        Directory.Exists(Path.Combine(applyRoot, "ADMX Templates")).Should().BeTrue();
        // Verify .pol was staged
        File.Exists(Path.Combine(applyRoot, "GPO", "Machine", "Registry.pol")).Should().BeTrue();
        // Verify GptTmpl.inf was staged
        File.Exists(Path.Combine(applyRoot, "GPO", "SecurityTemplate", "GptTmpl.inf")).Should().BeTrue();
    }

    [Fact]
    public void StageForApply_NoMatchingOs_ReturnsNoArtifacts()
    {
        var root = CreateGpoPackageStructure(new[] { "Windows 11" });
        var applyRoot = Path.Combine(_tempDir, "apply");

        var result = GpoPackageExtractor.StageForApply(root, applyRoot, OsTarget.Server2019);

        result.HasAnyArtifacts.Should().BeFalse();
    }

    private string CreateGpoPackageStructure(string[] osNames)
    {
        var root = Path.Combine(_tempDir, "gpo-package");
        Directory.CreateDirectory(root);

        foreach (var osName in osNames)
        {
            // ADMX Templates/{OS}/test.admx
            var admxDir = Path.Combine(root, "ADMX Templates", osName);
            Directory.CreateDirectory(admxDir);
            File.WriteAllText(Path.Combine(admxDir, "test.admx"),
                "<policyDefinitions><policyNamespaces><target namespace=\"test.ns\"/></policyNamespaces><policies><policy name=\"TestPolicy\" displayName=\"Test\" key=\"SOFTWARE\\Test\" valueName=\"TestVal\"/></policies></policyDefinitions>");

            // .Support Files/Local Policies/{OS}/DomainSysvol/GPO/Machine/
            var policyDir = Path.Combine(root, ".Support Files", "Local Policies", osName, "DomainSysvol", "GPO", "Machine");
            Directory.CreateDirectory(policyDir);

            // Write a minimal valid .pol file
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write((uint)0x67655250); // PReg
                writer.Write((uint)1);           // Version
                File.WriteAllBytes(Path.Combine(policyDir, "Registry.pol"), ms.ToArray());
            }

            // Write a minimal GptTmpl.inf
            var secEditDir = Path.Combine(policyDir, "microsoft", "windows nt", "secedit");
            Directory.CreateDirectory(secEditDir);
            File.WriteAllText(Path.Combine(secEditDir, "GptTmpl.inf"),
                "[System Access]\nMinimumPasswordLength = 14\n");
        }

        return root;
    }
}
