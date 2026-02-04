# Phase 1 Plan 01 - Issues & Gotchas

## [2026-02-03] Known Issues

### XDocument Memory Problem
**Symptom:** OutOfMemoryException on STIG files >50MB
**Root Cause:** XDocument.Load() loads entire XML DOM into memory
**Impact:** Cannot parse production DISA STIG files
**Fix:** XmlReader streaming pattern (this plan)

### XCCDF Namespace Handling
**Gotcha:** XCCDF uses namespace `http://checklists.nist.gov/xccdf/1.2`
**Critical:** Must use XmlNamespaceManager with XmlNameTable
**Anti-pattern:** Hardcoding namespace prefixes causes null results

### DTD Processing
**Gotcha:** DISA STIGs include DTD declarations
**Critical:** Must set `DtdProcessing = DtdProcessing.Ignore`
**Impact:** Without this, parser throws security exceptions

### SCC False Positives
**Problem:** Automated SCC checks may contain manual keywords in descriptions
**Example:** "This check manually verifies..." in an automated SCAP check
**Solution:** Check system attribute FIRST - if scap.nist.gov, it's automated regardless of content keywords
