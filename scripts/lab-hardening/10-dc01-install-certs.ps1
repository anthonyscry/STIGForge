# STIGForge Lab - Step 10: Install DoD & ECA Certificates on DC01/MS01
# Run on: DC01 or MS01 (lab.local\Administrator)
# Prerequisites: Copy cert files to C:\temp\
#   - dod_certs.zip, eca_certs.zip, fbca_crosscert_remover_v118.zip
#   - crosscert_*.cer, InstallRoot_5.6x64.msi

Import-Module "$PSScriptRoot\lib\StigForge-Certificates.psm1" -Force

# DC/MS does not need the MoveRootsFromCA logic
Install-AllCertificates -TempDir 'C:\temp'
