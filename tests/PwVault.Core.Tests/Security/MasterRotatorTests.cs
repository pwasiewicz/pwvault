using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Security.Age;
using PwVault.Core.Storage;
using Xunit;

namespace PwVault.Core.Tests.Security;

public sealed class MasterRotatorTests : IDisposable
{
    private const string OldMaster = "correct horse battery staple";
    private const string NewMaster = "even better diceware string 2026";

    private readonly string _tempRoot;
    private readonly IFileSystem _fs = new RealFileSystem();
    private readonly FakeAgeGateway _age = new();

    public MasterRotatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "pwvault-rotate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        var sentinelAge = _age.Encrypt(VaultMetadataStore.SentinelPlaintext, OldMaster);
        VaultMetadataStore.Write(_tempRoot, _fs, new VaultMetadata(SchemaVersion: 1, SentinelAge: sentinelAge));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private VaultEntry Encrypted(string pathValue, string password, string? notes, string master)
    {
        var pwArmor = new EncryptedField(_age.Encrypt(password, master));
        EncryptedField? notesArmor = notes is null ? null : new EncryptedField(_age.Encrypt(notes, master));
        return new VaultEntry(
            Path: new EntryPath(pathValue),
            Title: pathValue,
            Username: null,
            Url: null,
            PasswordEncrypted: pwArmor,
            NotesEncrypted: notesArmor);
    }

    [Fact]
    public void Rotate_rewrites_all_entries_and_sentinel_with_new_master()
    {
        using (var seed = VaultStorage.Open(_tempRoot, _fs))
        {
            seed.Add(Encrypted("banking/mbank", "bank-pw", "bank-notes", OldMaster));
            seed.Add(Encrypted("dev/github", "gh-pw", null, OldMaster));
        }

        MasterRotationResult result;
        using (var storage = VaultStorage.Open(_tempRoot, _fs))
        {
            result = MasterRotator.Rotate(storage, _age, _fs, _tempRoot, OldMaster, NewMaster);
        }

        Assert.Equal(2, result.EntriesRewritten);

        using var verify = VaultStorage.Open(_tempRoot, _fs);
        var mbank = verify.Get(new EntryPath("banking/mbank"));
        Assert.Equal("bank-pw", _age.Decrypt(mbank.Entry.PasswordEncrypted.AsciiArmor, NewMaster));
        Assert.Equal("bank-notes", _age.Decrypt(mbank.Entry.NotesEncrypted!.Value.AsciiArmor, NewMaster));

        var github = verify.Get(new EntryPath("dev/github"));
        Assert.Equal("gh-pw", _age.Decrypt(github.Entry.PasswordEncrypted.AsciiArmor, NewMaster));
        Assert.Null(github.Entry.NotesEncrypted);

        var metadata = VaultMetadataStore.Read(_tempRoot, _fs);
        Assert.Equal(VaultMetadataStore.SentinelPlaintext, _age.Decrypt(metadata.SentinelAge, NewMaster));
        Assert.Throws<InvalidPassphraseException>(() => _age.Decrypt(metadata.SentinelAge, OldMaster));
    }

    [Fact]
    public void Rotate_with_wrong_old_master_throws_without_touching_entries()
    {
        using (var seed = VaultStorage.Open(_tempRoot, _fs))
        {
            seed.Add(Encrypted("a", "pw", null, OldMaster));
        }

        using var storage = VaultStorage.Open(_tempRoot, _fs);
        Assert.Throws<InvalidPassphraseException>(() =>
            MasterRotator.Rotate(storage, _age, _fs, _tempRoot, "not-the-master", NewMaster));

        var entry = storage.Get(new EntryPath("a"));
        Assert.Equal("pw", _age.Decrypt(entry.Entry.PasswordEncrypted.AsciiArmor, OldMaster));
    }

    [Fact]
    public void Rotate_empty_vault_only_rewrites_sentinel()
    {
        using (var storage = VaultStorage.Open(_tempRoot, _fs))
        {
            var result = MasterRotator.Rotate(storage, _age, _fs, _tempRoot, OldMaster, NewMaster);
            Assert.Equal(0, result.EntriesRewritten);
        }

        var metadata = VaultMetadataStore.Read(_tempRoot, _fs);
        Assert.Equal(VaultMetadataStore.SentinelPlaintext, _age.Decrypt(metadata.SentinelAge, NewMaster));
    }

    [Fact]
    public void Rotate_reports_progress_for_each_entry()
    {
        using (var seed = VaultStorage.Open(_tempRoot, _fs))
        {
            seed.Add(Encrypted("a", "pw-a", null, OldMaster));
            seed.Add(Encrypted("b", "pw-b", null, OldMaster));
            seed.Add(Encrypted("c", "pw-c", null, OldMaster));
        }

        var progress = new List<(int done, int total)>();
        using (var storage = VaultStorage.Open(_tempRoot, _fs))
        {
            MasterRotator.Rotate(storage, _age, _fs, _tempRoot, OldMaster, NewMaster,
                onProgress: (done, total) => progress.Add((done, total)));
        }

        Assert.Equal((0, 3), progress[0]);
        Assert.Equal((3, 3), progress[^1]);
        Assert.All(progress, p => Assert.Equal(3, p.total));
    }
}
