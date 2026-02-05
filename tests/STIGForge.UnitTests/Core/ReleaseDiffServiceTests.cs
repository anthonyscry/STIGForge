using STIGForge.Core.Models;
using STIGForge.Core.Services;
using Xunit;

namespace STIGForge.UnitTests.Core;

public sealed class ReleaseDiffServiceTests
{
  [Fact]
  public void Diff_FindsAddedRemovedChangedAndManual()
  {
    var from = new List<ControlRecord>
    {
      new() { ExternalIds = new ExternalIds { RuleId = "SV-1" }, Title = "Old", CheckText = "A", IsManual = true },
      new() { ExternalIds = new ExternalIds { RuleId = "SV-2" }, Title = "Keep", CheckText = "B", IsManual = false }
    };
    var to = new List<ControlRecord>
    {
      new() { ExternalIds = new ExternalIds { RuleId = "SV-1" }, Title = "Old", CheckText = "A2", IsManual = true },
      new() { ExternalIds = new ExternalIds { RuleId = "SV-3" }, Title = "New", CheckText = "C", IsManual = false }
    };

    var diff = new ReleaseDiffService().Diff("packA", "packB", from, to);

    Assert.Contains(diff.Items, i => i.RuleId == "SV-3" && i.Kind == DiffKind.Added);
    Assert.Contains(diff.Items, i => i.RuleId == "SV-2" && i.Kind == DiffKind.Removed);
    Assert.Contains(diff.Items, i => i.RuleId == "SV-1" && i.Kind == DiffKind.Changed && i.ManualChanged);
  }

  [Fact]
  public void Diff_DoesNotDropDuplicateTitleFallbackKeys()
  {
    var from = new List<ControlRecord>
    {
      new() { ControlId = "C-1", Title = "Duplicate", ExternalIds = new ExternalIds() },
      new() { ControlId = string.Empty, Title = "Duplicate", ExternalIds = new ExternalIds() },
      new() { ControlId = string.Empty, Title = "Duplicate", ExternalIds = new ExternalIds() }
    };

    var diff = new ReleaseDiffService().Diff("packA", "packB", from, Array.Empty<ControlRecord>());

    Assert.Equal(3, diff.Items.Count);
    Assert.Contains(diff.Items, i => i.Key.Contains("C-1", StringComparison.OrdinalIgnoreCase));

    var titleOnlyKeys = diff.Items
      .Where(i => !i.Key.Contains("C-1", StringComparison.OrdinalIgnoreCase))
      .Select(i => i.Key)
      .ToList();

    Assert.Equal(2, titleOnlyKeys.Count);
    Assert.Equal(2, new HashSet<string>(titleOnlyKeys, StringComparer.OrdinalIgnoreCase).Count);
  }

  [Fact]
  public void Diff_UsesDeterministicKeysForDuplicateBaseKey()
  {
    var controlA = new ControlRecord
    {
      Title = "Duplicate",
      CheckText = "Check A",
      ExternalIds = new ExternalIds()
    };
    var controlB = new ControlRecord
    {
      Title = "Duplicate",
      CheckText = "Check B",
      ExternalIds = new ExternalIds()
    };

    var from = new List<ControlRecord> { controlA, controlB };
    var to = new List<ControlRecord> { controlB, controlA };

    var diff = new ReleaseDiffService().Diff("packA", "packB", from, to);

    Assert.All(diff.Items, item => Assert.Equal(DiffKind.Unchanged, item.Kind));
  }
}
