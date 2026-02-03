namespace STIGForge.Apply.Dsc;

public sealed class LcmConfig
{
    public string ConfigurationMode { get; set; } = "ApplyAndMonitor";
    public bool RebootNodeIfNeeded { get; set; } = true;
    public int ConfigurationModeFrequencyMins { get; set; } = 15;
    public bool AllowModuleOverwrite { get; set; } = true;
}

public sealed class LcmState
{
    public string ConfigurationMode { get; set; } = string.Empty;
    public bool RebootNodeIfNeeded { get; set; }
    public int ConfigurationModeFrequencyMins { get; set; }
    public string LCMState { get; set; } = string.Empty;
}
