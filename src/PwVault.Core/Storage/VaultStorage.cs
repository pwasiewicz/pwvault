using System.Text.Json;
using System.Text.Json.Serialization;
using FuzzySharp;
using PwVault.Core.Domain;
using PwVault.Core.IO;

namespace PwVault.Core.Storage;

public sealed class VaultStorage : IVaultStorage
{
    private const string EntryFileExtension = ".json";
    private const int FuzzyScoreThreshold = 60;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IFileSystem _fs;
    private readonly TimeProvider _time;
    private bool _disposed;

    public string RootPath { get; }

    private VaultStorage(string rootPath, IFileSystem fileSystem, TimeProvider timeProvider)
    {
        if (!fileSystem.DirectoryExists(rootPath))
            throw new VaultStorageException($"Vault root does not exist: '{rootPath}'.");

        RootPath = rootPath;
        _fs = fileSystem;
        _time = timeProvider;
    }

    public static VaultStorage Open(
        string rootPath,
        IFileSystem? fileSystem = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        return new VaultStorage(
            rootPath,
            fileSystem ?? new RealFileSystem(),
            timeProvider ?? TimeProvider.System);
    }

    public IReadOnlyList<StoredEntry> List(EntryPath? underPath = null, IReadOnlyList<string>? tags = null)
    {
        EnsureNotDisposed();
        var directory = underPath is null
            ? RootPath
            : CombinePath(RootPath, underPath.Value.Value);

        if (!_fs.DirectoryExists(directory))
            return [];

        var filter = TagNormalizer.Normalize(tags);
        var result = new List<StoredEntry>();
        foreach (var file in _fs.EnumerateFiles(directory, $"*{EntryFileExtension}", recursive: true))
        {
            if (IsReservedFile(file)) continue;
            var stored = LoadEntryFromFile(file);
            if (stored is null) continue;
            if (!MatchesAllTags(stored.Entry, filter)) continue;
            result.Add(stored);
        }
        return result;
    }

    private static bool MatchesAllTags(VaultEntry entry, IReadOnlyList<string> requiredTags)
    {
        if (requiredTags.Count == 0) return true;
        foreach (var required in requiredTags)
        {
            if (!entry.Tags.Contains(required, StringComparer.Ordinal)) return false;
        }
        return true;
    }

    private static bool IsReservedFile(string fullPath) =>
        Path.GetFileName(fullPath).StartsWith('.');

    public StoredEntry? TryGet(EntryPath path)
    {
        EnsureNotDisposed();
        var file = GetFilePath(path);
        return _fs.FileExists(file) ? LoadEntryFromFile(file) : null;
    }

    public StoredEntry Get(EntryPath path) =>
        TryGet(path) ?? throw new EntryNotFoundException(path);

    public StoredEntry Add(VaultEntry entry)
    {
        EnsureNotDisposed();
        var file = GetFilePath(entry.Path);
        if (_fs.FileExists(file))
            throw new EntryAlreadyExistsException(entry.Path);

        var now = _time.GetUtcNow();
        var withTimestamps = entry with { Created = now, Updated = now };
        WriteEntry(withTimestamps, file);

        var stored = new StoredEntry(withTimestamps, BuildMetadata(file));
        VerifyRoundtrip(stored);
        return stored;
    }

    public StoredEntry Update(VaultEntry entry)
    {
        EnsureNotDisposed();
        var file = GetFilePath(entry.Path);
        var existing = TryGet(entry.Path) ?? throw new EntryNotFoundException(entry.Path);

        var now = _time.GetUtcNow();
        var updated = entry with { Created = existing.Entry.Created, Updated = now };
        WriteEntry(updated, file);

        var stored = new StoredEntry(updated, BuildMetadata(file));
        VerifyRoundtrip(stored);
        return stored;
    }

    public void Remove(EntryPath path)
    {
        EnsureNotDisposed();
        var file = GetFilePath(path);
        if (!_fs.FileExists(file))
            throw new EntryNotFoundException(path);

        _fs.DeleteFile(file);
    }

    public IReadOnlyList<StoredEntry> Search(string query, int maxResults = 20, IReadOnlyList<string>? tags = null)
    {
        EnsureNotDisposed();
        var candidates = List(tags: tags);
        if (string.IsNullOrWhiteSpace(query))
            return candidates;

        return candidates
            .Select(stored => (stored, score: ScoreEntry(stored.Entry, query)))
            .Where(x => x.score >= FuzzyScoreThreshold)
            .OrderByDescending(x => x.score)
            .Take(maxResults)
            .Select(x => x.stored)
            .ToList();
    }

    public IReadOnlyList<TagCount> ListTags()
    {
        EnsureNotDisposed();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stored in List())
        {
            foreach (var tag in stored.Entry.Tags)
            {
                counts[tag] = counts.TryGetValue(tag, out var c) ? c + 1 : 1;
            }
        }

        return counts
            .Select(kv => new TagCount(kv.Key, kv.Value))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag, StringComparer.Ordinal)
            .ToList();
    }

    private static int ScoreEntry(VaultEntry entry, string query)
    {
        var haystackParts = new[]
        {
            entry.Path.Value,
            entry.Title,
            entry.Username ?? "",
            entry.Url ?? ""
        };
        var haystack = string.Join(' ', haystackParts.Where(s => !string.IsNullOrWhiteSpace(s)));
        return Fuzz.PartialRatio(query, haystack);
    }

    private string GetFilePath(EntryPath path) =>
        CombinePath(RootPath, path.Value) + EntryFileExtension;

    private static string CombinePath(string root, string relative) =>
        Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private StoredEntry? LoadEntryFromFile(string file)
    {
        var json = _fs.ReadAllText(file);
        var model = JsonSerializer.Deserialize<EntryFileModel>(json, SerializerOptions);
        if (model is null) return null;

        var path = DeriveEntryPath(file);
        return new StoredEntry(EntryMapper.ToDomain(model, path), BuildMetadata(file));
    }

    private EntryPath DeriveEntryPath(string fullFilePath)
    {
        var info = _fs.GetFileInfo(fullFilePath);
        var relative = Path.GetRelativePath(RootPath, info.FullPath);
        var withoutExtension = relative[..^EntryFileExtension.Length];
        var logical = withoutExtension.Replace(Path.DirectorySeparatorChar, '/');
        return new EntryPath(logical);
    }

    private EntryMetadata BuildMetadata(string file)
    {
        var info = _fs.GetFileInfo(file);
        return new EntryMetadata(
            AbsoluteFilePath: info.FullPath,
            FileSizeBytes: info.SizeBytes,
            FileCreatedUtc: info.CreatedUtc,
            FileModifiedUtc: info.ModifiedUtc);
    }

    private void WriteEntry(VaultEntry entry, string file)
    {
        var model = EntryMapper.ToFile(entry);
        var json = JsonSerializer.Serialize(model, SerializerOptions);
        _fs.WriteAllTextAtomic(file, json);
    }

    private void VerifyRoundtrip(StoredEntry stored)
    {
        var reloaded = TryGet(stored.Entry.Path)
            ?? throw new VaultStorageException(
                $"Roundtrip verification failed: entry '{stored.Entry.Path.Value}' not found after write.");

        if (!EntryDataEqual(stored.Entry, reloaded.Entry))
            throw new VaultStorageException(
                $"Roundtrip verification failed: data mismatch for '{stored.Entry.Path.Value}' after write.");
    }

    private static bool EntryDataEqual(VaultEntry a, VaultEntry b) =>
        a.Path == b.Path
        && a.Title == b.Title
        && a.Username == b.Username
        && a.Url == b.Url
        && a.PasswordEncrypted == b.PasswordEncrypted
        && a.NotesEncrypted == b.NotesEncrypted
        && a.Tags.SequenceEqual(b.Tags, StringComparer.Ordinal)
        && a.Created == b.Created
        && a.Updated == b.Updated;

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
