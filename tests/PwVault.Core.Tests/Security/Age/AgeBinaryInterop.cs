using System.Diagnostics;

namespace PwVault.Core.Tests.Security.Age;

internal static class AgeBinaryInterop
{
    private static readonly Lazy<bool> AvailableLazy = new(() =>
        !OperatingSystem.IsWindows()
        && ExistsInPath("age")
        && ExistsInPath("script"));

    public static bool IsAvailable => AvailableLazy.Value;

    public static string Encrypt(string plaintext, string passphrase)
    {
        var tempDir = CreateTempDir();
        try
        {
            var inFile = Path.Combine(tempDir, "in.txt");
            var outFile = Path.Combine(tempDir, "out.age");
            File.WriteAllText(inFile, plaintext);

            var inner = $"age -p -a -o '{outFile}' '{inFile}'";
            RunScript(inner, $"{passphrase}\n{passphrase}\n", timeoutMs: 30_000);

            if (!File.Exists(outFile))
                throw new InvalidOperationException($"age did not produce output file at '{outFile}'.");
            return File.ReadAllText(outFile);
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    public static string Decrypt(string ciphertext, string passphrase)
    {
        var tempDir = CreateTempDir();
        try
        {
            var inFile = Path.Combine(tempDir, "in.age");
            var outFile = Path.Combine(tempDir, "out.txt");
            File.WriteAllText(inFile, ciphertext);

            var inner = $"age -d -o '{outFile}' '{inFile}'";
            RunScript(inner, $"{passphrase}\n", timeoutMs: 30_000);

            if (!File.Exists(outFile))
                throw new InvalidOperationException($"age did not produce output file at '{outFile}'.");
            return File.ReadAllText(outFile);
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    private static void RunScript(string innerCommand, string stdin, int timeoutMs)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "script",
            Arguments = $"-qec \"{innerCommand}\" /dev/null",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'script' process.");

        process.StandardInput.Write(stdin);
        process.StandardInput.Close();

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"age invocation timed out after {timeoutMs}ms.");
        }

        if (process.ExitCode != 0)
        {
            var stderr = process.StandardError.ReadToEnd();
            var stdout = process.StandardOutput.ReadToEnd();
            throw new InvalidOperationException(
                $"age invocation failed (exit {process.ExitCode}): {stderr} | {stdout}");
        }
    }

    private static bool ExistsInPath(string command)
    {
        try
        {
            var psi = new ProcessStartInfo("which", command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi)!;
            if (!p.WaitForExit(2000)) return false;
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "pwvault-age-interop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort */ }
    }
}
