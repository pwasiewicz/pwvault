namespace PwVault.Core.Security;

internal sealed class SessionFileModel
{
    public int SchemaVersion { get; set; } = 1;
    public string MasterPassword { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
