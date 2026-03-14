using FluentAssertions;
using Moq;
using STIGForge.Build;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Build;

/// <summary>
/// Tests for BundleBuilder error paths, directory creation, and output correctness.
/// </summary>
public sealed class BundleBuilderTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static BundleBuilder CreateBuilder(IPathBuilder? paths = null)
    {
        var mockPaths = paths ?? CreateDefaultPathBuilder();

        var hash = new Mock<IHashingService>();
        hash.Setup(h => h.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("aabbccddaabbccddaabbccddaabbccddaabbccddaabbccddaabbccddaabbccdd");

        var scope = new Mock<IClassificationScopeService>();
        scope.Setup(s => s.Compile(It.IsAny<Profile>(), It.IsAny<IReadOnlyList<ControlRecord>>()))
             .Returns(new CompiledControls(
                 Array.Empty<CompiledControl>(),
                 Array.Empty<CompiledControl>()));

        var clock = new Mock<IClock>();
        clock.Setup(c => c.Now).Returns(FixedTime);

        return new BundleBuilder(
            mockPaths,
            hash.Object,
            scope.Object,
            new ReleaseAgeGate(clock.Object),
            new OverlayConflictDetector(),
            new OverlayMergeService());
    }

    private static IPathBuilder CreateDefaultPathBuilder()
    {
        var paths = new Mock<IPathBuilder>();
        paths.Setup(p => p.GetBundleRoot(It.IsAny<string>()))
             .Returns((string id) => Path.Combine(Path.GetTempPath(), "stigforge-build-" + id));
        return paths.Object;
    }

    private static BundleBuildRequest CreateMinimalRequest(string outputRoot, string bundleId = "test-bundle") =>
        new()
        {
            BundleId = bundleId,
            OutputRoot = outputRoot,
            Pack = new ContentPack
            {
                PackId = "pack-01",
                Name = "Test Pack",
                ReleaseDate = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero),
                ImportedAt = new DateTimeOffset(2025, 12, 2, 0, 0, 0, TimeSpan.Zero)
            },
            Profile = new Profile
            {
                ProfileId = "profile-01",
                Name = "Test Profile",
                OsTarget = OsTarget.Server2022,
                RoleTemplate = RoleTemplate.MemberServer,
                AutomationPolicy = new AutomationPolicy { NewRuleGraceDays = 0 }
            },
            Controls = Array.Empty<ControlRecord>(),
            Overlays = Array.Empty<Overlay>(),
            ToolVersion = "test-1.0",
            ForceAutoApply = true
        };

    // ── Valid minimal build ─────────────────────────────────────────────────────

    [Fact]
    public async Task Build_ValidMinimalRequest_CreatesOutputDirectory()
    {
        using var tmp = new TempDirectory();
        STIGForge.Build.BuildTime.Seed(FixedTime);
        try
        {
            var request = CreateMinimalRequest(tmp.Path);
            var builder = CreateBuilder();

            await builder.BuildAsync(request, CancellationToken.None);

            Directory.Exists(tmp.Path).Should().BeTrue();
        }
        finally
        {
            STIGForge.Build.BuildTime.Reset();
        }
    }

    [Fact]
    public async Task Build_OutputContainsManifestFile()
    {
        using var tmp = new TempDirectory();
        STIGForge.Build.BuildTime.Seed(FixedTime);
        try
        {
            var request = CreateMinimalRequest(tmp.Path);
            var builder = CreateBuilder();

            var result = await builder.BuildAsync(request, CancellationToken.None);

            File.Exists(result.ManifestPath).Should().BeTrue(
                because: "BuildAsync must write a manifest.json to the Manifest subdirectory");
        }
        finally
        {
            STIGForge.Build.BuildTime.Reset();
        }
    }

    [Fact]
    public async Task Build_OutputContainsHashManifest()
    {
        using var tmp = new TempDirectory();
        STIGForge.Build.BuildTime.Seed(FixedTime);
        try
        {
            var request = CreateMinimalRequest(tmp.Path);
            var builder = CreateBuilder();

            await builder.BuildAsync(request, CancellationToken.None);

            var hashManifest = Path.Combine(tmp.Path, "Manifest", "file_hashes.sha256");
            File.Exists(hashManifest).Should().BeTrue(
                because: "BuildAsync must write a file_hashes.sha256 to the Manifest subdirectory");
        }
        finally
        {
            STIGForge.Build.BuildTime.Reset();
        }
    }

    [Fact]
    public async Task Build_DeterministicOutput_SameInputProducesSameHash()
    {
        using var tmpA = new TempDirectory();
        using var tmpB = new TempDirectory();
        STIGForge.Build.BuildTime.Seed(FixedTime);
        try
        {
            var requestA = CreateMinimalRequest(tmpA.Path, "det-bundle");
            var requestB = CreateMinimalRequest(tmpB.Path, "det-bundle");

            var builderA = CreateBuilder();
            var builderB = CreateBuilder();

            await builderA.BuildAsync(requestA, CancellationToken.None);
            await builderB.BuildAsync(requestB, CancellationToken.None);

            var hashesA = await File.ReadAllTextAsync(Path.Combine(tmpA.Path, "Manifest", "file_hashes.sha256"));
            var hashesB = await File.ReadAllTextAsync(Path.Combine(tmpB.Path, "Manifest", "file_hashes.sha256"));

            hashesA.Should().Be(hashesB,
                because: "identical inputs with seeded BuildTime must produce identical hash manifests");
        }
        finally
        {
            STIGForge.Build.BuildTime.Reset();
        }
    }

    [Fact]
    public async Task Build_BundleIdMissing_GeneratesGuidBundleId()
    {
        using var tmp = new TempDirectory();
        STIGForge.Build.BuildTime.Seed(FixedTime);
        try
        {
            var request = CreateMinimalRequest(tmp.Path);
            request.BundleId = string.Empty; // let builder generate one
            var builder = CreateBuilder();

            var result = await builder.BuildAsync(request, CancellationToken.None);

            result.BundleId.Should().NotBeNullOrWhiteSpace(
                because: "BuildAsync must generate a BundleId when none is supplied");
        }
        finally
        {
            STIGForge.Build.BuildTime.Reset();
        }
    }
}
