using STIGForge.Verify;
using Xunit;

namespace STIGForge.UnitTests.Verify;

public class CklParserTests
{
    private static string GetFixturePath(string filename)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "..", "..", "..", "fixtures", filename);
    }

    [Fact]
    public void ParseFile_ExtractsRuleIdAndStatus()
    {
        var cklPath = GetFixturePath("sample.ckl");

        var results = CklParser.ParseFile(cklPath, "test-tool");

        Assert.Single(results);
        var result = results[0];
        Assert.Equal("SV-12345r1_rule", result.RuleId);
        Assert.Equal("Open", result.Status);
    }
}
