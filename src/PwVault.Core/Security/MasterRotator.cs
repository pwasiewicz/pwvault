using PwVault.Core.Domain;
using PwVault.Core.IO;
using PwVault.Core.Security.Age;
using PwVault.Core.Storage;

namespace PwVault.Core.Security;

public sealed record MasterRotationResult(int EntriesRewritten);

public static class MasterRotator
{
    public static MasterRotationResult Rotate(
        IVaultStorage storage,
        IAgeGateway age,
        IFileSystem fileSystem,
        string vaultRoot,
        string oldMaster,
        string newMaster,
        Action<int, int>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(age);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);
        ArgumentException.ThrowIfNullOrEmpty(oldMaster);
        ArgumentException.ThrowIfNullOrEmpty(newMaster);

        var metadata = VaultMetadataStore.Read(vaultRoot, fileSystem);
        var decryptedSentinel = age.Decrypt(metadata.SentinelAge, oldMaster);
        if (decryptedSentinel != VaultMetadataStore.SentinelPlaintext)
            throw new InvalidPassphraseException();

        var entries = storage.List();
        var total = entries.Count;
        onProgress?.Invoke(0, total);

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i].Entry;

            var plainPassword = age.Decrypt(entry.PasswordEncrypted.AsciiArmor, oldMaster);
            var reencryptedPassword = new EncryptedField(age.Encrypt(plainPassword, newMaster));

            EncryptedField? reencryptedNotes = null;
            if (entry.NotesEncrypted is { } notes)
            {
                var plainNotes = age.Decrypt(notes.AsciiArmor, oldMaster);
                reencryptedNotes = new EncryptedField(age.Encrypt(plainNotes, newMaster));
            }

            var rotated = entry with
            {
                PasswordEncrypted = reencryptedPassword,
                NotesEncrypted = reencryptedNotes,
            };
            storage.Update(rotated);

            onProgress?.Invoke(i + 1, total);
        }

        var newSentinel = age.Encrypt(VaultMetadataStore.SentinelPlaintext, newMaster);
        VaultMetadataStore.Write(vaultRoot, fileSystem, metadata with { SentinelAge = newSentinel });

        return new MasterRotationResult(total);
    }
}
