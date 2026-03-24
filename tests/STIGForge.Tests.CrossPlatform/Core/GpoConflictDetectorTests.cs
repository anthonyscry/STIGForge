using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;
using STIGForge.Tests.CrossPlatform.Helpers;

namespace STIGForge.Tests.CrossPlatform.Core;

/// <summary>
/// Tests for GpoConflictDetector  -  focuses on path matching strategies not covered by existing tests:
/// hive-stripped paths, compact normalization, multiple targets, and case-insensitive matching.
/// All tests exercise ParseRsopXml indirectly through DetectConflictsAsync.
/// </summary>
public sealed class GpoConflictDetectorTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string CreateBundleWithControls(string dir, IReadOnlyList<ControlRecord> controls)
    {
        Directory.CreateDirectory(Path.Combine(dir, "Manifest"));
        var json = JsonSerializer.Serialize(controls, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, "Manifest", "pack_controls.json"), json);
        return dir;
    }

    private sealed class TestProcessRunner : STIGForge.Core.Abstractions.IProcessRunner
    {
        private readonly string _rsopXml;
        public TestProcessRunner(string rsopXml) => _rsopXml = rsopXml;

        public Task<STIGForge.Core.Abstractions.ProcessResult> RunAsync(
            System.Diagnostics.ProcessStartInfo startInfo, CancellationToken ct)
        {
            if (string.Equals(startInfo.FileName, "gpresult.exe", StringComparison.OrdinalIgnoreCase)
                && startInfo.Arguments.StartsWith("/x ", StringComparison.OrdinalIgnoreCase))
            {
                var path = ExtractPath(startInfo.Arguments);
                if (!string.IsNullOrWhiteSpace(path))
                    File.WriteAllText(path, _rsopXml);
            }
            return Task.FromResult(new STIGForge.Core.Abstractions.ProcessResult { ExitCode = 0, StandardOutput = string.Empty });
        }

        public bool ExistsInPath(string fileName) => true;

        public Task<STIGForge.Core.Abstractions.ProcessResult> RunWithTimeoutAsync(
            System.Diagnostics.ProcessStartInfo startInfo, TimeSpan timeout, CancellationToken ct)
            => RunAsync(startInfo, ct);

        private static string ExtractPath(string args)
        {
            var first = args.IndexOf('"');
            if (first < 0) return string.Empty;
            var second = args.IndexOf('"', first + 1);
            if (second <= first) return string.Empty;
            return args.Substring(first + 1, second - first - 1);
        }
    }

    // ── Hive-stripped path matching ────────────────────────────────────────────

    [Fact]
    public async Task ParseRsopXml_HiveStrippedPath_MatchesTarget()
    {
        // Control title uses HKLM\ prefix; RSOP XML omits the hive
        using var tmp = new TempDirectory();
        var controls = new[]
        {
            new ControlRecord { Title = @"Registry Policy: HKLM\Software\Policies\Foo = 1" }
        };
        CreateBundleWithControls(tmp.Path, controls);

        var rsop = """
<Rsop>
  <Policy path="Software\Policies\Foo" value="0" gpoName="BaselineGPO" />
</Rsop>
""";
        var detector = new GpoConflictDetector(new TestProcessRunner(rsop));

        var conflicts = await detector.DetectConflictsAsync(tmp.Path, CancellationToken.None);

        conflicts.Should().ContainSingle(because: "hive-stripped RSoP path should match HKLM\\-prefixed target");
        conflicts[0].GpoValue.Should().Be("0");
        conflicts[0].LocalValue.Should().Be("1");
    }

    // ── Compact normalization (separator differences) ──────────────────────────

    [Fact]
    public async Task ParseRsopXml_CompactNormalized_MatchesTarget()
    {
        // RSOP path uses forward slashes; control uses backslashes
        using var tmp = new TempDirectory();
        var controls = new[]
        {
            new ControlRecord { Title = @"Registry Policy: HKLM\Software\Policies\Bar = enabled" }
        };
        CreateBundleWithControls(tmp.Path, controls);

        var rsop = """
<Rsop>
  <Policy path="Software/Policies/Bar" value="disabled" gpoName="CorpGPO" />
</Rsop>
""";
        var detector = new GpoConflictDetector(new TestProcessRunner(rsop));

        var conflicts = await detector.DetectConflictsAsync(tmp.Path, CancellationToken.None);

        conflicts.Should().ContainSingle(because: "forward-slash RSoP path should be normalized to match backslash target");
    }

    // ── Multiple targets matched ───────────────────────────────────────────────

    [Fact]
    public async Task ParseRsopXml_MultipleTargets_AllMatched()
    {
        using var tmp = new TempDirectory();
        var controls = new[]
        {
            new ControlRecord { Title = @"Registry Policy: HKLM\Software\Policies\Alpha = 1" },
            new ControlRecord { Title = @"Registry Policy: HKLM\Software\Policies\Beta = 2" }
        };
        CreateBundleWithControls(tmp.Path, controls);

        var rsop = """
<Rsop>
  <Policy path="Software\Policies\Alpha" value="0" gpoName="GPO-A" />
  <Policy path="Software\Policies\Beta" value="0" gpoName="GPO-B" />
</Rsop>
""";
        var detector = new GpoConflictDetector(new TestProcessRunner(rsop));

        var conflicts = await detector.DetectConflictsAsync(tmp.Path, CancellationToken.None);

        conflicts.Should().HaveCount(2, because: "both target paths conflict with GPO settings");
    }

    // ── No matching targets ────────────────────────────────────────────────────

    [Fact]
    public async Task ParseRsopXml_NoMatchingTargets_ReturnsEmpty()
    {
        using var tmp = new TempDirectory();
        var controls = new[]
        {
            new ControlRecord { Title = @"Registry Policy: HKLM\Software\Policies\Unrelated = 1" }
        };
        CreateBundleWithControls(tmp.Path, controls);

        var rsop = """
<Rsop>
  <Policy path="Software\Different\Path" value="99" gpoName="SomeGPO" />
</Rsop>
""";
        var detector = new GpoConflictDetector(new TestProcessRunner(rsop));

        var conflicts = await detector.DetectConflictsAsync(tmp.Path, CancellationToken.None);

        conflicts.Should().BeEmpty(because: "RSoP path does not correspond to any local STIG target");
    }

    // ── Case-insensitive path matching ─────────────────────────────────────────

    [Fact]
    public async Task ParseRsopXml_CaseInsensitivePathMatch()
    {
        using var tmp = new TempDirectory();
        // Control uses mixed-case; RSOP uses all-lower
        var controls = new[]
        {
            new ControlRecord { Title = @"Registry Policy: HKLM\SOFTWARE\Policies\EnableFeature = 1" }
        };
        CreateBundleWithControls(tmp.Path, controls);

        var rsop = """
<Rsop>
  <Policy path="software\policies\enablefeature" value="0" gpoName="FeatureGPO" />
</Rsop>
""";
        var detector = new GpoConflictDetector(new TestProcessRunner(rsop));

        var conflicts = await detector.DetectConflictsAsync(tmp.Path, CancellationToken.None);

        conflicts.Should().ContainSingle(because: "path matching must be case-insensitive");
    }
}
