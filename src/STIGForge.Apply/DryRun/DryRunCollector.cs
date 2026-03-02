using STIGForge.Apply;

namespace STIGForge.Apply.DryRun;

/// <summary>
/// Collects proposed changes during a dry-run apply simulation and builds a structured report.
/// </summary>
public sealed class DryRunCollector
{
    private readonly List<DryRunChange> _changes = new();
    private readonly List<string> _diagnostics = new();

    public void Add(string stepName, string description, string? currentValue, string? proposedValue, string? ruleId = null, string? resourceType = null, string? resourcePath = null)
    {
        _changes.Add(new DryRunChange
        {
            StepName = stepName,
            Description = description,
            CurrentValue = currentValue,
            ProposedValue = proposedValue,
            RuleId = ruleId,
            ResourceType = resourceType,
            ResourcePath = resourcePath
        });
    }

    public void AddDiagnostic(string message)
    {
        _diagnostics.Add(message);
    }

    public void AddRange(string stepName, IEnumerable<DryRunChange> changes)
    {
        foreach (var change in changes)
        {
            change.StepName = stepName;
            _changes.Add(change);
        }
    }

    public DryRunReport Build(string bundleRoot, string mode)
    {
        return new DryRunReport
        {
            BundleRoot = bundleRoot,
            Mode = mode,
            GeneratedAt = DateTimeOffset.UtcNow,
            TotalChanges = _changes.Count,
            Changes = _changes.ToList(),
            Diagnostics = _diagnostics.ToList()
        };
    }

    public int Count => _changes.Count;
}
