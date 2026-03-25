using FluentAssertions;
using STIGForge.Evidence;
using Xunit;

namespace STIGForge.UnitTests.Evidence;

public class CommentTemplateEngineTests
{
    [Fact]
    public void Generate_PassStatus_ReturnsCompliantRationale()
    {
        var result = CommentTemplateEngine.Generate(
            status: "pass",
            keyEvidence: "Registry key EnableLUA is set to 1",
            toolName: "Evaluate-STIG",
            verifiedAt: new DateTimeOffset(2026, 3, 20, 14, 35, 0, TimeSpan.Zero),
            artifactFileNames: new[] { "registry_export.txt" });

        result.Should().Contain("Verified compliant");
        result.Should().Contain("EnableLUA");
        result.Should().Contain("Evaluate-STIG");
        result.Should().Contain("2026-03-20");
        result.Should().Contain("registry_export.txt");
    }

    [Fact]
    public void Generate_FailStatus_ReturnsOpenFindingRationale()
    {
        var result = CommentTemplateEngine.Generate(
            status: "fail",
            keyEvidence: "Registry key EnableLUA is set to 0",
            toolName: "SCAP",
            verifiedAt: new DateTimeOffset(2026, 3, 20, 14, 35, 0, TimeSpan.Zero),
            artifactFileNames: Array.Empty<string>());

        result.Should().Contain("Open finding");
        result.Should().Contain("EnableLUA");
    }

    [Fact]
    public void Generate_NotApplicable_ReturnsNaRationale()
    {
        var result = CommentTemplateEngine.Generate(
            status: "notapplicable",
            keyEvidence: null,
            toolName: "Manual CKL",
            verifiedAt: null,
            artifactFileNames: Array.Empty<string>());

        result.Should().Contain("Not applicable");
    }

    [Fact]
    public void Generate_NotReviewed_ReturnsAwaitingRationale()
    {
        var result = CommentTemplateEngine.Generate(
            status: "notreviewed",
            keyEvidence: null,
            toolName: null,
            verifiedAt: null,
            artifactFileNames: Array.Empty<string>());

        result.Should().Contain("Awaiting review");
    }

    [Fact]
    public void Generate_NullTool_OmitsToolReference()
    {
        var result = CommentTemplateEngine.Generate(
            status: "pass",
            keyEvidence: "Setting is configured",
            toolName: null,
            verifiedAt: null,
            artifactFileNames: Array.Empty<string>());

        result.Should().NotContain("verified by");
        result.Should().NotContain("Scan verified");
    }

    [Fact]
    public void Generate_MergedTool_HandlesGracefully()
    {
        var result = CommentTemplateEngine.Generate(
            status: "pass",
            keyEvidence: "Setting is configured",
            toolName: "Merged",
            verifiedAt: new DateTimeOffset(2026, 3, 20, 14, 35, 0, TimeSpan.Zero),
            artifactFileNames: Array.Empty<string>());

        result.Should().Contain("Verified compliant");
        result.Should().Contain("verified by multiple tools");
    }

    [Fact]
    public void ContainsSentinel_WithSentinel_ReturnsTrue()
    {
        var text = "some content\n\n--- STIGForge Evidence ---\nmore content";
        CommentTemplateEngine.ContainsSentinel(text).Should().BeTrue();
    }

    [Fact]
    public void ContainsSentinel_WithoutSentinel_ReturnsFalse()
    {
        CommentTemplateEngine.ContainsSentinel("just normal text").Should().BeFalse();
    }

    [Fact]
    public void ContainsSentinel_Null_ReturnsFalse()
    {
        CommentTemplateEngine.ContainsSentinel(null).Should().BeFalse();
    }
}
