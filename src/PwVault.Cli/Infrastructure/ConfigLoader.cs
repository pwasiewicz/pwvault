using System.Text.Json;

namespace PwVault.Cli.Infrastructure;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static PwVaultConfig Load()
    {
        var config = LoadFromFile() ?? new PwVaultConfig();

        var envPath = Environment.GetEnvironmentVariable("PWVAULT_PATH");
        if (!string.IsNullOrEmpty(envPath)) config.VaultPath = envPath;

        return config;
    }

    public static PwVaultConfig? LoadFromFile()
    {
        var configPath = GetConfigPath();
        if (!File.Exists(configPath)) return null;

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<PwVaultConfig>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Save(PwVaultConfig config)
    {
        var configPath = GetConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        var temp = configPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, configPath, overwrite: true);
    }

    public static string GetConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "pwvault", "config.json");
        }

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfig))
            return Path.Combine(xdgConfig, "pwvault", "config.json");

        var home = Environment.GetEnvironmentVariable("HOME")
            ?? throw new InvalidOperationException("HOME environment variable is not set.");
        return Path.Combine(home, ".config", "pwvault", "config.json");
    }
}
