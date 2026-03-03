using STIGForge.Core.Models;
using STIGForge.Core.Services;

namespace STIGForge.Build;

public sealed class BundleBuildRequest
{
  public string BundleId { get; set; } = string.Empty;
  public ContentPack Pack { get; set; } = new();
  public Profile Profile { get; set; } = new();
  public IReadOnlyList<ControlRecord> Controls { get; set; } = [];
  public IReadOnlyList<Overlay> Overlays { get; set; } = [];
  public string? OutputRoot { get; set; }
  public string ToolVersion { get; set; } = "dev";
  public bool ForceAutoApply { get; set; }

  /// <summary>
  /// Optional SCAP benchmark candidates for per-STIG mapping manifest generation.
  /// When provided with a CanonicalScapSelector, produces scap_mapping_manifest.json.
  /// </summary>
  public IReadOnlyList<CanonicalScapCandidate>? ScapCandidates { get; set; }
}

public sealed class OrchestrateRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public string? ApplyScriptPath { get; set; }
  public string? ApplyScriptArgs { get; set; }
  public bool SkipSnapshot { get; set; }
  public bool BreakGlassAcknowledged { get; set; }
  public string? BreakGlassReason { get; set; }
  public string? DscMofPath { get; set; }
  public bool DscVerbose { get; set; }
  public string? PowerStigModulePath { get; set; }
  public string? PowerStigDataFile { get; set; }
  public string? PowerStigOutputPath { get; set; }
  public bool PowerStigVerbose { get; set; }

  public string? EvaluateStigRoot { get; set; }
  public string? EvaluateStigArgs { get; set; }
  public string? ScapCommandPath { get; set; }
  public string? ScapArgs { get; set; }
  public string? ScapToolLabel { get; set; }

  public string? VerifyOutputRoot { get; set; }

  /// <summary>When true, simulates apply phase without changes and produces a dry-run report.</summary>
  public bool DryRun { get; set; }

  /// <summary>Optional filter: only apply controls matching these Rule IDs (e.g., SV-12345).</summary>
  public IReadOnlyList<string>? FilterRuleIds { get; set; }

  /// <summary>Optional filter: only apply controls matching these severities ("high","medium","low").</summary>
  public IReadOnlyList<string>? FilterSeverities { get; set; }

  /// <summary>Optional filter: only apply controls matching these categories.</summary>
  public IReadOnlyList<string>? FilterCategories { get; set; }
}

public sealed class BundleBuildResult
{
  public string BundleId { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public string ManifestPath { get; set; } = string.Empty;
  public string? ScapMappingManifestPath { get; set; }
}

public sealed class BundleManifest
{
  public int SchemaVersion { get; set; } = 1;
  public string BundleId { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public RunManifest Run { get; set; } = new();
  public ContentPack Pack { get; set; } = new();
  public Profile Profile { get; set; } = new();
  public int TotalControls { get; set; }
  public int AutoNaCount { get; set; }
  public int ReviewQueueCount { get; set; }
}
