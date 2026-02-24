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
    return ValidateRequiredTools(null);
  }

  public string ValidateRequiredTools(string? evaluateStigToolRoot)
  {
    var candidates = ResolveCandidates(evaluateStigToolRoot);

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
      $"Expected Evaluate-STIG.ps1 under {string.Join(" or ", candidates.Select(static c => $"'{c}'"))}.");
  }

  private IReadOnlyList<string> ResolveCandidates(string? evaluateStigToolRoot)
  {
    if (!string.IsNullOrWhiteSpace(evaluateStigToolRoot))
      return new[] { evaluateStigToolRoot };

    var toolsRoot = _paths.GetToolsRoot();
    return new[]
    {
      Path.Combine(toolsRoot, "Evaluate-STIG", "Evaluate-STIG"),
      Path.Combine(toolsRoot, "Evaluate-STIG")
    };
  }
}
