using PwVault.Core.Domain;

namespace PwVault.Core.Storage;

internal static class EntryMapper
{
    public static VaultEntry ToDomain(EntryFileModel model, EntryPath path) => new VaultEntry(
        Path: path,
        Title: model.Title,
        Username: string.IsNullOrEmpty(model.Username) ? null : model.Username,
        Url: string.IsNullOrEmpty(model.Url) ? null : model.Url,
        PasswordEncrypted: new EncryptedField(model.PasswordAge),
        NotesEncrypted: string.IsNullOrWhiteSpace(model.NotesAge)
            ? null
            : new EncryptedField(model.NotesAge),
        Created: model.Created,
        Updated: model.Updated)
    {
        Tags = model.Tags ?? (IReadOnlyList<string>)Array.Empty<string>(),
    };

    public static EntryFileModel ToFile(VaultEntry entry) => new()
    {
        SchemaVersion = 1,
        Title = entry.Title,
        Username = entry.Username,
        Url = entry.Url,
        PasswordAge = entry.PasswordEncrypted.AsciiArmor,
        NotesAge = entry.NotesEncrypted?.AsciiArmor,
        Tags = entry.Tags.Count == 0 ? null : entry.Tags.ToList(),
        Created = entry.Created,
        Updated = entry.Updated,
    };
}
