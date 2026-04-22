using System.Text;
using Org.BouncyCastle.Crypto.Generators;

namespace PwVault.Core.Security.Age;

internal static class ScryptKdf
{
    public const string AgeLabel = "age-encryption.org/v1/scrypt";
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const int R = 8;
    public const int P = 1;

    public static byte[] Derive(string passphrase, ReadOnlySpan<byte> salt, int workFactor)
    {
        if (salt.Length != SaltSize)
            throw new ArgumentException($"Salt must be {SaltSize} bytes.", nameof(salt));
        if (workFactor < 1)
            throw new ArgumentOutOfRangeException(nameof(workFactor), "Work factor must be >= 1.");

        var labelBytes = Encoding.ASCII.GetBytes(AgeLabel);
        var fullSalt = new byte[labelBytes.Length + salt.Length];
        Buffer.BlockCopy(labelBytes, 0, fullSalt, 0, labelBytes.Length);
        salt.CopyTo(fullSalt.AsSpan(labelBytes.Length));

        var n = 1 << workFactor;
        var passwordBytes = Encoding.UTF8.GetBytes(passphrase);

        try
        {
            return SCrypt.Generate(passwordBytes, fullSalt, n, R, P, KeySize);
        }
        finally
        {
            Array.Clear(passwordBytes);
        }
    }
}
