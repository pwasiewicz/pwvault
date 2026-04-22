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
    private readonly IReadOnlyList<string> _tags = Array.Empty<string>();

    public IReadOnlyList<string> Tags
    {
        get => _tags;
        init => _tags = TagNormalizer.Normalize(value);
    }

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

    public bool Equals(VaultEntry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Path == other.Path
            && Title == other.Title
            && Username == other.Username
            && Url == other.Url
            && PasswordEncrypted == other.PasswordEncrypted
            && NotesEncrypted == other.NotesEncrypted
            && Created == other.Created
            && Updated == other.Updated
            && Tags.SequenceEqual(other.Tags, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Path);
        hash.Add(Title);
        hash.Add(Username);
        hash.Add(Url);
        hash.Add(PasswordEncrypted);
        hash.Add(NotesEncrypted);
        hash.Add(Created);
        hash.Add(Updated);
        foreach (var tag in Tags) hash.Add(tag, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
