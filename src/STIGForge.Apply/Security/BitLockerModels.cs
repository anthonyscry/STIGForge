namespace STIGForge.Apply.Security;

public sealed class BitLockerConfig
{
    public IReadOnlyList<string> VolumeTargets { get; set; } = new[] { "C:" };
    public string EncryptionMethod { get; set; } = "XtsAes256";
    public string? RecoveryKeyPath { get; set; }
    public bool RequireTpm { get; set; } = true;
}
