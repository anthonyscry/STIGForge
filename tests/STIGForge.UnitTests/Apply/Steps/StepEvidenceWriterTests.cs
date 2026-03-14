using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using STIGForge.Apply;
using STIGForge.Apply.Steps;
using STIGForge.Evidence;

namespace STIGForge.UnitTests.Apply.Steps;

public sealed class StepEvidenceWriterTests : IDisposable
{
    private readonly string _bundleRoot;

    public StepEvidenceWriterTests()
    {
        _bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-evidence-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_bundleRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_bundleRoot, true); } catch { }
    }

    // ── Write ────────────────────────────────────────────────────────────────

    [Fact]
    public void Write_WhenCollectorIsNull_ReturnsOutcomeUnchanged()
    {
        var writer = new StepEvidenceWriter(NullLogger<ApplyRunner>.Instance, null);
        var outcome = MakeOutcome("step1", 0);

        var result = writer.Write(outcome, _bundleRoot, "run-1", new Dictionary<string, string>());

        result.Should().BeSameAs(outcome);
        result.EvidenceMetadataPath.Should().BeNull();
    }

    [Fact]
    public void Write_WithValidArtifactFile_SetsEvidenceMetadataPath()
    {
        var artifactFile = Path.Combine(_bundleRoot, "out.log");
        File.WriteAllText(artifactFile, "some output");

        var collector = new EvidenceCollector();
        var writer = new StepEvidenceWriter(NullLogger<ApplyRunner>.Instance, collector);
        var outcome = MakeOutcome("step-artifact", 0, stdOutPath: artifactFile);

        var result = writer.Write(outcome, _bundleRoot, "run-42", new Dictionary<string, string>());

        result.EvidenceMetadataPath.Should().NotBeNullOrWhiteSpace();
        result.ArtifactSha256.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Write_NoStdOutPath_UsesTextContentFallback()
    {
        var collector = new EvidenceCollector();
        var writer = new StepEvidenceWriter(NullLogger<ApplyRunner>.Instance, collector);
        var outcome = MakeOutcome("step-nopath", 0, stdOutPath: string.Empty);

        var result = writer.Write(outcome, _bundleRoot, "run-99", new Dictionary<string, string>());

        result.EvidenceMetadataPath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Write_StdOutPathDoesNotExist_UsesTextContentFallback()
    {
        var collector = new EvidenceCollector();
        var writer = new StepEvidenceWriter(NullLogger<ApplyRunner>.Instance, collector);
        var outcome = MakeOutcome("step-missing-file", 0, stdOutPath: @"C:\does\not\exist.log");

        var result = writer.Write(outcome, _bundleRoot, "run-99", new Dictionary<string, string>());

        // Should not throw; uses fallback text
        result.EvidenceMetadataPath.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Write_MatchingPriorSha_SetsContinuityMarkerRetained()
    {
        var artifactFile = Path.Combine(_bundleRoot, "stable.log");
        File.WriteAllText(artifactFile, "stable content");

        // Compute the same SHA the writer will compute
        using var stream = File.OpenRead(artifactFile);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        var sha = Convert.ToHexString(hash).ToLowerInvariant();

        var priorSha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["step-retain"] = sha
        };

        var collector = new EvidenceCollector();
        var writer = new StepEvidenceWriter(NullLogger<ApplyRunner>.Instance, collector);
        var outcome = MakeOutcome("step-retain", 0, stdOutPath: artifactFile);

        var result = writer.Write(outcome, _bundleRoot, "run-r", priorSha);

        result.ContinuityMarker.Should().Be("retained");
    }

    [Fact]
    public void Write_DifferentPriorSha_SetsContinuityMarkerSuperseded()
    {
        var artifactFile = Path.Combine(_bundleRoot, "changed.log");
        File.WriteAllText(artifactFile, "new content");

        var priorSha = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["step-sup"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };

        var collector = new EvidenceCollector();
        var writer = new StepEvidenceWriter(NullLogger<ApplyRunner>.Instance, collector);
        var outcome = MakeOutcome("step-sup", 0, stdOutPath: artifactFile);

        var result = writer.Write(outcome, _bundleRoot, "run-s", priorSha);

        result.ContinuityMarker.Should().Be("superseded");
    }

    // ── LoadPriorRunStepSha256 ───────────────────────────────────────────────

    [Fact]
    public void LoadPriorRunStepSha256_NullPriorRunId_ReturnsEmpty()
    {
        var result = StepEvidenceWriter.LoadPriorRunStepSha256(_bundleRoot, null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void LoadPriorRunStepSha256_EmptyPriorRunId_ReturnsEmpty()
    {
        var result = StepEvidenceWriter.LoadPriorRunStepSha256(_bundleRoot, "   ");

        result.Should().BeEmpty();
    }

    [Fact]
    public void LoadPriorRunStepSha256_FileNotFound_ReturnsEmpty()
    {
        var result = StepEvidenceWriter.LoadPriorRunStepSha256(_bundleRoot, "run-missing");

        result.Should().BeEmpty();
    }

    [Fact]
    public void LoadPriorRunStepSha256_MismatchedRunId_ReturnsEmpty()
    {
        WriteRunJson(_bundleRoot, "run-real", new[]
        {
            new { StepName = "step1", ArtifactSha256 = "abc123" }
        });

        var result = StepEvidenceWriter.LoadPriorRunStepSha256(_bundleRoot, "run-different");

        result.Should().BeEmpty();
    }

    [Fact]
    public void LoadPriorRunStepSha256_ValidJson_ReturnsMapping()
    {
        WriteRunJson(_bundleRoot, "run-valid", new[]
        {
            new { StepName = "step-a", ArtifactSha256 = "deadbeef" },
            new { StepName = "step-b", ArtifactSha256 = "cafebabe" }
        });

        var result = StepEvidenceWriter.LoadPriorRunStepSha256(_bundleRoot, "run-valid");

        result.Should().ContainKey("step-a").WhoseValue.Should().Be("deadbeef");
        result.Should().ContainKey("step-b").WhoseValue.Should().Be("cafebabe");
    }

    [Fact]
    public void LoadPriorRunStepSha256_CorruptJson_ReturnsEmpty()
    {
        var applyDir = Path.Combine(_bundleRoot, "Apply");
        Directory.CreateDirectory(applyDir);
        File.WriteAllText(Path.Combine(applyDir, "apply_run.json"), "NOT VALID JSON {{{{");

        var result = StepEvidenceWriter.LoadPriorRunStepSha256(_bundleRoot, "run-x");

        result.Should().BeEmpty();
    }

    [Fact]
    public void LoadPriorRunStepSha256_StepWithNullSha_IsSkipped()
    {
        var applyDir = Path.Combine(_bundleRoot, "Apply");
        Directory.CreateDirectory(applyDir);
        var json = """{"runId":"run-n","steps":[{"StepName":"step1","ArtifactSha256":null}]}""";
        File.WriteAllText(Path.Combine(applyDir, "apply_run.json"), json);

        var result = StepEvidenceWriter.LoadPriorRunStepSha256(_bundleRoot, "run-n");

        result.Should().BeEmpty();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ApplyStepOutcome MakeOutcome(string stepName, int exitCode, string? stdOutPath = null)
        => new ApplyStepOutcome
        {
            StepName = stepName,
            ExitCode = exitCode,
            StdOutPath = stdOutPath ?? string.Empty
        };

    private static void WriteRunJson(string bundleRoot, string runId, IEnumerable<object> steps)
    {
        var applyDir = Path.Combine(bundleRoot, "Apply");
        Directory.CreateDirectory(applyDir);
        var obj = new { runId, steps };
        var json = JsonSerializer.Serialize(obj);
        File.WriteAllText(Path.Combine(applyDir, "apply_run.json"), json);
    }
}
