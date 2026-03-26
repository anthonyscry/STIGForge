# StigForge-OrgSettings.psm1 — Apply classification + environment org settings to CKL files
# Import: Import-Module "$PSScriptRoot\..\lib\StigForge-OrgSettings.psm1" -Force
#
# Marks STIG findings as Not_Applicable based on:
#   - System classification (classified/unclassified)
#   - Environment conditions (no LAPS, no smart card, VM, etc.)

function Get-OrgSettings {
    param(
        [ValidateSet('classified','unclassified')]
        [string]$Classification = 'unclassified',
        [string]$OrgSettingsDir
    )

    if (-not $OrgSettingsDir) {
        $OrgSettingsDir = Join-Path $PSScriptRoot '..\org-settings'
    }

    $classFile = Join-Path $OrgSettingsDir "$Classification.json"
    $envFile = Join-Path $OrgSettingsDir 'environment.json'

    $settings = @{
        Classification = $Classification
        NotApplicable  = @()
        OrgDefined     = @()
        Environment    = $null
    }

    if (Test-Path $classFile) {
        $classData = Get-Content $classFile -Raw | ConvertFrom-Json
        $settings.NotApplicable = @($classData.notApplicable)
        $settings.OrgDefined = @($classData.orgDefined)
    }

    if (Test-Path $envFile) {
        $settings.Environment = Get-Content $envFile -Raw | ConvertFrom-Json
    }

    return $settings
}

function Get-EnvironmentNA {
    param(
        [PSCustomObject]$EnvironmentSettings,
        [hashtable]$Conditions = @{}
    )

    $naList = @()
    if (-not $EnvironmentSettings -or -not $EnvironmentSettings.conditions) { return $naList }

    $condProps = $EnvironmentSettings.conditions.PSObject.Properties
    foreach ($prop in $condProps) {
        $condName = $prop.Name
        $condDef = $prop.Value
        $condValue = $Conditions[$condName]

        # If condition is explicitly set to $false and has ifFalse_NA
        if ($condValue -eq $false -and $condDef.ifFalse_NA) {
            foreach ($vid in $condDef.ifFalse_NA) {
                $naList += [PSCustomObject]@{
                    VulnId = $vid
                    Reason = $condDef.reason
                    Condition = "$condName = false"
                }
            }
        }

        # If condition is $true and has ifTrue_NA
        if ($condValue -eq $true -and $condDef.ifTrue_NA) {
            foreach ($vid in $condDef.ifTrue_NA) {
                $naList += [PSCustomObject]@{
                    VulnId = $vid
                    Reason = $condDef.reason
                    Condition = "$condName = true"
                }
            }
        }
    }

    return $naList
}

function Set-CklNotApplicable {
    param(
        [Parameter(Mandatory)][string]$CklPath,
        [string[]]$VulnIds,
        [string]$Comment = 'Marked N/A by STIGForge OrgSettings',
        [hashtable]$ReasonMap = @{}
    )

    [xml]$xml = Get-Content $CklPath
    $changed = 0

    foreach ($vuln in $xml.CHECKLIST.STIGS.iSTIG.VULN) {
        $vid = ($vuln.STIG_DATA | Where-Object { $_.VULN_ATTRIBUTE -eq 'Vuln_Num' }).ATTRIBUTE_DATA
        if ($vid -in $VulnIds -and $vuln.STATUS -ne 'Not_Applicable') {
            $oldStatus = $vuln.STATUS
            $vuln.STATUS = 'Not_Applicable'

            # Add comment with reason
            $reason = if ($ReasonMap[$vid]) { $ReasonMap[$vid] } else { $Comment }
            $existingComment = $vuln.COMMENTS
            $newComment = "[STIGForge OrgSettings] $reason (was: $oldStatus)"
            if ($existingComment) {
                $vuln.COMMENTS = "$existingComment`r`n$newComment"
            } else {
                $vuln.COMMENTS = $newComment
            }
            $changed++
        }
    }

    if ($changed -gt 0) {
        $xml.Save($CklPath)
    }

    return $changed
}

function Apply-OrgSettings {
    param(
        [Parameter(Mandatory)][string]$CklDirectory,
        [ValidateSet('classified','unclassified')]
        [string]$Classification = 'unclassified',
        [hashtable]$EnvironmentConditions = @{},
        [string]$OrgSettingsDir
    )

    $settings = Get-OrgSettings -Classification $Classification -OrgSettingsDir $OrgSettingsDir
    $totalChanged = 0

    # Build N/A list from classification
    $naVulnIds = @()
    $reasonMap = @{}
    foreach ($na in $settings.NotApplicable) {
        $naVulnIds += $na.vulnId
        $reasonMap[$na.vulnId] = "$Classification system: $($na.reason)"
    }

    # Build N/A list from environment conditions
    $envNA = Get-EnvironmentNA -EnvironmentSettings $settings.Environment -Conditions $EnvironmentConditions
    foreach ($na in $envNA) {
        $naVulnIds += $na.VulnId
        $reasonMap[$na.VulnId] = "Environment: $($na.Reason) ($($na.Condition))"
    }

    if ($naVulnIds.Count -eq 0) {
        Write-Host "  No findings to mark N/A for $Classification system"
        return 0
    }

    Write-Host "  Applying OrgSettings ($Classification): $($naVulnIds.Count) findings to mark N/A"

    # Apply to all CKL files in directory
    $cklFiles = Get-ChildItem $CklDirectory -Filter '*.ckl' -Recurse -ErrorAction SilentlyContinue
    foreach ($ckl in $cklFiles) {
        $changed = Set-CklNotApplicable -CklPath $ckl.FullName -VulnIds $naVulnIds -ReasonMap $reasonMap
        if ($changed -gt 0) {
            Write-Host "    $($ckl.Name): $changed findings marked N/A"
            $totalChanged += $changed
        }
    }

    # Report org-defined values
    if ($settings.OrgDefined.Count -gt 0) {
        Write-Host ""
        Write-Host "  Org-defined values ($Classification):"
        foreach ($od in $settings.OrgDefined) {
            Write-Host "    $($od.vulnId): $($od.setting) = $($od.value)"
        }
    }

    Write-Host "  Total findings marked N/A: $totalChanged"
    return $totalChanged
}

Export-ModuleMember -Function @(
    'Get-OrgSettings', 'Get-EnvironmentNA',
    'Set-CklNotApplicable', 'Apply-OrgSettings'
)
