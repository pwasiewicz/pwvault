namespace PwVault.Core.Domain;

public sealed record StoredEntry(VaultEntry Entry, EntryMetadata Metadata);
