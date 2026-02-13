using STIGForge.Core.Constants;
using STIGForge.Verify;

namespace STIGForge.Export;

public static class ExportStatusMapper
{
  public static VerifyStatus MapToVerifyStatus(string? status)
  {
    if (string.IsNullOrWhiteSpace(status))
      return VerifyStatus.NotReviewed;

    var normalized = Normalize(status!);

    return normalized switch
    {
      "pass" => VerifyStatus.Pass,
      "notafinding" => VerifyStatus.Pass,
      "fail" => VerifyStatus.Fail,
      "open" => VerifyStatus.Fail,
      "notapplicable" => VerifyStatus.NotApplicable,
      "na" => VerifyStatus.NotApplicable,
      "notreviewed" => VerifyStatus.NotReviewed,
      "notchecked" => VerifyStatus.NotReviewed,
      "informational" => VerifyStatus.Informational,
      "error" => VerifyStatus.Error,
      "unknown" => VerifyStatus.Unknown,
      _ => VerifyStatus.NotReviewed
    };
  }

  public static string MapToCklStatus(string? status)
  {
    return MapToVerifyStatus(status) switch
    {
      VerifyStatus.Pass => ControlStatus.NotAFinding,
      VerifyStatus.NotApplicable => ControlStatus.NotApplicableAlt,
      VerifyStatus.Fail => ControlStatus.Open,
      VerifyStatus.Error => ControlStatus.Open,
      _ => ControlStatus.NotReviewed
    };
  }

  public static string MapToIndexStatus(IEnumerable<string?> statuses)
  {
    var mapped = statuses.Select(MapToVerifyStatus).ToList();

    if (mapped.Any(s => s == VerifyStatus.Fail || s == VerifyStatus.Error))
      return ControlStatus.Fail;

    if (mapped.Any(s => s == VerifyStatus.NotReviewed || s == VerifyStatus.Unknown))
      return ControlStatus.Open;

    if (mapped.Any(s => s == VerifyStatus.NotApplicable))
      return "NA";

    if (mapped.Any(s => s == VerifyStatus.Pass || s == VerifyStatus.Informational))
      return ControlStatus.Pass;

    return ControlStatus.Open;
  }

  public static bool IsOpenStatus(string? status)
  {
    var mapped = MapToVerifyStatus(status);
    return mapped == VerifyStatus.Fail
      || mapped == VerifyStatus.Error
      || mapped == VerifyStatus.NotReviewed
      || mapped == VerifyStatus.Unknown;
  }

  private static string Normalize(string value)
  {
    return value
      .Trim()
      .Replace("_", string.Empty)
      .Replace("-", string.Empty)
      .Replace(" ", string.Empty)
      .ToLowerInvariant();
  }
}
