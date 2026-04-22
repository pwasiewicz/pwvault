using System.Text;
using PwVault.Core.Security;

namespace PwVault.Core.Tests.Security;

internal sealed class FakeAgeGateway : IAgeGateway
{
    private const string ArmorHeader = "-----BEGIN AGE ENCRYPTED FILE-----";
    private const string ArmorFooter = "-----END AGE ENCRYPTED FILE-----";

    public int DecryptCalls { get; private set; }
    public int EncryptCalls { get; private set; }
    public string? LastPassphrase { get; private set; }

    public string Encrypt(string plaintext, string passphrase)
    {
        EncryptCalls++;
        LastPassphrase = passphrase;
        var payload = $"{passphrase}\0{plaintext}";
        var body = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return $"{ArmorHeader}\n{body}\n{ArmorFooter}";
    }

    public string Decrypt(string asciiArmor, string passphrase)
    {
        DecryptCalls++;
        LastPassphrase = passphrase;

        var body = asciiArmor
            .Replace(ArmorHeader, "")
            .Replace(ArmorFooter, "")
            .Trim();

        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(body));
        var separatorIndex = payload.IndexOf('\0');
        if (separatorIndex < 0)
            throw new InvalidOperationException("Malformed fake ciphertext.");

        var embeddedPassphrase = payload[..separatorIndex];
        var plaintext = payload[(separatorIndex + 1)..];

        if (embeddedPassphrase != passphrase)
            throw new InvalidOperationException("Wrong passphrase.");

        return plaintext;
    }
}
