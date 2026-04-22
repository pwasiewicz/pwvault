namespace PwVault.Core.Security.Age;

public class AgeDecryptionException : Exception
{
    public AgeDecryptionException(string message) : base(message) { }
    public AgeDecryptionException(string message, Exception inner) : base(message, inner) { }
}

public sealed class InvalidPassphraseException : AgeDecryptionException
{
    public InvalidPassphraseException()
        : base("Age decryption failed — passphrase is incorrect or file is corrupt.") { }

    public InvalidPassphraseException(Exception inner)
        : base("Age decryption failed — passphrase is incorrect or file is corrupt.", inner) { }
}
