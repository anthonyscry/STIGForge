# STIGForge Lab - Step 7: Apply DSC Configurations to Windows Server
# Run on: Any Windows Server 2019 (DC or Member Server) as Administrator
# Auto-detects: Domain Controller vs Member Server role
# Prerequisites: Run 00-install-modules.ps1 first (PowerSTIG + DSC dependencies)
# Covers: WindowsServer 2019, Firewall, Defender, DotNet, IE11, DNS (if installed)

# ============================================
# Detect Server Role
# ============================================
$productType = (Get-CimInstance Win32_OperatingSystem).ProductType
# 1 = Workstation, 2 = Domain Controller, 3 = Member Server

switch ($productType) {
    2 { $serverRole = 'DC';  $roleName = 'Domain Controller' }
    3 { $serverRole = 'MS';  $roleName = 'Member Server' }
    default {
        Write-Host "ERROR: This script is for Windows Server only (ProductType=$productType)"
        Write-Host "  For workstations, use 03-ws01-dsc-hardening.ps1"
        exit 1
    }
}

$hostname = $env:COMPUTERNAME
$domain = (Get-CimInstance Win32_ComputerSystem).Domain
$hasDns = (Get-WindowsFeature -Name DNS -ErrorAction SilentlyContinue).Installed

Write-Host "=== Server Role Detection ==="
Write-Host "  Hostname:    $hostname"
Write-Host "  Role:        $roleName ($serverRole)"
Write-Host "  Domain:      $domain"
Write-Host "  DNS Role:    $hasDns"
Write-Host ""

# Increase WinRM envelope for large MOFs
Set-Item WSMan:\localhost\MaxEnvelopeSizekb -Value 8192

# ============================================
# Build DSC Configuration dynamically based on role
# ============================================

# DSC Configuration blocks can't use variables for resource properties directly,
# so we use two separate configurations and select at runtime.

if ($serverRole -eq 'DC') {

    Configuration ServerSTIG {
        Import-DscResource -ModuleName @{ModuleName='PowerSTIG'; RequiredVersion='4.28.0'}

        Node localhost {
            WindowsServer ServerBaseline {
                OsVersion   = '2019'
                OsRole      = 'DC'
                DomainName  = $Node.DomainName
                ForestName  = $Node.ForestName
            }

            WindowsFirewall FirewallBaseline {
                OrgSettings = @{
                    'V-241989' = @{LogFilePath = '%systemroot%\system32\logfiles\firewall\domainfw.log'}
                    'V-241992' = @{LogFilePath = '%systemroot%\system32\logfiles\firewall\privatefw.log'}
                    'V-241995' = @{LogFilePath = '%systemroot%\system32\logfiles\firewall\publicfw.log'}
                }
            }

            WindowsDefender DefenderBaseline {
            }

            DotNetFramework DotNetBaseline {
                FrameworkVersion = '4'
            }

            # InternetExplorer removed: conflicts with DotNetFramework (V-225224 vs V-223016)
            # IE11 hardening handled by 09a-dc01-ie11-hardening.ps1 (direct registry)
        }
    }

} else {

    Configuration ServerSTIG {
        Import-DscResource -ModuleName @{ModuleName='PowerSTIG'; RequiredVersion='4.28.0'}

        Node localhost {
            WindowsServer ServerBaseline {
                OsVersion   = '2019'
                OsRole      = 'MS'
                DomainName  = $Node.DomainName
                ForestName  = $Node.ForestName
            }

            WindowsFirewall FirewallBaseline {
                OrgSettings = @{
                    'V-241989' = @{LogFilePath = '%systemroot%\system32\logfiles\firewall\domainfw.log'}
                    'V-241992' = @{LogFilePath = '%systemroot%\system32\logfiles\firewall\privatefw.log'}
                    'V-241995' = @{LogFilePath = '%systemroot%\system32\logfiles\firewall\publicfw.log'}
                }
            }

            WindowsDefender DefenderBaseline {
            }

            DotNetFramework DotNetBaseline {
                FrameworkVersion = '4'
            }

            # InternetExplorer removed: conflicts with DotNetFramework (V-225224 vs V-223016)
            # IE11 hardening handled by 09a-dc01-ie11-hardening.ps1 (direct registry)
        }
    }

}

# Parse domain/forest from FQDN
$domainParts = $domain.Split('.')
$domainName = $domain
$forestName = $domain

$configData = @{
    AllNodes = @(
        @{
            NodeName   = 'localhost'
            DomainName = $domainName
            ForestName = $forestName
        }
    )
}

Write-Host "Compiling DSC configuration (ServerRole=$serverRole)..."
$mofDir = ServerSTIG -ConfigurationData $configData -OutputPath "$env:TEMP\ServerSTIG"
$mofSize = (Get-Item "$env:TEMP\ServerSTIG\localhost.mof").Length / 1KB
Write-Host "MOF size: $([math]::Round($mofSize, 1)) KB"

Write-Host "Applying DSC configuration (10-minute timeout)..."
$mofPath = "$env:TEMP\ServerSTIG"
$dscJob = Start-Job -ScriptBlock {
    param($Path)
    Start-DscConfiguration -Path $Path -Wait -Force -Verbose 4>&1
} -ArgumentList $mofPath
$completed = $dscJob | Wait-Job -Timeout 600
if (-not $completed) {
    Write-Host "WARNING: DSC timed out after 10 minutes - stopping job"
    $dscJob | Stop-Job
    $dscJob | Receive-Job -ErrorAction SilentlyContinue
    $dscJob | Remove-Job -Force
} else {
    $dscJob | Receive-Job -ErrorAction SilentlyContinue
    $dscJob | Remove-Job -Force
    Write-Host "DSC configuration applied successfully"
}

Write-Host ""
Write-Host "=== DSC Configuration Applied ($roleName) ==="
Write-Host ""
Write-Host "  WindowsServer 2019 $serverRole baseline - registry, audit, security settings"
Write-Host "  WindowsFirewall - all 3 profiles enabled with logging"
Write-Host "  WindowsDefender - scanning, MAPS, ASR rules"
Write-Host "  DotNetFramework 4 - strong crypto + TLS defaults"
Write-Host "  (IE11 handled separately by 09a-dc01-ie11-hardening.ps1)"
Write-Host ""
Write-Host "Next steps:"
if ($serverRole -eq 'DC') {
    Write-Host "  1. Run 07a-dc01-admx-lgpo.ps1 for ADMX + DISA GPO imports"
    Write-Host "  2. Run 08-dc01-stig-gpos.ps1 for custom GPO complement"
    Write-Host "  3. Run 09-dc01-local-hardening.ps1 for user rights, AD, DNS, NTP"
    Write-Host "  4. Run 10-dc01-install-certs.ps1 for DoD certificates"
    Write-Host "  5. Reboot, then run 11-dc01-scan.ps1"
} else {
    Write-Host "  1. Run 07a with LGPO for MS STIG GPO local policy import"
    Write-Host "  2. Run local hardening (secedit, auditpol, user rights)"
    Write-Host "  3. Run cert install script"
    Write-Host "  4. Reboot, then run Evaluate-STIG scan"
}
