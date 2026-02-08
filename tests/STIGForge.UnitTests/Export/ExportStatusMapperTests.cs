using FluentAssertions;
using STIGForge.Export;
using STIGForge.Verify;

namespace STIGForge.UnitTests.Export;

public sealed class ExportStatusMapperTests
{
  [Theory]
  [InlineData("NotAFinding", VerifyStatus.Pass)]
  [InlineData("pass", VerifyStatus.Pass)]
  [InlineData("OPEN", VerifyStatus.Fail)]
  [InlineData("fail", VerifyStatus.Fail)]
  [InlineData("Not_Applicable", VerifyStatus.NotApplicable)]
  [InlineData("na", VerifyStatus.NotApplicable)]
  [InlineData("Not Reviewed", VerifyStatus.NotReviewed)]
  [InlineData("error", VerifyStatus.Error)]
  [InlineData("informational", VerifyStatus.Informational)]
  [InlineData("", VerifyStatus.NotReviewed)]
  public void MapToVerifyStatus_HandlesCommonVariants(string input, VerifyStatus expected)
  {
    ExportStatusMapper.MapToVerifyStatus(input).Should().Be(expected);
  }

  [Theory]
  [InlineData("NotAFinding", "NotAFinding")]
  [InlineData("Open", "Open")]
  [InlineData("Fail", "Open")]
  [InlineData("Not_Applicable", "Not_Applicable")]
  [InlineData("Not_Reviewed", "Not_Reviewed")]
  [InlineData("error", "Open")]
  [InlineData("unknown", "Not_Reviewed")]
  public void MapToCklStatus_MapsNormalizedStatuses(string input, string expected)
  {
    ExportStatusMapper.MapToCklStatus(input).Should().Be(expected);
  }

  [Fact]
  public void MapToIndexStatus_AppliesExpectedPrecedence()
  {
    ExportStatusMapper.MapToIndexStatus(new[] { "NotAFinding", "Not_Applicable" }).Should().Be("NA");
    ExportStatusMapper.MapToIndexStatus(new[] { "NotAFinding", "Not_Reviewed" }).Should().Be("Open");
    ExportStatusMapper.MapToIndexStatus(new[] { "NotAFinding", "Open" }).Should().Be("Fail");
  }

  [Theory]
  [InlineData("Open", true)]
  [InlineData("Fail", true)]
  [InlineData("Not_Reviewed", true)]
  [InlineData("unknown", true)]
  [InlineData("NotAFinding", false)]
  [InlineData("Not_Applicable", false)]
  public void IsOpenStatus_MapsExpectedOpenState(string status, bool expected)
  {
    ExportStatusMapper.IsOpenStatus(status).Should().Be(expected);
  }
}
