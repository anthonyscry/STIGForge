# STIGForge Lab - Step 3: Apply DSC Configurations
# Run on: WS01 (.\Install or domain admin)
# Prerequisites: PowerSTIG + module dependencies installed
# Covers: Firewall, Defender, Edge, DotNet, Win11

# Increase WinRM envelope for large MOFs
Set-Item WSMan:\localhost\MaxEnvelopeSizekb -Value 8192

# Unified DSC Configuration (single block to avoid MOF overwrites)
Configuration UnifiedSTIG {
    Import-DscResource -ModuleName PowerSTIG

    Node localhost {
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

        Edge EdgeBaseline {
        }

        WindowsClient Win11Baseline {
            OsVersion   = '11'
            DomainName  = 'lab.local'
            ForestName  = 'lab.local'
        }
    }
}

Write-Host "Compiling DSC configuration..."
$mofDir = UnifiedSTIG -OutputPath "$env:TEMP\UnifiedSTIG"
$mofSize = (Get-Item "$env:TEMP\UnifiedSTIG\localhost.mof").Length / 1KB
Write-Host "MOF size: $([math]::Round($mofSize, 1)) KB"

Write-Host "Applying DSC configuration..."
Start-DscConfiguration -Path "$env:TEMP\UnifiedSTIG" -Wait -Verbose -Force

Write-Host ""
Write-Host "=== DSC Configuration Applied ==="
