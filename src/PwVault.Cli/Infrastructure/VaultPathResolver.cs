namespace PwVault.Cli.Infrastructure;

public static class VaultPathResolver
{
    public static string Resolve(BaseCommandSettings settings, PwVaultConfig config)
    {
        var path = settings.VaultPath ?? config.VaultPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new VaultNotConfiguredException(
                "Vault path not configured. Use --vault <path>, set PWVAULT_PATH, "
                + "or add 'vault_path' to the config file (see ~/.config/pwvault/config.json).");
        return Path.GetFullPath(path);
    }
}

public sealed class VaultNotConfiguredException : Exception
{
    public VaultNotConfiguredException(string message) : base(message) { }
}
