namespace PwVault.Core.Domain;

public sealed record EntryMetadata(
    string AbsoluteFilePath,
    long FileSizeBytes,
    DateTimeOffset FileCreatedUtc,
    DateTimeOffset FileModifiedUtc);
