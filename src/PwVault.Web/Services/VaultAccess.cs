using Microsoft.Extensions.Options;
using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Security;
using PwVault.Core.Security.Age;
using PwVault.Core.Storage;

namespace PwVault.Web.Services;

public sealed class VaultAccess
{
    private const string SentinelPlaintext = "pwvault-sentinel-v1";

    private readonly VaultOptions _options;
    private readonly IFileSystem _fs;
    private readonly IAgeGateway _age;

    public VaultAccess(IOptions<VaultOptions> options, IFileSystem fs, IAgeGateway age)
    {
        _options = options.Value;
        _fs = fs;
        _age = age;
    }

    public string VaultPath => _options.VaultPath;

    public bool TryUnlock(string master, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(master))
        {
            error = "Master password cannot be empty.";
            return false;
        }

        if (!_fs.DirectoryExists(_options.VaultPath))
        {
            error = $"Vault directory does not exist: {_options.VaultPath}";
            return false;
        }

        var metadataPath = Path.Combine(_options.VaultPath, ".vault.json");
        if (!_fs.FileExists(metadataPath))
        {
            error = "Vault is not initialized (.vault.json missing).";
            return false;
        }

        var metadataJson = _fs.ReadAllText(metadataPath);
        var sentinelAge = ExtractSentinel(metadataJson);
        if (sentinelAge is null)
        {
            error = "Vault metadata is unreadable.";
            return false;
        }

        try
        {
            var decrypted = _age.Decrypt(sentinelAge, master);
            if (decrypted != SentinelPlaintext)
            {
                error = "Sentinel mismatch — wrong master password.";
                return false;
            }
            return true;
        }
        catch (InvalidPassphraseException)
        {
            error = "Wrong master password.";
            return false;
        }
        catch (AgeDecryptionException ex)
        {
            error = $"Decryption failed: {ex.Message}";
            return false;
        }
    }

    public IReadOnlyList<StoredEntry> ListEntries()
    {
        using var storage = VaultStorage.Open(_options.VaultPath, _fs);
        return storage.List()
            .OrderBy(e => e.Entry.Path.Value, StringComparer.Ordinal)
            .ToList();
    }

    public string DecryptPassword(VaultEntry entry, string master) =>
        _age.Decrypt(entry.PasswordEncrypted.AsciiArmor, master);

    public string? DecryptNotes(VaultEntry entry, string master) =>
        entry.NotesEncrypted is { } notes
            ? _age.Decrypt(notes.AsciiArmor, master)
            : null;

    private static string? ExtractSentinel(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("sentinel_age", out var prop)
                ? prop.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
