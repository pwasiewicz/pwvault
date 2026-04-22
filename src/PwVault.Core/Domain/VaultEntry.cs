namespace PwVault.Core.Domain;

public sealed record VaultEntry(
    EntryPath Path,
    string Title,
    string? Username,
    string? Url,
    EncryptedField PasswordEncrypted,
    EncryptedField? NotesEncrypted,
    DateTimeOffset Created,
    DateTimeOffset Updated)
{
    public VaultEntry(
        EntryPath Path,
        string Title,
        string? Username,
        string? Url,
        EncryptedField PasswordEncrypted,
        EncryptedField? NotesEncrypted)
        : this(Path, Title, Username, Url, PasswordEncrypted, NotesEncrypted,
            Created: default, Updated: default)
    {
    }
}
