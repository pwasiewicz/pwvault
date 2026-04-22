namespace PwVault.Core.IO;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    string ReadAllText(string path);
    void WriteAllTextAtomic(string path, string content);
    void DeleteFile(string path);
    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive);
    FileInfoSnapshot GetFileInfo(string path);
}
