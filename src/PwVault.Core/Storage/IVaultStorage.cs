using PwVault.Core.Domain;

namespace PwVault.Core.Storage;

public interface IVaultStorage : IDisposable
{
    string RootPath { get; }

    IReadOnlyList<StoredEntry> List(EntryPath? underPath = null, IReadOnlyList<string>? tags = null);

    StoredEntry? TryGet(EntryPath path);

    StoredEntry Get(EntryPath path);

    StoredEntry Add(VaultEntry entry);

    StoredEntry Update(VaultEntry entry);

    void Remove(EntryPath path);

    IReadOnlyList<StoredEntry> Search(string query, int maxResults = 20, IReadOnlyList<string>? tags = null);

    IReadOnlyList<TagCount> ListTags();
}

public readonly record struct TagCount(string Tag, int Count);
