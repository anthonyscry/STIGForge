# STIGForge Lab - Step 5: Install DoD & ECA Root Certificates
# Run on: WS01 (.\Install or domain admin)
# Prerequisites: dod_certs.zip, eca_certs.zip, fbca_crosscert_remover_v118.zip,
#   crosscert_*.cer, InstallRoot_5.6x64.msi copied to C:\temp\
#
# Covers STIG findings:
#   V-253425-V-253428: DoD Root CA 3/4/5/6 in Trusted Root Store
#   V-253431-V-253432: ECA Root CA 4/5 in Trusted Root Store
#   V-253429-V-253430: Cross-certs in Untrusted Store (via FBCA + manual)

Import-Module "$PSScriptRoot\lib\StigForge-Certificates.psm1" -Force

# WS01 uses -IsWorkstation to handle certs that land in wrong store
Install-AllCertificates -TempDir 'C:\temp' -IsWorkstation
