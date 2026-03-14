using FluentAssertions;
using STIGForge.Content.Import;

namespace STIGForge.Tests.CrossPlatform.Content;

public sealed class FormatDetectorTests
{
    private readonly FormatDetector _detector = new();

    private static FormatDetectionResult Detect(
        int xccdf = 0,
        int oval = 0,
        int scapDataStream = 0,
        int admx = 0,
        int totalXml = 0)
    {
        var stats = new SourceArtifactStats
        {
            XccdfXmlCount = xccdf,
            OvalXmlCount = oval,
            ScapDataStreamXmlCount = scapDataStream,
            AdmxCount = admx,
            TotalXmlCount = totalXml
        };

        // extractedRoot is unused in DetectPackFormatWithConfidence
        return new FormatDetector().DetectPackFormatWithConfidence(string.Empty, stats);
    }

    [Fact]
    public void Detect_XccdfAndOval_ReturnsScapHighConfidence()
    {
        var result = Detect(xccdf: 1, oval: 1, totalXml: 2);

        result.Format.Should().Be(PackFormat.Scap);
        result.Confidence.Should().Be(DetectionConfidence.High);
    }

    [Fact]
    public void Detect_XccdfAndDataStream_ReturnsScapHighConfidence()
    {
        var result = Detect(xccdf: 1, scapDataStream: 1, totalXml: 2);

        result.Format.Should().Be(PackFormat.Scap);
        result.Confidence.Should().Be(DetectionConfidence.High);
    }

    [Fact]
    public void Detect_OnlyAdmx_ReturnsGpoHighConfidence()
    {
        var result = Detect(admx: 3);

        result.Format.Should().Be(PackFormat.Gpo);
        result.Confidence.Should().Be(DetectionConfidence.High);
    }

    [Fact]
    public void Detect_AdmxAndXccdf_ReturnsGpoMediumConfidence()
    {
        var result = Detect(xccdf: 1, admx: 2, totalXml: 3);

        result.Format.Should().Be(PackFormat.Gpo);
        result.Confidence.Should().Be(DetectionConfidence.Medium);
    }

    [Fact]
    public void Detect_OnlyXccdf_ReturnsStigHighConfidence()
    {
        var result = Detect(xccdf: 2, totalXml: 2);

        result.Format.Should().Be(PackFormat.Stig);
        result.Confidence.Should().Be(DetectionConfidence.High);
    }

    [Fact]
    public void Detect_Nothing_ReturnsUnknownLowConfidence()
    {
        var result = Detect();

        result.Format.Should().Be(PackFormat.Unknown);
        result.Confidence.Should().Be(DetectionConfidence.Low);
    }

    [Fact]
    public void Detect_Reasons_ArePopulated()
    {
        var result = Detect(xccdf: 1, oval: 1, totalXml: 2);

        result.Reasons.Should().NotBeEmpty();
        result.Reasons[0].Should().Contain("XCCDF");
    }
}
