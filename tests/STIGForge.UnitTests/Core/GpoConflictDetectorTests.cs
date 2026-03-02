using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using STIGForge.Core.Abstractions;
using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.UnitTests.Core;

public sealed class GpoConflictDetectorTests
{
  [Fact]
  public async Task DetectConflicts_ValueMismatch_ReturnsConflict()
  {
    var bundleRoot = CreateBundleWithControls(new[]
    {
      new ControlRecord
      {
        Title = @"Registry Policy: HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableSmartScreen = 1"
      }
    });

    try
    {
      var processRunner = new TestProcessRunner(
        """
<Rsop>
  <Policy path="HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\\EnableSmartScreen" value="0" gpoName="DomainBaseline" />
</Rsop>
""");

      var detector = new GpoConflictDetector(processRunner);
      var conflicts = await detector.DetectConflictsAsync(bundleRoot, CancellationToken.None);

      conflicts.Should().HaveCount(1);
      conflicts[0].SettingPath.Should().Be(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableSmartScreen");
      conflicts[0].LocalValue.Should().Be("1");
      conflicts[0].GpoValue.Should().Be("0");
      conflicts[0].GpoName.Should().Be("DomainBaseline");
      conflicts[0].ConflictType.Should().Be("ValueOverride");
    }
    finally
    {
      try { Directory.Delete(bundleRoot, true); } catch { }
    }
  }

  [Fact]
  public async Task DetectConflicts_MatchingValues_ReturnsEmpty()
  {
    var bundleRoot = CreateBundleWithControls(new[]
    {
      new ControlRecord
      {
        Title = @"Registry Policy: HKLM\SOFTWARE\Policies\Microsoft\Windows\System\EnableSmartScreen = 1"
      }
    });

    try
    {
      var processRunner = new TestProcessRunner(
        """
<Rsop>
  <Policy path="HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System\\EnableSmartScreen" value="1" gpoName="DomainBaseline" />
</Rsop>
""");

      var detector = new GpoConflictDetector(processRunner);
      var conflicts = await detector.DetectConflictsAsync(bundleRoot, CancellationToken.None);

      conflicts.Should().BeEmpty();
    }
    finally
    {
      try { Directory.Delete(bundleRoot, true); } catch { }
    }
  }

  [Fact]
  public async Task DetectConflicts_SecuritySettingCheckText_IsParsed()
  {
    var bundleRoot = CreateBundleWithControls(new[]
    {
      new ControlRecord
      {
        CheckText = "Verify the security setting 'MinimumPasswordLength' is configured to '14' in Local Security Policy > Account Policies."
      }
    });

    try
    {
      var processRunner = new TestProcessRunner(
        """
<Rsop>
  <Setting name="MinimumPasswordLength" value="12" gpo="DomainPasswordPolicy" />
</Rsop>
""");

      var detector = new GpoConflictDetector(processRunner);
      var conflicts = await detector.DetectConflictsAsync(bundleRoot, CancellationToken.None);

      conflicts.Should().ContainSingle();
      conflicts[0].SettingPath.Should().Be("MinimumPasswordLength");
      conflicts[0].LocalValue.Should().Be("14");
      conflicts[0].GpoValue.Should().Be("12");
    }
    finally
    {
      try { Directory.Delete(bundleRoot, true); } catch { }
    }
  }

  private static string CreateBundleWithControls(IReadOnlyList<ControlRecord> controls)
  {
    var bundleRoot = Path.Combine(Path.GetTempPath(), "stigforge-gpo-conflicts-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(bundleRoot, "Manifest"));

    var json = JsonSerializer.Serialize(controls, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(Path.Combine(bundleRoot, "Manifest", "pack_controls.json"), json);

    return bundleRoot;
  }

  private sealed class TestProcessRunner : IProcessRunner
  {
    private readonly string _rsopXml;

    public TestProcessRunner(string rsopXml)
    {
      _rsopXml = rsopXml;
    }

    public Task<ProcessResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
      if (string.Equals(startInfo.FileName, "gpresult.exe", StringComparison.OrdinalIgnoreCase)
          && startInfo.Arguments.StartsWith("/x ", StringComparison.OrdinalIgnoreCase))
      {
        var path = ExtractRsopPath(startInfo.Arguments);
        if (!string.IsNullOrWhiteSpace(path))
          File.WriteAllText(path, _rsopXml);

        return Task.FromResult(new ProcessResult
        {
          ExitCode = 0,
          StandardOutput = "RSOP generated"
        });
      }

      return Task.FromResult(new ProcessResult
      {
        ExitCode = 0,
        StandardOutput = "gpresult fallback"
      });
    }

    public bool ExistsInPath(string fileName)
    {
      return true;
    }

    private static string ExtractRsopPath(string arguments)
    {
      var firstQuote = arguments.IndexOf('"');
      if (firstQuote < 0)
        return string.Empty;

      var secondQuote = arguments.IndexOf('"', firstQuote + 1);
      if (secondQuote <= firstQuote)
        return string.Empty;

      return arguments.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
    }
  }
}
