using System.Text.Json;

namespace PwVault.Core.Security;

public sealed class FileSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly TimeProvider _time;
    private readonly TimeSpan _initialTtl;
    private readonly TimeSpan _minTtlAfterUse;

    public string SessionFilePath { get; }

    public FileSessionStore(
        string? sessionFilePath = null,
        TimeProvider? time = null,
        TimeSpan? initialTtl = null,
        TimeSpan? minTtlAfterUse = null)
    {
        SessionFilePath = sessionFilePath ?? ResolveDefaultPath();
        _time = time ?? TimeProvider.System;
        _initialTtl = initialTtl ?? SessionTtl.Initial;
        _minTtlAfterUse = minTtlAfterUse ?? SessionTtl.MinAfterUse;
    }

    public void Save(string masterPassword)
    {
        ArgumentNullException.ThrowIfNull(masterPassword);
        var model = new SessionFileModel
        {
            MasterPassword = masterPassword,
            ExpiresAt = _time.GetUtcNow().Add(_initialTtl),
        };
        WriteAtomicOwnerOnly(model);
    }

    public string? TryGetAndExtend()
    {
        if (!File.Exists(SessionFilePath)) return null;

        SessionFileModel? model;
        try
        {
            var json = File.ReadAllText(SessionFilePath);
            model = JsonSerializer.Deserialize<SessionFileModel>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            Clear();
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        if (model is null)
        {
            Clear();
            return null;
        }

        var now = _time.GetUtcNow();
        if (model.ExpiresAt <= now)
        {
            Clear();
            return null;
        }

        var minExpiration = now.Add(_minTtlAfterUse);
        if (model.ExpiresAt < minExpiration)
        {
            model.ExpiresAt = minExpiration;
            WriteAtomicOwnerOnly(model);
        }

        return model.MasterPassword;
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(SessionFilePath))
                File.Delete(SessionFilePath);
        }
        catch (IOException) { /* best-effort */ }
        catch (UnauthorizedAccessException) { /* best-effort */ }
    }

    private void WriteAtomicOwnerOnly(SessionFileModel model)
    {
        var directory = Path.GetDirectoryName(SessionFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(model, SerializerOptions);
        var tempPath = $"{SessionFilePath}.tmp.{Guid.NewGuid():N}";

        try
        {
            WriteWithOwnerOnlyPermissions(tempPath, json);
            File.Move(tempPath, SessionFilePath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void WriteWithOwnerOnlyPermissions(string path, string content)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }

    private static string ResolveDefaultPath()
    {
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                ?? throw new InvalidOperationException("HOME environment variable is not set.");
            return Path.Combine(home, "Library", "Caches", "pwvault", "session");
        }

        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runtimeDir))
            return Path.Combine(runtimeDir, "pwvault", "session");

        var homePath = Environment.GetEnvironmentVariable("HOME")
            ?? throw new InvalidOperationException(
                "Neither XDG_RUNTIME_DIR nor HOME environment variable is set.");
        return Path.Combine(homePath, ".cache", "pwvault", "session");
    }
}
