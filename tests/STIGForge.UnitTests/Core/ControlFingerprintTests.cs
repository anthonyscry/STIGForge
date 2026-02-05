using STIGForge.Core.Models;
using STIGForge.Core.Services;
using Xunit;

namespace STIGForge.UnitTests.Core;

public sealed class ControlFingerprintTests
{
  [Fact]
  public void Compute_SameContent_SameHash()
  {
    var a = new ControlRecord
    {
      ExternalIds = new ExternalIds { RuleId = "SV-1", VulnId = "V-1" },
      Title = "Title",
      Severity = "high",
      Discussion = "Discussion",
      CheckText = "Check",
      FixText = "Fix",
      IsManual = true
    };
    var b = new ControlRecord
    {
      ExternalIds = new ExternalIds { RuleId = "SV-1", VulnId = "V-1" },
      Title = "Title",
      Severity = "high",
      Discussion = "Discussion",
      CheckText = "Check",
      FixText = "Fix",
      IsManual = true
    };

    var ha = ControlFingerprint.Compute(a);
    var hb = ControlFingerprint.Compute(b);

    Assert.Equal(ha, hb);
  }

  [Fact]
  public void Compute_ChangedContent_DifferentHash()
  {
    var a = new ControlRecord { Title = "Title", CheckText = "A" };
    var b = new ControlRecord { Title = "Title", CheckText = "B" };

    Assert.NotEqual(ControlFingerprint.Compute(a), ControlFingerprint.Compute(b));
  }

  [Fact]
  public void Compute_DelimiterContent_DifferentHash()
  {
    var a = new ControlRecord { Title = "A|B", Severity = "C" };
    var b = new ControlRecord { Title = "A", Severity = "B|C" };

    Assert.NotEqual(ControlFingerprint.Compute(a), ControlFingerprint.Compute(b));
  }
}
