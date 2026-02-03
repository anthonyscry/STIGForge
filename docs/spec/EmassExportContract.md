# eMASS Export Package Contract (v1)

Root folder name:

```
EMASS_<System><OS><Role><Profile><Pack>_<YYYYMMDD-HHMM>/
```

Structure:

```
00_Manifest/
01_Scans/
02_Checklists/
03_POAM/
04_Evidence/
05_Attestations/
06_Index/
README_Submission.txt
```

Required indices:
- control_evidence_index.csv
- control_to_scan_source.csv
- na_scope_filter_report.csv
- file_hashes.sha256 (SHA-256 for every file)
