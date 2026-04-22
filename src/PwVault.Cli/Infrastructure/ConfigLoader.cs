using System.Text.Json;

namespace PwVault.Cli.Infrastructure;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static PwVaultConfig Load()
    {
        var config = new PwVaultConfig();
        var configPath = GetConfigPath();

        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                var loaded = JsonSerializer.Deserialize<PwVaultConfig>(json, SerializerOptions);
                if (loaded is not null) config = loaded;
            }
            catch (JsonException)
            {
                // invalid config — use defaults
            }
        }

        var envPath = Environment.GetEnvironmentVariable("PWVAULT_PATH");
        if (!string.IsNullOrEmpty(envPath)) config.VaultPath = envPath;

        return config;
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
