namespace PwVault.Web.Services;

public sealed class VaultOptions
{
    public string VaultPath { get; init; } = "";
    public int WorkFactor { get; init; } = 18;
    public int MaxDecryptWorkFactor { get; init; } = 22;
}
