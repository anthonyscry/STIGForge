using STIGForge.Core.Abstractions;

namespace STIGForge.Apply.Remediation;

/// <summary>
/// Registry of built-in remediation handlers for common STIG findings.
/// Starting with 10 representative rules across registry, service, and audit policy categories.
/// </summary>
public static class RemediationHandlerRegistry
{
    public static IReadOnlyList<IRemediationHandler> CreateHandlers(IProcessRunner? processRunner = null)
    {
        return new IRemediationHandler[]
        {
            new Handlers.RegistryRemediationHandler("SV-253265", @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreenSlideshow", "1", "DWord", "Disable lock screen slideshow", processRunner),
            new Handlers.RegistryRemediationHandler("SV-253266", @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", "1", "DWord", "Enable Windows SmartScreen", processRunner),
            new Handlers.RegistryRemediationHandler("SV-253267", @"HKLM:\SOFTWARE\Policies\Microsoft\WindowsFirewall\DomainProfile", "EnableFirewall", "1", "DWord", "Enable domain firewall profile", processRunner),
            new Handlers.RegistryRemediationHandler("SV-253268", @"HKLM:\SYSTEM\CurrentControlSet\Control\Lsa", "LmCompatibilityLevel", "5", "DWord", "Set LAN Manager authentication level to NTLMv2 only", processRunner),
            new Handlers.RegistryRemediationHandler("SV-253269", @"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "InactivityTimeoutSecs", "900", "DWord", "Set machine inactivity timeout to 900 seconds", processRunner),

            new Handlers.ServiceRemediationHandler("SV-253270", "RemoteRegistry", "Disabled", "Stopped", "Disable Remote Registry service", processRunner),
            new Handlers.ServiceRemediationHandler("SV-253271", "SSDPSRV", "Disabled", "Stopped", "Disable SSDP Discovery service", processRunner),
            new Handlers.ServiceRemediationHandler("SV-253272", "lltdsvc", "Disabled", "Stopped", "Disable Link-Layer Topology Discovery Mapper service", processRunner),

            new Handlers.AuditPolicyRemediationHandler("SV-253273", "Logon", "Success and Failure", "Enable Logon auditing for success and failure", processRunner),
            new Handlers.AuditPolicyRemediationHandler("SV-253274", "Account Lockout", "Failure", "Enable Account Lockout failure auditing", processRunner),
        };
    }
}
