using PwVault.Core.Domain;

namespace PwVault.Core.Storage;

public class VaultStorageException : Exception
{
    public VaultStorageException(string message) : base(message) { }
    public VaultStorageException(string message, Exception inner) : base(message, inner) { }
}

public sealed class EntryNotFoundException(EntryPath path)
    : VaultStorageException($"Entry not found: '{path.Value}'.")
{
    public EntryPath Path { get; } = path;
}

public sealed class EntryAlreadyExistsException(EntryPath path)
    : VaultStorageException($"Entry already exists: '{path.Value}'.")
{
    public EntryPath Path { get; } = path;
}
