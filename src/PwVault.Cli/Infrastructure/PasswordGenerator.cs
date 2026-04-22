using System.Security.Cryptography;

namespace PwVault.Cli.Infrastructure;

public static class PasswordGenerator
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}<>?.,";

    public static string Generate(int length = 24, bool includeSymbols = true)
    {
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));

        var pool = Lowercase + Uppercase + Digits;
        if (includeSymbols) pool += Symbols;

        var result = new char[length];
        for (var i = 0; i < length; i++)
            result[i] = pool[RandomNumberGenerator.GetInt32(pool.Length)];
        return new string(result);
    }
}
