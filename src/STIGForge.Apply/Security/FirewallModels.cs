namespace STIGForge.Apply.Security;

public sealed class FirewallConfig
{
    public bool EnableAllProfiles { get; set; } = true;
    public string DefaultInboundAction { get; set; } = "Block";
    public string DefaultOutboundAction { get; set; } = "Allow";
    public IReadOnlyList<FirewallRuleDefinition> RequiredRules { get; set; } = Array.Empty<FirewallRuleDefinition>();
}

public sealed class FirewallRuleDefinition
{
    public string DisplayName { get; set; } = string.Empty;
    public string Direction { get; set; } = "Inbound";
    public string Action { get; set; } = "Block";
    public string? Protocol { get; set; }
    public string? LocalPort { get; set; }
    public string? RemoteAddress { get; set; }
    public string? Description { get; set; }
}
