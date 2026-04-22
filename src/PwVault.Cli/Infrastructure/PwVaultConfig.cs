namespace PwVault.Cli.Infrastructure;

public sealed class PwVaultConfig
{
    public string? VaultPath { get; set; }
    public int WorkFactor { get; set; } = 18;
    public int MaxDecryptWorkFactor { get; set; } = 22;
    public int ClipboardClearSeconds { get; set; } = 15;
    public bool AutoCommit { get; set; } = true;
    public bool AutoPush { get; set; } = false;
    public int GeneratedPasswordLength { get; set; } = 24;
}
