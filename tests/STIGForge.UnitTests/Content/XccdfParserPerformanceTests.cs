using System.Diagnostics;
using System.Text;
using FluentAssertions;
using STIGForge.Content.Import;

namespace STIGForge.UnitTests.Content;

public sealed class XccdfParserPerformanceTests : IDisposable
{
  private readonly string _tempRoot;

  public XccdfParserPerformanceTests()
  {
    _tempRoot = Path.Combine(Path.GetTempPath(), "stigforge-xccdf-perf-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_tempRoot);
  }

  public void Dispose()
  {
    try { Directory.Delete(_tempRoot, true); } catch { }
  }

  [Fact]
  public void Parse_LargeSyntheticBenchmark_CompletesWithinGuardrail()
  {
    var path = Path.Combine(_tempRoot, "large-xccdf.xml");
    const int ruleCount = 2000;
    File.WriteAllText(path, BuildXccdf(ruleCount), Encoding.UTF8);

    var sw = Stopwatch.StartNew();
    var controls = XccdfParser.Parse(path, "PerfPack");
    sw.Stop();

    controls.Should().HaveCount(ruleCount);
    sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
  }

  private static string BuildXccdf(int ruleCount)
  {
    var sb = new StringBuilder();
    sb.AppendLine("<Benchmark id=\"B-1\" xmlns=\"http://checklists.nist.gov/xccdf/1.2\">");
    sb.AppendLine("  <title>Perf Benchmark</title>");
    sb.AppendLine("  <version>1</version>");
    for (var i = 0; i < ruleCount; i++)
    {
      sb.AppendLine($"  <Rule id=\"SV-{i}_rule\" severity=\"medium\">");
      sb.AppendLine($"    <title>Rule {i}</title>");
      sb.AppendLine($"    <description>Description {i}</description>");
      sb.AppendLine($"    <check><check-content>Check {i}</check-content></check>");
      sb.AppendLine($"    <fixtext>Fix {i}</fixtext>");
      sb.AppendLine("  </Rule>");
    }
    sb.AppendLine("</Benchmark>");
    return sb.ToString();
  }
}
