using System.Security.Cryptography;
using System.Text;

namespace PwVault.Core.Security.Age;

public sealed class AgeV1Gateway : IAgeGateway
{
    public const int DefaultWorkFactor = 18;
    public const int MaxAcceptedWorkFactor = 22;
    public const int MinAcceptedWorkFactor = 10;

    private const int FileKeySize = 16;
    private const int PayloadNonceSize = 16;
    private const int WrapKeySize = 32;
    private const int MacKeySize = 32;
    private const int PayloadKeySize = 32;

    private static readonly byte[] HeaderInfo = "header"u8.ToArray();
    private static readonly byte[] PayloadInfo = "payload"u8.ToArray();

    private readonly int _encryptWorkFactor;
    private readonly int _maxDecryptWorkFactor;

    public AgeV1Gateway(
        int encryptWorkFactor = DefaultWorkFactor,
        int maxDecryptWorkFactor = MaxAcceptedWorkFactor)
    {
        if (encryptWorkFactor < MinAcceptedWorkFactor)
            throw new ArgumentOutOfRangeException(
                nameof(encryptWorkFactor),
                $"Work factor must be at least {MinAcceptedWorkFactor} for security.");
        if (encryptWorkFactor > maxDecryptWorkFactor)
            throw new ArgumentException(
                "encryptWorkFactor cannot exceed maxDecryptWorkFactor.",
                nameof(encryptWorkFactor));
        _encryptWorkFactor = encryptWorkFactor;
        _maxDecryptWorkFactor = maxDecryptWorkFactor;
    }

    public string Encrypt(string plaintext, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(passphrase);

        var fileKey = RandomNumberGenerator.GetBytes(FileKeySize);
        var scryptSalt = RandomNumberGenerator.GetBytes(ScryptKdf.SaltSize);
        var payloadNonce = RandomNumberGenerator.GetBytes(PayloadNonceSize);
        var wrapKey = ScryptKdf.Derive(passphrase, scryptSalt, _encryptWorkFactor);

        try
        {
            var wrappedFileKey = new byte[FileKeySize + AgePayload.TagSize];
            Span<byte> zeroNonce = stackalloc byte[12];
            Geralt.ChaCha20Poly1305.Encrypt(wrappedFileKey, fileKey, zeroNonce, wrapKey);

            var headerForMac = AgeHeaderCodec.BuildBytesForMac(scryptSalt, _encryptWorkFactor, wrappedFileKey);

            var macKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: fileKey,
                outputLength: MacKeySize,
                salt: null,
                info: HeaderInfo);

            byte[] mac;
            byte[] payloadKey;
            try
            {
                mac = HMACSHA256.HashData(macKey, headerForMac);
                payloadKey = HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    ikm: fileKey,
                    outputLength: PayloadKeySize,
                    salt: payloadNonce,
                    info: PayloadInfo);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(macKey);
            }

            byte[] encryptedPayload;
            try
            {
                var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
                try
                {
                    encryptedPayload = AgePayload.Encrypt(plaintextBytes, payloadKey);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintextBytes);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payloadKey);
            }

            var fullHeader = AgeHeaderCodec.AppendMac(headerForMac, mac);
            var binary = Concat(fullHeader, payloadNonce, encryptedPayload);
            return AgeArmor.Encode(binary);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
            CryptographicOperations.ZeroMemory(wrapKey);
        }
    }

    public string Decrypt(string asciiArmor, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(asciiArmor);
        ArgumentNullException.ThrowIfNull(passphrase);

        var binary = AgeArmor.Decode(asciiArmor);
        var parsed = AgeHeaderCodec.Parse(binary);

        if (parsed.Header.WorkFactor > _maxDecryptWorkFactor)
            throw new AgeDecryptionException(
                $"Refusing to process file: scrypt work factor {parsed.Header.WorkFactor} exceeds maximum {_maxDecryptWorkFactor}.");

        var wrapKey = ScryptKdf.Derive(passphrase, parsed.Header.ScryptSalt, parsed.Header.WorkFactor);
        var fileKey = new byte[FileKeySize];

        try
        {
            Span<byte> zeroNonce = stackalloc byte[12];
            try
            {
                Geralt.ChaCha20Poly1305.Decrypt(fileKey, parsed.Header.WrappedFileKey, zeroNonce, wrapKey);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidPassphraseException(ex);
            }

            var macKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: fileKey,
                outputLength: MacKeySize,
                salt: null,
                info: HeaderInfo);
            try
            {
                var expectedMac = HMACSHA256.HashData(macKey, parsed.BytesForMac);
                if (!CryptographicOperations.FixedTimeEquals(expectedMac, parsed.Header.Mac))
                    throw new AgeDecryptionException("Age header MAC verification failed — file is corrupt or tampered with.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(macKey);
            }

            if (binary.Length - parsed.HeaderEndIndex < PayloadNonceSize)
                throw new AgeDecryptionException("Age file truncated — missing payload nonce.");

            var payloadNonce = binary.AsSpan(parsed.HeaderEndIndex, PayloadNonceSize).ToArray();
            var payload = binary.AsSpan(parsed.HeaderEndIndex + PayloadNonceSize);

            var payloadKey = HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: fileKey,
                outputLength: PayloadKeySize,
                salt: payloadNonce,
                info: PayloadInfo);
            try
            {
                byte[] plainBytes;
                try
                {
                    plainBytes = AgePayload.Decrypt(payload, payloadKey);
                }
                catch (CryptographicException ex)
                {
                    throw new AgeDecryptionException("Age payload decryption failed — file is corrupt or tampered with.", ex);
                }
                return Encoding.UTF8.GetString(plainBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payloadKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fileKey);
            CryptographicOperations.ZeroMemory(wrapKey);
        }
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var p in parts) total += p.Length;
        var result = new byte[total];
        var offset = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }
}
