namespace PwVault.Core.Security;

public interface IAgeGateway
{
    string Decrypt(string asciiArmor, string passphrase);

    string Encrypt(string plaintext, string passphrase);
}
