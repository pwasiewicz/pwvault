using PwVault.Core.Domain;

namespace PwVault.Core.Security;

public interface ICryptoService
{
    DecryptionResult DecryptPassword(VaultEntry entry, string? masterPassword = null);

    DecryptionResult DecryptNotes(VaultEntry entry, string? masterPassword = null);
}
