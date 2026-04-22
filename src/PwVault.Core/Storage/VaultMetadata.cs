using System.Text.Json;
using System.Text.Json.Serialization;
using PwVault.Core.IO;

namespace PwVault.Core.Storage;

public sealed record VaultMetadata(int SchemaVersion, string SentinelAge);

public static class VaultMetadataStore
{
    public const string MetadataFileName = ".vault.json";
    public const string SentinelPlaintext = "pwvault-sentinel-v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static bool Exists(string vaultRoot, IFileSystem fs) =>
        fs.FileExists(GetPath(vaultRoot));

    public static VaultMetadata Read(string vaultRoot, IFileSystem fs)
    {
        var path = GetPath(vaultRoot);
        if (!fs.FileExists(path))
            throw new VaultStorageException($"Missing vault metadata file at '{path}'. Did you run 'pwvault init'?");

        var json = fs.ReadAllText(path);
        var model = JsonSerializer.Deserialize<VaultMetadataModel>(json, SerializerOptions)
            ?? throw new VaultStorageException($"Invalid vault metadata at '{path}'.");
        if (string.IsNullOrWhiteSpace(model.SentinelAge))
            throw new VaultStorageException($"Vault metadata at '{path}' is missing 'sentinel_age'.");
        return new VaultMetadata(model.SchemaVersion, model.SentinelAge);
    }

    public static void Write(string vaultRoot, IFileSystem fs, VaultMetadata metadata)
    {
        var model = new VaultMetadataModel
        {
            SchemaVersion = metadata.SchemaVersion,
            SentinelAge = metadata.SentinelAge,
        };
        var json = JsonSerializer.Serialize(model, SerializerOptions);
        fs.WriteAllTextAtomic(GetPath(vaultRoot), json);
    }

    public static string GetPath(string vaultRoot) => Path.Combine(vaultRoot, MetadataFileName);

    internal sealed class VaultMetadataModel
    {
        public int SchemaVersion { get; set; } = 1;
        public string SentinelAge { get; set; } = "";
    }
}
