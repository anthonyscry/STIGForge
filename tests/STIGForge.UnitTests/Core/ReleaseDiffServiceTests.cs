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
}
