using System.Globalization;

namespace PwVault.Cli.Infrastructure;

public static class VaultBackup
{
    public static string Create(string vaultPath, DateTimeOffset now)
    {
        var stamp = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var backupPath = $"{vaultPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}.backup.{stamp}";

        CopyDirectory(vaultPath, backupPath);
        return backupPath;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            var targetDir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
            File.Copy(file, target, overwrite: true);
        }
    }
}
