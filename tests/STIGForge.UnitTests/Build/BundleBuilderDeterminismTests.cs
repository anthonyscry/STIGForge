using System.Text.Json;
using FluentAssertions;
using Moq;
using STIGForge.Build;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Build;

/// <summary>
/// Verifies BLD-01 deterministic bundle output invariants:
/// identical inputs produce identical hash manifests, schema version is set,
/// and missing apply templates skip validation gracefully.
/// </summary>
public sealed class BundleBuilderDeterminismTests : IDisposable
{
    private readonly string _tempRoot;

    public BundleBuilderDeterminismTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-determinism-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        BuildTime.Reset();
        try { Directory.Delete(_tempRoot, true); } catch { }
    }

    [Fact]
    public async Task IdenticalInputs_ProduceIdenticalHashes()
    {
        // Arrange: seed deterministic clock so timestamps are identical
        var fixedTime = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        BuildTime.Seed(fixedTime);

        var outputA = Path.Combine(_tempRoot, "bundle-a");
        var outputB = Path.Combine(_tempRoot, "bundle-b");

        var request = CreateMinimalRequest();

        var builderA = CreateBuilder();
        var builderB = CreateBuilder();

        // Act
        request.OutputRoot = outputA;
        request.BundleId = "determinism-test";
        var resultA = await builderA.BuildAsync(request, CancellationToken.None);

        request.OutputRoot = outputB;
        request.BundleId = "determinism-test";
        var resultB = await builderB.BuildAsync(request, CancellationToken.None);

        // Assert: hash manifests must be content-identical (paths differ, but relative structure matches)
        var hashesA = File.ReadAllText(Path.Combine(outputA, "Manifest", "file_hashes.sha256"));
        var hashesB = File.ReadAllText(Path.Combine(outputB, "Manifest", "file_hashes.sha256"));

        hashesA.Should().Be(hashesB,
            because: "identical inputs with seeded BuildTime must produce identical hash manifests");
    }

    [Fact]
    public async Task SchemaVersion_IsSetToOne()
    {
        // Arrange
        BuildTime.Seed(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var output = Path.Combine(_tempRoot, "schema-test");
        var request = CreateMinimalRequest();
        request.OutputRoot = output;
        request.BundleId = "schema-test";

        var builder = CreateBuilder();

        // Act
        await builder.BuildAsync(request, CancellationToken.None);

        // Assert
        var manifestPath = Path.Combine(output, "Manifest", "manifest.json");
        File.Exists(manifestPath).Should().BeTrue();

        var manifest = JsonSerializer.Deserialize<BundleManifest>(File.ReadAllText(manifestPath));
        manifest.Should().NotBeNull();
        manifest!.SchemaVersion.Should().Be(1,
            because: "BLD-01 requires schema version to be set for forward compatibility");
    }

    [Fact]
    public async Task MissingApplyTemplates_SkipsValidation()
    {
        // Arrange: build in a temp dir that is NOT inside a git repo,
        // so FindRepoRoot returns null and CopyApplyTemplates returns false.
        BuildTime.Seed(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));
        var output = Path.Combine(_tempRoot, "no-templates-test");
        var request = CreateMinimalRequest();
        request.OutputRoot = output;
        request.BundleId = "no-templates";

        var builder = CreateBuilder();

        // Act & Assert: should NOT throw even though no apply templates exist
        var act = () => builder.BuildAsync(request, CancellationToken.None);
        await act.Should().NotThrowAsync(
            because: "when templates cannot be copied (no repo root), validation is skipped gracefully");

        // The Apply directory should still exist (created by BuildAsync)
        Directory.Exists(Path.Combine(output, "Apply")).Should().BeTrue();
    }

    private static BundleBuildRequest CreateMinimalRequest()
    {
        return new BundleBuildRequest
        {
            BundleId = "test-bundle",
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
                AutomationPolicy = new AutomationPolicy
                {
                    NewRuleGraceDays = 0
                }
            },
            Controls = Array.Empty<ControlRecord>(),
            Overlays = Array.Empty<Overlay>(),
            ToolVersion = "test-1.0",
            ForceAutoApply = true
        };
    }

    private BundleBuilder CreateBuilder()
    {
        var paths = new Mock<IPathBuilder>();
        paths.Setup(p => p.GetBundleRoot(It.IsAny<string>()))
             .Returns((string id) => Path.Combine(_tempRoot, "default-" + id));

        var hash = new Mock<IHashingService>();
        hash.Setup(h => h.Sha256FileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult("deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef"));

        var scope = new Mock<IClassificationScopeService>();
        scope.Setup(s => s.Compile(It.IsAny<Profile>(), It.IsAny<IReadOnlyList<ControlRecord>>()))
             .Returns(new CompiledControls(
                 Array.Empty<CompiledControl>(),
                 Array.Empty<CompiledControl>()));

        var clock = new Mock<IClock>();
        clock.Setup(c => c.Now).Returns(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

        var releaseGate = new ReleaseAgeGate(clock.Object);
        var conflictDetector = new OverlayConflictDetector();

        return new BundleBuilder(paths.Object, hash.Object, scope.Object, releaseGate, conflictDetector, new OverlayMergeService());
    }
}
