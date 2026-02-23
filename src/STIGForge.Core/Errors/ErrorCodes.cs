namespace STIGForge.Core.Errors;

/// <summary>
/// Centralized error code constants for STIGForge.
/// Format: {COMPONENT}_{NUMBER} where NUMBER is a unique 3-digit identifier per component.
/// </summary>
public static class ErrorCodes
{
    // Build errors (BUILD_001 - BUILD_099)
    public const string BUILD_BUNDLE_FAILED = "BUILD_001";
    public const string BUILD_INVALID_PROFILE = "BUILD_002";
    public const string BUILD_NO_STIGS_SELECTED = "BUILD_003";

    // Import errors (IMPORT_001 - IMPORT_099)
    public const string IMPORT_PARSE_FAILED = "IMPORT_001";
    public const string IMPORT_VALIDATION_FAILED = "IMPORT_002";
    public const string IMPORT_DUPLICATE_DETECTED = "IMPORT_003";

    // Apply errors (APPLY_001 - APPLY_099)
    public const string APPLY_DSC_FAILED = "APPLY_001";
    public const string APPLY_REBOOT_REQUIRED = "APPLY_002";
    public const string APPLY_SNAPSHOT_FAILED = "APPLY_003";
    public const string APPLY_VALIDATION_FAILED = "APPLY_004";

    // Verify errors (VERIFY_001 - VERIFY_099)
    public const string VERIFY_SCAP_FAILED = "VERIFY_001";
    public const string VERIFY_TIMEOUT = "VERIFY_002";
    public const string VERIFY_RESULTS_NOT_FOUND = "VERIFY_003";

    // Export errors (EXPORT_001 - EXPORT_099)
    public const string EXPORT_EMASS_FAILED = "EXPORT_001";
    public const string EXPORT_CKL_FAILED = "EXPORT_002";
    public const string EXPORT_XCCDF_FAILED = "EXPORT_003";

    // Orchestration errors (ORCH_001 - ORCH_099)
    public const string ORCH_PHASE_FAILED = "ORCH_001";
    public const string ORCH_INTEGRITY_VIOLATION = "ORCH_002";
    public const string ORCH_DEPENDENCY_MISSING = "ORCH_003";

    // Configuration errors (CONFIG_001 - CONFIG_099)
    public const string CONFIG_PATH_NOT_FOUND = "CONFIG_001";
    public const string CONFIG_INVALID_SETTING = "CONFIG_002";

    // Evidence errors (EVIDENCE_001 - EVIDENCE_099)
    public const string EVIDENCE_WRITE_FAILED = "EVIDENCE_001";
    public const string EVIDENCE_HASH_MISMATCH = "EVIDENCE_002";
}
