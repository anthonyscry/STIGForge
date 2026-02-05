using STIGForge.Content.Diff;
using STIGForge.Core.Models;
using Xunit;

namespace STIGForge.UnitTests.Content;

public sealed class ReleaseDiffWriterTests
{
  [Fact]
  public void WriteCsv_WritesHeaderAndRow()
  {
    var diff = new ReleaseDiff
    {
      FromPackId = "A",
      ToPackId = "B",
      Items = new[]
      {
        new ControlDiff { RuleId = "SV-1", VulnId = "V-1", Title = "Title", Kind = DiffKind.Added, IsManual = true }
      }
    };

    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
    ReleaseDiffWriter.WriteCsv(path, diff);

    var text = File.ReadAllText(path);
    Assert.Contains("RuleId", text);
    Assert.Contains("SV-1", text);
  }
}
