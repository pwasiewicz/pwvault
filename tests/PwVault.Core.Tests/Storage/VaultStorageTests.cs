using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Storage;
using Xunit;

namespace PwVault.Core.Tests.Storage;

public sealed class VaultStorageTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly IFileSystem _fs = new RealFileSystem();

    public VaultStorageTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pwvault-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private VaultStorage NewStorage() => VaultStorage.Open(_tempRoot, _fs);

    private static EncryptedField Cipher(string label) => new(
        $"-----BEGIN AGE ENCRYPTED FILE-----\nFAKE({label})\n-----END AGE ENCRYPTED FILE-----");

    private static VaultEntry SampleEntry(string pathValue, string title = "Sample") => new(
        Path: new EntryPath(pathValue),
        Title: title,
        Username: "user@example.com",
        Url: "https://example.com",
        PasswordEncrypted: Cipher("pwd"),
        NotesEncrypted: Cipher("notes"));

    [Fact]
    public void Opening_missing_root_throws()
    {
        var missing = Path.Combine(_tempRoot, "does-not-exist");
        var ex = Assert.Throws<VaultStorageException>(() => VaultStorage.Open(missing, _fs));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Add_then_Get_roundtrips_all_fields()
    {
        using var storage = NewStorage();
        var added = storage.Add(SampleEntry("banking/mbank"));

        var fetched = storage.Get(new EntryPath("banking/mbank"));

        Assert.Equal(added.Entry, fetched.Entry);
        Assert.Equal("Sample", fetched.Entry.Title);
        Assert.Equal("user@example.com", fetched.Entry.Username);
        Assert.Equal("https://example.com", fetched.Entry.Url);
        Assert.NotNull(fetched.Entry.NotesEncrypted);
        Assert.True(fetched.Metadata.FileSizeBytes > 0);
    }

    [Fact]
    public void Add_assigns_Created_and_Updated_timestamps()
    {
        using var storage = NewStorage();
        var added = storage.Add(SampleEntry("x"));
        Assert.NotEqual(default, added.Entry.Created);
        Assert.Equal(added.Entry.Created, added.Entry.Updated);
    }

    [Fact]
    public void Add_same_path_twice_throws()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("github"));
        Assert.Throws<EntryAlreadyExistsException>(() => storage.Add(SampleEntry("github")));
    }

    [Fact]
    public void Get_missing_entry_throws()
    {
        using var storage = NewStorage();
        Assert.Throws<EntryNotFoundException>(() => storage.Get(new EntryPath("missing")));
    }

    [Fact]
    public void TryGet_missing_entry_returns_null()
    {
        using var storage = NewStorage();
        Assert.Null(storage.TryGet(new EntryPath("missing")));
    }

    [Fact]
    public void Update_preserves_Created_and_bumps_Updated()
    {
        using var storage = NewStorage();
        var original = storage.Add(SampleEntry("aws/prod"));
        Thread.Sleep(10);

        var renamed = original.Entry with { Title = "AWS Prod renamed" };
        var updated = storage.Update(renamed);

        Assert.Equal("AWS Prod renamed", updated.Entry.Title);
        Assert.Equal(original.Entry.Created, updated.Entry.Created);
        Assert.True(updated.Entry.Updated > original.Entry.Updated);
    }

    [Fact]
    public void Update_nonexistent_entry_throws()
    {
        using var storage = NewStorage();
        Assert.Throws<EntryNotFoundException>(() =>
            storage.Update(SampleEntry("never/added")));
    }

    [Fact]
    public void Remove_deletes_entry()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("personal/gmail"));
        storage.Remove(new EntryPath("personal/gmail"));
        Assert.Null(storage.TryGet(new EntryPath("personal/gmail")));
    }

    [Fact]
    public void Remove_missing_entry_throws()
    {
        using var storage = NewStorage();
        Assert.Throws<EntryNotFoundException>(() =>
            storage.Remove(new EntryPath("missing")));
    }

    [Fact]
    public void List_returns_all_entries_recursively()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("banking/mbank"));
        storage.Add(SampleEntry("banking/revolut"));
        storage.Add(SampleEntry("dev/github"));

        var all = storage.List();

        Assert.Equal(3, all.Count);
        Assert.Contains(all, e => e.Entry.Path.Value == "banking/mbank");
        Assert.Contains(all, e => e.Entry.Path.Value == "banking/revolut");
        Assert.Contains(all, e => e.Entry.Path.Value == "dev/github");
    }

    [Fact]
    public void List_under_path_restricts_to_subtree()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("banking/mbank"));
        storage.Add(SampleEntry("banking/revolut"));
        storage.Add(SampleEntry("dev/github"));

        var banking = storage.List(new EntryPath("banking"));

        Assert.Equal(2, banking.Count);
        Assert.All(banking, e => Assert.StartsWith("banking/", e.Entry.Path.Value));
    }

    [Fact]
    public void Search_finds_by_fuzzy_title_match()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("banking/mbank", title: "mBank Poland"));
        storage.Add(SampleEntry("dev/github", title: "GitHub"));

        var results = storage.Search("mbank pol");

        Assert.Single(results);
        Assert.Equal("mBank Poland", results[0].Entry.Title);
    }

    [Fact]
    public void Search_empty_query_returns_all()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("a"));
        storage.Add(SampleEntry("b"));

        var results = storage.Search("");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Notes_null_roundtrips_correctly()
    {
        using var storage = NewStorage();
        var entry = SampleEntry("misc/thing") with { NotesEncrypted = null };
        storage.Add(entry);

        var fetched = storage.Get(new EntryPath("misc/thing"));

        Assert.Null(fetched.Entry.NotesEncrypted);
    }

    [Fact]
    public void Disposed_storage_rejects_operations()
    {
        var storage = NewStorage();
        storage.Dispose();

        Assert.Throws<ObjectDisposedException>(() => storage.List());
        Assert.Throws<ObjectDisposedException>(() => storage.TryGet(new EntryPath("x")));
    }

    [Fact]
    public void File_is_written_as_snake_case_json()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("banking/mbank"));

        var file = Path.Combine(_tempRoot, "banking", "mbank.json");
        var json = File.ReadAllText(file);

        Assert.Contains("\"schema_version\"", json);
        Assert.Contains("\"password_age\"", json);
        Assert.Contains("\"notes_age\"", json);
        Assert.Contains("\"title\"", json);
    }
}
