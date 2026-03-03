using System.Text.RegularExpressions;
using STIGForge.Apply;

namespace STIGForge.Apply.DryRun;

/// <summary>
/// Parses DSC Start-DscConfiguration -WhatIf output into structured DryRunChange entries.
/// WhatIf output format: "What if: [ResourceName]ResourceType\Instance: message"
/// </summary>
public static class DscWhatIfParser
{
    private static readonly Regex WhatIfPattern = new(
        @"What if:\s*\[(?<resourceType>[^\]]+)\](?<instance>[^:]+):\s*(?<message>.+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PropertyChangePattern = new(
        @"^\s+(?<property>\w+):\s*(?<current>.*?)\s*->\s*(?<desired>.*?)\s*$",
        RegexOptions.Compiled);

    public static IReadOnlyList<DryRunChange> Parse(string? whatIfOutput)
    {
        if (string.IsNullOrWhiteSpace(whatIfOutput))
            return [];

        var changes = new List<DryRunChange>();
        var lines = whatIfOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        string? currentResourceType = null;
        string? currentInstance = null;
        string? currentMessage = null;

        foreach (var line in lines)
        {
            var whatIfMatch = WhatIfPattern.Match(line);
            if (whatIfMatch.Success)
            {
                if (currentResourceType != null && currentMessage != null)
                {
                    changes.Add(new DryRunChange
                    {
                        StepName = "DSC",
                        Description = currentMessage,
                        ResourceType = currentResourceType,
                        ResourcePath = currentInstance,
                        CurrentValue = null,
                        ProposedValue = null
                    });
                }

                currentResourceType = whatIfMatch.Groups["resourceType"].Value;
                currentInstance = whatIfMatch.Groups["instance"].Value;
                currentMessage = whatIfMatch.Groups["message"].Value;
                continue;
            }

            var propMatch = PropertyChangePattern.Match(line);
            if (propMatch.Success && currentResourceType != null)
            {
                changes.Add(new DryRunChange
                {
                    StepName = "DSC",
                    Description = $"{currentInstance}: {propMatch.Groups["property"].Value} change",
                    ResourceType = currentResourceType,
                    ResourcePath = currentInstance,
                    CurrentValue = propMatch.Groups["current"].Value.Trim(),
                    ProposedValue = propMatch.Groups["desired"].Value.Trim()
                });

                currentResourceType = null;
                currentMessage = null;
                continue;
            }
        }

        if (currentResourceType != null && currentMessage != null)
        {
            changes.Add(new DryRunChange
            {
                StepName = "DSC",
                Description = currentMessage,
                ResourceType = currentResourceType,
                ResourcePath = currentInstance,
                CurrentValue = null,
                ProposedValue = null
            });
        }

        return changes;
    }
}
