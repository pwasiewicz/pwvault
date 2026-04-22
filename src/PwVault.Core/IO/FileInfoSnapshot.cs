namespace PwVault.Core.IO;

public sealed record FileInfoSnapshot(
    string FullPath,
    long SizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc);
