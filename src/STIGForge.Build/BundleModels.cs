using STIGForge.Core.Models;

namespace STIGForge.Build;

public sealed class BundleBuildRequest
{
  public string BundleId { get; set; } = string.Empty;
  public ContentPack Pack { get; set; } = new();
  public Profile Profile { get; set; } = new();
  public IReadOnlyList<ControlRecord> Controls { get; set; } = Array.Empty<ControlRecord>();
  public IReadOnlyList<Overlay> Overlays { get; set; } = Array.Empty<Overlay>();
  public string? OutputRoot { get; set; }
  public string ToolVersion { get; set; } = "dev";
  public bool ForceAutoApply { get; set; }
}

public sealed class OrchestrateRequest
{
  public string BundleRoot { get; set; } = string.Empty;
  public string? ApplyScriptPath { get; set; }
  public string? ApplyScriptArgs { get; set; }
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
}

public sealed class BundleBuildResult
{
  public string BundleId { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public string ManifestPath { get; set; } = string.Empty;
}

public sealed class BundleManifest
{
  public string BundleId { get; set; } = string.Empty;
  public string BundleRoot { get; set; } = string.Empty;
  public RunManifest Run { get; set; } = new();
  public ContentPack Pack { get; set; } = new();
  public Profile Profile { get; set; } = new();
  public int TotalControls { get; set; }
  public int AutoNaCount { get; set; }
  public int ReviewQueueCount { get; set; }
}
