using System.Buffers.Binary;
using Geralt;

namespace PwVault.Core.Security.Age;

internal static class AgePayload
{
    public const int ChunkSize = 65536;
    public const int TagSize = 16;
    public const int NonceSize = 12;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> payloadKey)
    {
        if (payloadKey.Length != ChaCha20Poly1305.KeySize)
            throw new ArgumentException(
                $"Payload key must be {ChaCha20Poly1305.KeySize} bytes.", nameof(payloadKey));

        var numFullChunks = plaintext.Length / ChunkSize;
        var remainder = plaintext.Length % ChunkSize;
        var totalChunks = numFullChunks + 1;
        var output = new byte[plaintext.Length + totalChunks * TagSize];

        ulong counter = 0;
        var writePos = 0;
        Span<byte> nonce = stackalloc byte[NonceSize];

        for (var i = 0; i < numFullChunks; i++)
        {
            WriteNonce(nonce, counter, isLast: false);
            var chunk = plaintext.Slice(i * ChunkSize, ChunkSize);
            var outSlice = output.AsSpan(writePos, ChunkSize + TagSize);
            ChaCha20Poly1305.Encrypt(outSlice, chunk, nonce, payloadKey);
            writePos += outSlice.Length;
            counter++;
        }

        WriteNonce(nonce, counter, isLast: true);
        var finalChunk = plaintext.Slice(numFullChunks * ChunkSize, remainder);
        var finalOut = output.AsSpan(writePos, remainder + TagSize);
        ChaCha20Poly1305.Encrypt(finalOut, finalChunk, nonce, payloadKey);
        writePos += finalOut.Length;

        if (writePos != output.Length)
            throw new InvalidOperationException("Internal error: payload size mismatch.");

        return output;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> payloadKey)
    {
        if (payloadKey.Length != ChaCha20Poly1305.KeySize)
            throw new ArgumentException(
                $"Payload key must be {ChaCha20Poly1305.KeySize} bytes.", nameof(payloadKey));

        using var buffer = new MemoryStream();
        ulong counter = 0;
        var pos = 0;
        var sawLast = false;
        Span<byte> nonce = stackalloc byte[NonceSize];

        while (pos < ciphertext.Length || !sawLast)
        {
            if (sawLast)
                throw new FormatException("Age payload has data after the last chunk.");

            var remaining = ciphertext.Length - pos;
            if (remaining < TagSize)
                throw new FormatException("Age payload truncated — chunk shorter than tag.");

            var isLast = remaining <= ChunkSize + TagSize;
            if (!isLast && remaining < ChunkSize + TagSize)
                throw new FormatException("Non-last age payload chunk truncated.");

            var cipherLen = isLast ? remaining - TagSize : ChunkSize;
            WriteNonce(nonce, counter, isLast);

            var plain = new byte[cipherLen];
            ChaCha20Poly1305.Decrypt(plain, ciphertext.Slice(pos, cipherLen + TagSize), nonce, payloadKey);
            buffer.Write(plain, 0, plain.Length);

            pos += cipherLen + TagSize;
            counter++;
            if (isLast) sawLast = true;
        }

        if (!sawLast)
            throw new FormatException("Age payload missing last chunk.");

        return buffer.ToArray();
    }

    private static void WriteNonce(Span<byte> nonce, ulong counter, bool isLast)
    {
        nonce.Clear();
        BinaryPrimitives.WriteUInt64BigEndian(nonce.Slice(3, 8), counter);
        nonce[11] = isLast ? (byte)1 : (byte)0;
    }
}
