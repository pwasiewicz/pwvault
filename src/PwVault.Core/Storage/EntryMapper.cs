using PwVault.Core.Domain;

namespace PwVault.Core.Storage;

internal static class EntryMapper
{
    public static VaultEntry ToDomain(EntryFileModel model, EntryPath path) => new(
        Path: path,
        Title: model.Title,
        Username: string.IsNullOrEmpty(model.Username) ? null : model.Username,
        Url: string.IsNullOrEmpty(model.Url) ? null : model.Url,
        PasswordEncrypted: new EncryptedField(model.PasswordAge),
        NotesEncrypted: string.IsNullOrWhiteSpace(model.NotesAge)
            ? null
            : new EncryptedField(model.NotesAge),
        Created: model.Created,
        Updated: model.Updated);

    public static EntryFileModel ToFile(VaultEntry entry) => new()
    {
        SchemaVersion = 1,
        Title = entry.Title,
        Username = entry.Username,
        Url = entry.Url,
        PasswordAge = entry.PasswordEncrypted.AsciiArmor,
        NotesAge = entry.NotesEncrypted?.AsciiArmor,
        Created = entry.Created,
        Updated = entry.Updated,
    };
}
