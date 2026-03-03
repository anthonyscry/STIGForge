namespace STIGForge.Apply.Security;

public sealed class WdacPolicyConfig
{
    public string? PolicyPath { get; set; }
    public string EnforcementMode { get; set; } = "Audit";
    public bool AllowMicrosoft { get; set; } = true;
    public bool AllowWindows { get; set; } = true;
    public IReadOnlyList<string> AllowedPublishers { get; set; } = [];
}
