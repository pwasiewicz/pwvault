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

    [Fact]
    public void Tags_roundtrip_normalized_and_sorted()
    {
        using var storage = NewStorage();
        var entry = SampleEntry("banking/mbank") with { Tags = new[] { "Banking", "money", "banking" } };
        storage.Add(entry);

        var fetched = storage.Get(new EntryPath("banking/mbank"));

        Assert.Equal(new[] { "banking", "money" }, fetched.Entry.Tags);
    }

    [Fact]
    public void Tags_empty_not_written_to_json()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("misc/thing"));

        var file = Path.Combine(_tempRoot, "misc", "thing.json");
        var json = File.ReadAllText(file);

        Assert.DoesNotContain("\"tags\"", json);
    }

    [Fact]
    public void Tags_non_empty_written_as_json_array()
    {
        using var storage = NewStorage();
        var entry = SampleEntry("banking/mbank") with { Tags = new[] { "money", "2fa" } };
        storage.Add(entry);

        var file = Path.Combine(_tempRoot, "banking", "mbank.json");
        var json = File.ReadAllText(file);

        Assert.Contains("\"tags\"", json);
        Assert.Contains("\"2fa\"", json);
        Assert.Contains("\"money\"", json);
    }

    [Fact]
    public void List_filters_by_tag()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("banking/mbank") with { Tags = new[] { "money", "2fa" } });
        storage.Add(SampleEntry("dev/github") with { Tags = new[] { "work" } });
        storage.Add(SampleEntry("dev/aws") with { Tags = new[] { "work", "2fa" } });

        var twofa = storage.List(tags: new[] { "2fa" });
        Assert.Equal(2, twofa.Count);
        Assert.Contains(twofa, e => e.Entry.Path.Value == "banking/mbank");
        Assert.Contains(twofa, e => e.Entry.Path.Value == "dev/aws");
    }

    [Fact]
    public void List_filters_by_multiple_tags_with_AND()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("a") with { Tags = new[] { "work", "2fa" } });
        storage.Add(SampleEntry("b") with { Tags = new[] { "work" } });
        storage.Add(SampleEntry("c") with { Tags = new[] { "2fa" } });

        var matches = storage.List(tags: new[] { "work", "2fa" });

        Assert.Single(matches);
        Assert.Equal("a", matches[0].Entry.Path.Value);
    }

    [Fact]
    public void List_with_tag_filter_normalizes_input()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("x") with { Tags = new[] { "Work" } });

        var matches = storage.List(tags: new[] { "WORK" });
        Assert.Single(matches);
    }

    [Fact]
    public void Search_combines_fuzzy_query_with_tag_filter()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("banking/mbank", title: "mBank Poland") with { Tags = new[] { "money" } });
        storage.Add(SampleEntry("dev/github", title: "mBank Mock") with { Tags = new[] { "work" } });

        var results = storage.Search("mbank", tags: new[] { "money" });

        Assert.Single(results);
        Assert.Equal("banking/mbank", results[0].Entry.Path.Value);
    }

    [Fact]
    public void Search_empty_query_with_tag_filter_returns_matching_entries()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("a") with { Tags = new[] { "work" } });
        storage.Add(SampleEntry("b") with { Tags = new[] { "home" } });

        var results = storage.Search("", tags: new[] { "work" });

        Assert.Single(results);
    }

    [Fact]
    public void ListTags_aggregates_counts_sorted_by_count_desc()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("a") with { Tags = new[] { "work", "2fa" } });
        storage.Add(SampleEntry("b") with { Tags = new[] { "work" } });
        storage.Add(SampleEntry("c") with { Tags = new[] { "work", "2fa" } });

        var tags = storage.ListTags();

        Assert.Equal(2, tags.Count);
        Assert.Equal("work", tags[0].Tag);
        Assert.Equal(3, tags[0].Count);
        Assert.Equal("2fa", tags[1].Tag);
        Assert.Equal(2, tags[1].Count);
    }

    [Fact]
    public void ListTags_empty_when_no_entries_tagged()
    {
        using var storage = NewStorage();
        storage.Add(SampleEntry("a"));

        Assert.Empty(storage.ListTags());
    }
}
