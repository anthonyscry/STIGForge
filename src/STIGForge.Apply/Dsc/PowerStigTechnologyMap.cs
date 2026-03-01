using STIGForge.Core.Models;

namespace STIGForge.Apply.Dsc;

/// <summary>
/// Maps STIGForge OsTarget values to PowerSTIG composite DSC resource names and parameters.
/// PowerSTIG uses composite resources (e.g., WindowsServer, WindowsClient) that each take
/// an OsVersion parameter (e.g., "2022", "11") to generate the correct MOF for the target OS.
/// </summary>
public static class PowerStigTechnologyMap
{
    /// <summary>
    /// Resolves the PowerSTIG composite resource name for the given OS target.
    /// Returns null if the OS is not supported by PowerSTIG.
    /// </summary>
    public static PowerStigTarget? Resolve(OsTarget osTarget, RoleTemplate role = RoleTemplate.Workstation)
    {
        return osTarget switch
        {
            OsTarget.Win11 => new PowerStigTarget("WindowsClient", "11", GetClientSkipRule(role)),
            OsTarget.Win10 => new PowerStigTarget("WindowsClient", "10", GetClientSkipRule(role)),
            OsTarget.Server2022 => new PowerStigTarget("WindowsServer", "2022", GetServerStigType(role)),
            OsTarget.Server2019 => new PowerStigTarget("WindowsServer", "2019", GetServerStigType(role)),
            _ => null
        };
    }

    /// <summary>
    /// Builds the PowerShell DSC configuration script block that uses the resolved
    /// PowerSTIG composite resource to generate MOFs for the target OS.
    /// </summary>
    public static string BuildDscConfigurationScript(PowerStigTarget target, string outputPath, string? stigDataFile = null)
    {
        var dataFileParam = string.IsNullOrWhiteSpace(stigDataFile)
            ? string.Empty
            : $"\r\n            StigData = '{EscapePsString(stigDataFile)}'";

        var stigTypeParam = string.IsNullOrWhiteSpace(target.StigType)
            ? string.Empty
            : $"\r\n            StigVersion = '{EscapePsString(target.StigType)}'";

        // WindowsFirewall is always applied alongside the primary OS STIG
        var firewallBlock = target.CompositeResourceName is "WindowsServer" or "WindowsClient"
            ? @"

        WindowsFirewall FirewallStig
        {
            StigVersion = $null
        }"
            : string.Empty;

        return $@"$ErrorActionPreference = 'Stop'
Configuration STIGForgeHarden
{{
    Import-DscResource -ModuleName PowerSTIG

    Node localhost
    {{
        {target.CompositeResourceName} OsStig
        {{
            OsVersion = '{EscapePsString(target.OsVersion)}'{stigTypeParam}{dataFileParam}
        }}{firewallBlock}
    }}
}}

STIGForgeHarden -OutputPath '{EscapePsString(outputPath)}'
";
    }

    private static string GetServerStigType(RoleTemplate role)
    {
        return role switch
        {
            RoleTemplate.DomainController => "DC",
            _ => "MS"
        };
    }

    private static string? GetClientSkipRule(RoleTemplate role)
    {
        // Client STIGs do not have a DC/MS distinction
        return null;
    }

    private static string EscapePsString(string value)
        => value.Replace("'", "''");
}

/// <summary>
/// Resolved PowerSTIG target containing the composite resource name, OS version,
/// and optional STIG type (e.g., "MS" or "DC" for server STIGs).
/// </summary>
public sealed class PowerStigTarget
{
    public PowerStigTarget(string compositeResourceName, string osVersion, string? stigType)
    {
        CompositeResourceName = compositeResourceName;
        OsVersion = osVersion;
        StigType = stigType;
    }

    /// <summary>PowerSTIG DSC composite resource name (e.g., "WindowsServer", "WindowsClient").</summary>
    public string CompositeResourceName { get; }

    /// <summary>OS version parameter value (e.g., "2022", "11").</summary>
    public string OsVersion { get; }

    /// <summary>Optional STIG type (e.g., "MS" for member server, "DC" for domain controller). Null for client STIGs.</summary>
    public string? StigType { get; }
}
