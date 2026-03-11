#!/usr/bin/env bash
set -euo pipefail

LAB_ENV_ROOT="${LAB_ENV_ROOT:-/home/anthonyscry/projects/lab-environment}"
HYPERV_HOST="${HYPERV_HOST:-triton-ajt}"
HYPERV_USER="${HYPERV_USER:-majordev}"
REMOTE_REPO="${REMOTE_REPO:-C:\\STIGForge}"

if [[ $# -lt 1 ]]; then
  cat <<'USAGE'
Usage:
  scripts/lab-hardening/run-on-hyperv-host.sh "<powershell command>"

Examples:
  scripts/lab-hardening/run-on-hyperv-host.sh "dotnet --info"
  scripts/lab-hardening/run-on-hyperv-host.sh "dotnet build STIGForge.sln -c Release"
  scripts/lab-hardening/run-on-hyperv-host.sh "dotnet test STIGForge.sln -c Release --no-build"

Optional env overrides:
  HYPERV_HOST, HYPERV_USER, REMOTE_REPO, LAB_ENV_ROOT
USAGE
  exit 1
fi

if [[ ! -d "$LAB_ENV_ROOT" ]]; then
  echo "ERROR: Lab environment folder not found at: $LAB_ENV_ROOT" >&2
  exit 1
fi

if [[ ! -f "$HOME/.ssh/id_ed25519" ]]; then
  echo "ERROR: SSH key not found at $HOME/.ssh/id_ed25519" >&2
  exit 1
fi

PS_COMMAND="$1"

ssh "${HYPERV_USER}@${HYPERV_HOST}" \
  "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \"if (-not (Test-Path '${REMOTE_REPO}')) { throw 'REMOTE_REPO not found: ${REMOTE_REPO}' }; Set-Location '${REMOTE_REPO}'; ${PS_COMMAND}\""
