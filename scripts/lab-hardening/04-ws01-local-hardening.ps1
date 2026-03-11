# STIGForge Lab - Step 4: WS01 Local-Only Fixes (Non-GPO)
# Run on: WS01 (.\Install or domain admin)
#
# This script handles ONLY settings that CANNOT be managed via GPO:
#   - BitLocker encryption START (enabling, protectors, manage-bde)
#
# All other settings are now in GPOs for easy enable/disable:
#   - Account policies -> Default Domain Policy (02-dc01-create-gpos.ps1)
#   - User rights, deny logon -> STIG-Win11 GPO (02-dc01-create-gpos.ps1)
#   - Audit policies -> STIG-Win11 GPO (02-dc01-create-gpos.ps1)
#   - HVCI/VBS -> STIG-Win11 GPO (DeviceGuard registry)
#   - BitLocker FVE POLICY -> STIG-Win11 GPO (FVE registry)
#   - IE11 settings -> STIG-IE11 GPO
#   - OneDrive settings -> STIG-OneDrive GPO
#   - HKCU IE/OneDrive -> STIG-IE11/STIG-OneDrive GPOs (User Configuration)

$fixed = 0

# ============================================
# BitLocker Encryption (requires TPM + local execution)
# The FVE POLICY is in STIG-Win11 GPO.
# This script starts the ACTUAL encryption.
# ============================================
Write-Host "=== BitLocker Encryption ==="
Write-Host ""

$tpm = Get-Tpm -ErrorAction SilentlyContinue
if ($tpm.TpmPresent -and $tpm.TpmReady) {
    $bl = Get-BitLockerVolume -MountPoint C: -ErrorAction SilentlyContinue
    if ($bl -and $bl.VolumeStatus -eq 'FullyDecrypted') {
        try {
            # Clean existing protectors
            foreach ($kp in $bl.KeyProtector) {
                Remove-BitLockerKeyProtector -MountPoint C: -KeyProtectorId $kp.KeyProtectorId -ErrorAction SilentlyContinue
            }
            Add-BitLockerKeyProtector -MountPoint C: -TpmProtector -ErrorAction Stop
            Add-BitLockerKeyProtector -MountPoint C: -RecoveryPasswordProtector -ErrorAction Stop

            # Get recovery key for backup
            $bl2 = Get-BitLockerVolume -MountPoint C:
            $recKey = ($bl2.KeyProtector | Where-Object { $_.KeyProtectorType -eq 'RecoveryPassword' }).RecoveryPassword
            Write-Host "  Recovery Key: $recKey"
            Write-Host "  SAVE THIS KEY!"

            # Use manage-bde instead of Enable-BitLocker (avoids parameter set errors)
            $bdeResult = manage-bde -on C: -UsedSpaceOnly 2>&1
            $bdeResult | ForEach-Object { Write-Host "  $_" }
            Write-Host "  BitLocker encryption started (reboot required for hardware test)"
            $fixed++
        } catch {
            Write-Host "  BitLocker error: $($_.Exception.Message)"
        }
    } elseif ($bl) {
        Write-Host "  BitLocker status: $($bl.VolumeStatus) (no action needed)"
    }
} else {
    Write-Host "  TPM not available - enable vTPM on Hyper-V host first"
}

Write-Host ""
Write-Host "========================================="
Write-Host "  Total local-only fixes applied: $fixed"
Write-Host "========================================="
Write-Host ""
Write-Host "  All other settings handled by GPO:"
Write-Host "    - Account policies -> Default Domain Policy"
Write-Host "    - User rights + audit -> STIG-Win11 GPO"
Write-Host "    - IE11 -> STIG-IE11 GPO"
Write-Host "    - OneDrive -> STIG-OneDrive GPO"
Write-Host "    - VBS/HVCI/BitLocker policy -> STIG-Win11 GPO"
Write-Host ""
Write-Host "  To DISABLE hardening: disable GPO links on Workstations OU"
Write-Host "  To RE-ENABLE: re-enable GPO links"
Write-Host ""
Write-Host "  REMAINING (not automated):"
Write-Host "    - DoD/ECA certs: run 05-ws01-install-certs.ps1"
Write-Host "    - Smart card/MFA (V-253470): hardware-dependent"
Write-Host "    - LAPS (V-253476): org configuration"
