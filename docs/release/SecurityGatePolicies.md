# Security Gate Policies

`tools/release/Invoke-SecurityGate.ps1` uses three policy files:

- `tools/release/security-vulnerability-exceptions.json`
  - Temporary allowlist for known advisories that are accepted with documented risk.
  - Keep entries minimal and remove as dependencies are upgraded.

- `tools/release/security-license-policy.json`
  - Allowed SPDX license expressions and license URLs.
  - `allowUnknownForPackages` is the explicit exception list for packages with missing license metadata.

- `tools/release/security-secrets-policy.json`
  - File extensions to scan.
  - Directory and file glob exclusions.
  - Benign line fragments to ignore (example/sample placeholders only).

## Policy update process

1. Reproduce the security gate failure locally.
2. Prefer remediation first (upgrade/remove dependency, remove secret-like data, fix metadata).
3. If exception is required, update the smallest possible policy entry.
4. Include a clear reason in the policy change.
5. Open a follow-up task to remove the exception.
