using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PwVault.Core.Security;

[SupportedOSPlatform("windows")]
public sealed class WindowsSessionStore : ISessionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly TimeProvider _time;
    private readonly TimeSpan _initialTtl;
    private readonly TimeSpan _minTtlAfterUse;

    public string SessionFilePath { get; }

    public WindowsSessionStore(
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
        WriteEncrypted(model);
    }

    public string? TryGetAndExtend()
    {
        if (!File.Exists(SessionFilePath)) return null;

        SessionFileModel? model;
        try
        {
            var encrypted = File.ReadAllBytes(SessionFilePath);
            var plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plain);
            model = JsonSerializer.Deserialize<SessionFileModel>(json, SerializerOptions);
        }
        catch (CryptographicException)
        {
            Clear();
            return null;
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
            WriteEncrypted(model);
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

    private void WriteEncrypted(SessionFileModel model)
    {
        var directory = Path.GetDirectoryName(SessionFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(model, SerializerOptions);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(json),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        var tempPath = $"{SessionFilePath}.tmp.{Guid.NewGuid():N}";
        try
        {
            File.WriteAllBytes(tempPath, encrypted);
            File.Move(tempPath, SessionFilePath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private static string ResolveDefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData))
            throw new InvalidOperationException("LOCALAPPDATA is not available.");
        return Path.Combine(localAppData, "pwvault", "session");
    }
}
