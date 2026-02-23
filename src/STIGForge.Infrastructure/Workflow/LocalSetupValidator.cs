using STIGForge.Core.Abstractions;

namespace STIGForge.Infrastructure.Workflow;

public sealed class LocalSetupValidator
{
  private readonly IPathBuilder _paths;

  public LocalSetupValidator(IPathBuilder paths)
  {
    _paths = paths;
  }

  public string ValidateRequiredTools()
  {
    var toolsRoot = _paths.GetToolsRoot();
    var candidates = new[]
    {
      Path.Combine(toolsRoot, "Evaluate-STIG", "Evaluate-STIG"),
      Path.Combine(toolsRoot, "Evaluate-STIG")
    };

    foreach (var candidate in candidates)
    {
      if (!Directory.Exists(candidate))
        continue;

      var scriptPath = Path.Combine(candidate, "Evaluate-STIG.ps1");
      if (File.Exists(scriptPath))
        return candidate;
    }

    throw new InvalidOperationException(
      "Required Evaluate-STIG tool path is missing or invalid. " +
      $"Expected Evaluate-STIG.ps1 under '{candidates[0]}' or '{candidates[1]}'.");
  }
}
