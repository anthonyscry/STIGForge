# STIGForge Lab - Step 1: Create Admin Accounts and OU Structure
# Run on: DC01 (lab.local\Administrator)
# Prerequisites: Active Directory domain controller

Import-Module ActiveDirectory

# ============================================
# Create OU Structure
# ============================================
$ouBase = 'DC=lab,DC=local'
$ous = @(
    @{ Name='Admin Accounts'; Path=$ouBase }
    @{ Name='Workstations'; Path=$ouBase }
    @{ Name='Servers'; Path=$ouBase }
)

foreach ($ou in $ous) {
    $ouDN = "OU=$($ou.Name),$($ou.Path)"
    if (-not ([ADSI]::Exists("LDAP://$ouDN"))) {
        New-ADOrganizationalUnit -Name $ou.Name -Path $ou.Path -ProtectedFromAccidentalDeletion $false
        Write-Host "Created OU: $ouDN"
    }
}

# ============================================
# Create Security Groups
# ============================================
$waGroup = 'Workstation Admins'
if (-not (Get-ADGroup -Filter "Name -eq '$waGroup'" -ErrorAction SilentlyContinue)) {
    New-ADGroup -Name $waGroup -GroupScope DomainLocal -GroupCategory Security `
        -Path "OU=Admin Accounts,$ouBase" -Description 'Workstation local admin group'
    Write-Host "Created group: $waGroup"
}

# ============================================
# Create Admin Accounts
# ============================================
$pw = ConvertTo-SecureString 'P@ssw0rd!' -AsPlainText -Force
$adminOU = "OU=Admin Accounts,$ouBase"

# xadmin - Server Admin
if (-not (Get-ADUser -Filter "SamAccountName -eq 'xadmin'" -ErrorAction SilentlyContinue)) {
    New-ADUser -Name 'xadmin' -SamAccountName 'xadmin' -UserPrincipalName 'xadmin@lab.local' `
        -Path $adminOU -AccountPassword $pw -Enabled $true -PasswordNeverExpires $true `
        -Description 'Server Administrator'
    Add-ADGroupMember -Identity 'Domain Admins' -Members 'xadmin'
    Add-ADGroupMember -Identity 'Server Operators' -Members 'xadmin'
    Write-Host "Created xadmin (Server Admin)"
}

# xxadmin - Domain Admin
if (-not (Get-ADUser -Filter "SamAccountName -eq 'xxadmin'" -ErrorAction SilentlyContinue)) {
    New-ADUser -Name 'xxadmin' -SamAccountName 'xxadmin' -UserPrincipalName 'xxadmin@lab.local' `
        -Path $adminOU -AccountPassword $pw -Enabled $true -PasswordNeverExpires $true `
        -Description 'Domain Administrator'
    Add-ADGroupMember -Identity 'Domain Admins' -Members 'xxadmin'
    Add-ADGroupMember -Identity 'Enterprise Admins' -Members 'xxadmin'
    Add-ADGroupMember -Identity 'Schema Admins' -Members 'xxadmin'
    Add-ADGroupMember -Identity 'Group Policy Creator Owners' -Members 'xxadmin'
    Write-Host "Created xxadmin (Domain Admin)"
}

# admin.wa - Workstation Admin
if (-not (Get-ADUser -Filter "SamAccountName -eq 'admin.wa'" -ErrorAction SilentlyContinue)) {
    New-ADUser -Name 'admin.wa' -SamAccountName 'admin.wa' -UserPrincipalName 'admin.wa@lab.local' `
        -Path $adminOU -AccountPassword $pw -Enabled $true -PasswordNeverExpires $true `
        -Description 'Workstation Administrator'
    Add-ADGroupMember -Identity $waGroup -Members 'admin.wa'
    Write-Host "Created admin.wa (Workstation Admin)"
}

# ============================================
# Move computers to proper OUs
# ============================================
$ws01 = Get-ADComputer 'WS01' -ErrorAction SilentlyContinue
if ($ws01 -and $ws01.DistinguishedName -notmatch 'OU=Workstations') {
    Move-ADObject -Identity $ws01.DistinguishedName -TargetPath "OU=Workstations,$ouBase"
    Write-Host "Moved WS01 to Workstations OU"
}

foreach ($srv in @('SRV01','SRV02')) {
    $comp = Get-ADComputer $srv -ErrorAction SilentlyContinue
    if ($comp -and $comp.DistinguishedName -notmatch 'OU=Servers') {
        Move-ADObject -Identity $comp.DistinguishedName -TargetPath "OU=Servers,$ouBase"
        Write-Host "Moved $srv to Servers OU"
    }
}

Write-Host ""
Write-Host "=== Account Setup Complete ==="
