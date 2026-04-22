namespace PwVault.Core.Domain;

public readonly record struct EncryptedField
{
    private const string ArmorHeader = "-----BEGIN AGE ENCRYPTED FILE-----";
    private const string ArmorFooter = "-----END AGE ENCRYPTED FILE-----";

    public string AsciiArmor { get; }

    public EncryptedField(string asciiArmor)
    {
        ArgumentNullException.ThrowIfNull(asciiArmor);
        var trimmed = asciiArmor.Trim();
        if (!IsValid(trimmed))
            throw new ArgumentException(
                "Value must be an ASCII-armored age ciphertext (BEGIN/END markers required).",
                nameof(asciiArmor));
        AsciiArmor = trimmed;
    }

    public static bool IsValid(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith(ArmorHeader, StringComparison.Ordinal)
        && value.Contains(ArmorFooter, StringComparison.Ordinal);

    public override string ToString() => AsciiArmor;
}
