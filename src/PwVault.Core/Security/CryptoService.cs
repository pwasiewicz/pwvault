using PwVault.Core.Domain;

namespace PwVault.Core.Security;

public sealed class CryptoService : ICryptoService
{
    private readonly ISessionStore _sessionStore;
    private readonly IAgeGateway _age;

    public CryptoService(ISessionStore sessionStore, IAgeGateway age)
    {
        ArgumentNullException.ThrowIfNull(sessionStore);
        ArgumentNullException.ThrowIfNull(age);
        _sessionStore = sessionStore;
        _age = age;
    }

    public DecryptionResult DecryptPassword(VaultEntry entry, string? masterPassword = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Decrypt(entry.PasswordEncrypted, masterPassword);
    }

    public DecryptionResult DecryptNotes(VaultEntry entry, string? masterPassword = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.NotesEncrypted is null)
            return DecryptionResult.Success(null);
        return Decrypt(entry.NotesEncrypted.Value, masterPassword);
    }

    private DecryptionResult Decrypt(EncryptedField field, string? providedMaster)
    {
        var master = providedMaster ?? _sessionStore.TryGetAndExtend();
        if (master is null)
            return DecryptionResult.MasterNeeded;

        var plainText = _age.Decrypt(field.AsciiArmor, master);
        return DecryptionResult.Success(plainText);
    }
}
