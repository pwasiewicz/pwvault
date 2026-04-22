using PwVault.Core.Security.Age;
using Xunit;

namespace PwVault.Core.Tests.Security.Age;

public class AgePayloadTests
{
    private static byte[] PayloadKey()
    {
        var key = new byte[32];
        for (var i = 0; i < 32; i++) key[i] = (byte)i;
        return key;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(200_000)]
    [InlineData(500_000)]
    public void Roundtrip_various_sizes(int size)
    {
        var plaintext = new byte[size];
        Random.Shared.NextBytes(plaintext);
        var key = PayloadKey();

        var encrypted = AgePayload.Encrypt(plaintext, key);
        var decrypted = AgePayload.Decrypt(encrypted, key);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Empty_payload_has_single_tagged_chunk()
    {
        var key = PayloadKey();
        var encrypted = AgePayload.Encrypt(ReadOnlySpan<byte>.Empty, key);

        Assert.Equal(AgePayload.TagSize, encrypted.Length);
    }

    [Fact]
    public void Full_chunk_produces_two_chunks_with_trailing_empty_last()
    {
        var key = PayloadKey();
        var plaintext = new byte[AgePayload.ChunkSize];

        var encrypted = AgePayload.Encrypt(plaintext, key);

        Assert.Equal(AgePayload.ChunkSize + AgePayload.TagSize + AgePayload.TagSize, encrypted.Length);
    }

    [Fact]
    public void Tampered_ciphertext_fails_auth()
    {
        var key = PayloadKey();
        var encrypted = AgePayload.Encrypt([1, 2, 3, 4], key);
        encrypted[0] ^= 0xff;

        Assert.ThrowsAny<Exception>(() => AgePayload.Decrypt(encrypted, key));
    }

    [Fact]
    public void Wrong_key_fails_auth()
    {
        var encrypted = AgePayload.Encrypt([1, 2, 3, 4], PayloadKey());
        var otherKey = new byte[32];

        Assert.ThrowsAny<Exception>(() => AgePayload.Decrypt(encrypted, otherKey));
    }
}
