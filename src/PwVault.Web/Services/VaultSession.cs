namespace PwVault.Web.Services;

public sealed class VaultSession
{
    private string? _master;

    public bool IsUnlocked => _master is not null;

    public string Master =>
        _master ?? throw new InvalidOperationException("Vault is locked — call Unlock first.");

    public void Unlock(string master) => _master = master;

    public void Lock() => _master = null;
}
