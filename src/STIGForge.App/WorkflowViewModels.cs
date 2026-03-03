namespace STIGForge.App;

public class ImportedPackViewModel
{
    public string PackName { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public bool HasFiles => Files.Count > 0;
}

public enum WorkflowStep
{
    Setup,
    Import,
    Scan,
    Harden,
    Verify,
    Done
}

public enum StepState
{
    Locked,
    Ready,
    Running,
    Complete,
    Error
}

public enum WorkflowRootCauseCode
{
    ElevationRequired,
    EvaluatePathInvalid,
    NoCklOutput,
    ScapNoOutput,
    ScapArgumentsInvalid,
    ToolExitNonZero,
    OutputNotWritable,
    UnknownFailure
}

public sealed class WorkflowFailureCard
{
    public WorkflowRootCauseCode RootCauseCode { get; init; }
    public string Title { get; init; } = string.Empty;
    public string WhatHappened { get; init; } = string.Empty;
    public string NextStep { get; init; } = string.Empty;
    public string Confidence { get; init; } = "Medium";
    public bool ShowOpenSettingsAction { get; init; }
    public bool ShowRetryScanAction { get; init; }
    public bool ShowRetryVerifyAction { get; init; }
    public bool ShowOpenOutputFolderAction { get; init; }
}
