# Phase 16: XCCDF Result Export - Context

**Gathered:** 2026-02-19
**Status:** Ready for planning

<domain>
## Phase Boundary

CLI command (`export-xccdf`) that exports verify results as XCCDF 1.2 XML consumable by Tenable, ACAS, STIG Viewer, and OpenRMF. Implements `IExportAdapter` from Phase 15. Round-trip validation ensures exported XML can be re-parsed by existing `ScapResultAdapter`.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion

User delegated all implementation decisions. Claude has full flexibility on:

- **XCCDF content mapping** — which ControlResult fields map to which XCCDF 1.2 elements; what benchmark metadata and system info to include vs omit
- **CLI invocation design** — flags, output path defaults, overwrite behavior for the `export-xccdf` command
- **Downstream tool compatibility** — prioritization between Tenable, ACAS, STIG Viewer, OpenRMF; strict XCCDF 1.2 schema compliance vs practical compatibility tradeoffs
- **Output file naming** — default filename pattern and directory placement
- **Error handling** — fail-closed behavior details (partial file cleanup, error message format)

### Locked by Roadmap
- XCCDF 1.2 namespace (`http://checklists.nist.gov/xccdf/1.2`) must be on every element (from success criteria)
- Round-trip test via `ScapResultAdapter.CanHandle()` must pass (from success criteria)
- Partial output file deleted on adapter throw (from success criteria)
- Must implement `IExportAdapter` interface from Phase 15

</decisions>

<specifics>
## Specific Ideas

No specific requirements — open to standard approaches. The XCCDF 1.2 spec and existing `ScapResultAdapter` parsing logic define the contract.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 16-xccdf-result-export*
*Context gathered: 2026-02-19*
