using PwVault.Core.Domain;
using PwVault.Core.Security;
using Xunit;

namespace PwVault.Core.Tests.Security;

public class CryptoServiceTests
{
    private readonly FakeAgeGateway _age = new();
    private readonly FakeSessionStore _session = new();

    private CryptoService NewService() => new(_session, _age);

    private VaultEntry BuildEntry(
        string password = "secret-pass",
        string? notes = "secret-notes",
        string passphrase = "master")
    {
        EncryptedField pwd = new(_age.Encrypt(password, passphrase));
        EncryptedField? notesField = notes is null ? null : new EncryptedField(_age.Encrypt(notes, passphrase));
        return new VaultEntry(
            Path: new EntryPath("test/entry"),
            Title: "Test",
            Username: "u",
            Url: "https://example.com",
            PasswordEncrypted: pwd,
            NotesEncrypted: notesField);
    }

    [Fact]
    public void DecryptPassword_with_provided_master_returns_success()
    {
        var entry = BuildEntry(password: "pwd-1", passphrase: "master-a");
        // reset gateway counters after setup encryption
        var service = NewService();
        var before = _age.DecryptCalls;

        var result = service.DecryptPassword(entry, masterPassword: "master-a");

        Assert.Equal(DecryptionStatus.Success, result.Status);
        Assert.Equal("pwd-1", result.PlainText);
        Assert.Equal(before + 1, _age.DecryptCalls);
        Assert.Equal(0, _session.GetCount);
    }

    [Fact]
    public void DecryptPassword_with_null_master_uses_session()
    {
        var entry = BuildEntry(password: "pwd-2", passphrase: "master-b");
        _session.Master = "master-b";
        var service = NewService();

        var result = service.DecryptPassword(entry);

        Assert.Equal(DecryptionStatus.Success, result.Status);
        Assert.Equal("pwd-2", result.PlainText);
        Assert.Equal(1, _session.GetCount);
    }

    [Fact]
    public void DecryptPassword_without_master_and_empty_session_returns_master_needed()
    {
        var entry = BuildEntry();
        var service = NewService();
        var beforeAge = _age.DecryptCalls;

        var result = service.DecryptPassword(entry);

        Assert.Equal(DecryptionStatus.MasterNeeded, result.Status);
        Assert.Null(result.PlainText);
        Assert.Equal(beforeAge, _age.DecryptCalls);
    }

    [Fact]
    public void DecryptNotes_returns_success_with_null_when_entry_has_no_notes()
    {
        var entry = BuildEntry(notes: null);
        var service = NewService();

        var result = service.DecryptNotes(entry, masterPassword: "master");

        Assert.Equal(DecryptionStatus.Success, result.Status);
        Assert.Null(result.PlainText);
    }

    [Fact]
    public void DecryptNotes_with_provided_master_returns_plaintext()
    {
        var entry = BuildEntry(notes: "security-answers", passphrase: "m");
        var service = NewService();

        var result = service.DecryptNotes(entry, masterPassword: "m");

        Assert.Equal(DecryptionStatus.Success, result.Status);
        Assert.Equal("security-answers", result.PlainText);
    }

    [Fact]
    public void DecryptNotes_without_master_and_empty_session_returns_master_needed()
    {
        var entry = BuildEntry(notes: "notes");
        var service = NewService();

        var result = service.DecryptNotes(entry);

        Assert.Equal(DecryptionStatus.MasterNeeded, result.Status);
        Assert.Null(result.PlainText);
    }

    [Fact]
    public void Provided_master_is_preferred_over_session()
    {
        var entry = BuildEntry(password: "p", passphrase: "correct");
        _session.Master = "wrong";
        var service = NewService();

        var result = service.DecryptPassword(entry, masterPassword: "correct");

        Assert.Equal("p", result.PlainText);
        Assert.Equal(0, _session.GetCount);
    }
}
