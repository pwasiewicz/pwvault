namespace PwVault.Core.IO;

public sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{path}.tmp.{Guid.NewGuid():N}";
        File.WriteAllText(tempPath, content);
        try
        {
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public void DeleteFile(string path) => File.Delete(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
    {
        if (!Directory.Exists(directory))
            return [];

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.EnumerateFiles(directory, searchPattern, option);
    }

    public FileInfoSnapshot GetFileInfo(string path)
    {
        var info = new FileInfo(path);
        return new FileInfoSnapshot(
            FullPath: info.FullName,
            SizeBytes: info.Length,
            CreatedUtc: new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
            ModifiedUtc: new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
