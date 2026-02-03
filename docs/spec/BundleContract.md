# Offline Bundle Contract (v1)

Deterministic structure:

```
Bundle_<bundleId>/
  Manifest/
    manifest.json
    file_hashes.sha256
    run_log.txt
  Apply/
  Verify/
  Manual/
    answerfile.template.json
  Evidence/
  Reports/
```

Rules:
- All paths created via IPathBuilder
- Hash manifest includes every file in bundle
- Bundle runnable via STIGForge.Cli
