using PwVault.Core.Security.Age;
using Xunit;

namespace PwVault.Core.Tests.Security.Age;

public class AgeV1GatewayTests
{
    private const int TestWorkFactor = 10;

    private static AgeV1Gateway NewGateway() => new(encryptWorkFactor: TestWorkFactor);

    [Fact]
    public void Roundtrip_short_string()
    {
        var gateway = NewGateway();
        var encrypted = gateway.Encrypt("hunter2", "master-password");

        var decrypted = gateway.Decrypt(encrypted, "master-password");

        Assert.Equal("hunter2", decrypted);
    }

    [Fact]
    public void Roundtrip_empty_string()
    {
        var gateway = NewGateway();
        var encrypted = gateway.Encrypt("", "m");
        Assert.Equal("", gateway.Decrypt(encrypted, "m"));
    }

    [Fact]
    public void Roundtrip_multi_chunk_payload()
    {
        var gateway = NewGateway();
        var plaintext = new string('a', 200_000);

        var encrypted = gateway.Encrypt(plaintext, "m");
        var decrypted = gateway.Decrypt(encrypted, "m");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Roundtrip_unicode()
    {
        var gateway = NewGateway();
        var plaintext = "zażółć gęślą jaźń 🔐 ünicöde";

        var encrypted = gateway.Encrypt(plaintext, "master 🔑 master");
        var decrypted = gateway.Decrypt(encrypted, "master 🔑 master");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Wrong_passphrase_throws_InvalidPassphraseException()
    {
        var gateway = NewGateway();
        var encrypted = gateway.Encrypt("secret", "correct");

        Assert.Throws<InvalidPassphraseException>(() => gateway.Decrypt(encrypted, "wrong"));
    }

    [Fact]
    public void Tampered_payload_throws_AgeDecryptionException()
    {
        var gateway = NewGateway();
        var encrypted = gateway.Encrypt("secret", "master");

        var binary = AgeArmor.Decode(encrypted);
        binary[^5] ^= 0xff;
        var tampered = AgeArmor.Encode(binary);

        Assert.Throws<AgeDecryptionException>(() => gateway.Decrypt(tampered, "master"));
    }

    [Fact]
    public void Encrypted_output_is_ascii_armored()
    {
        var gateway = NewGateway();
        var encrypted = gateway.Encrypt("secret", "master");

        Assert.StartsWith("-----BEGIN AGE ENCRYPTED FILE-----\n", encrypted);
        Assert.EndsWith("-----END AGE ENCRYPTED FILE-----\n", encrypted);
    }

    [Fact]
    public void Two_encryptions_of_same_plaintext_produce_different_ciphertext()
    {
        var gateway = NewGateway();
        var a = gateway.Encrypt("secret", "master");
        var b = gateway.Encrypt("secret", "master");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Reject_constructor_work_factor_below_minimum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgeV1Gateway(encryptWorkFactor: 5));
    }

    [Fact]
    public void Reject_file_with_excessive_work_factor()
    {
        var highWfGateway = new AgeV1Gateway(encryptWorkFactor: 15, maxDecryptWorkFactor: 15);
        var encrypted = highWfGateway.Encrypt("x", "m");

        var strictGateway = new AgeV1Gateway(encryptWorkFactor: 10, maxDecryptWorkFactor: 12);

        Assert.Throws<AgeDecryptionException>(() => strictGateway.Decrypt(encrypted, "m"));
    }
}
