using PwVault.Core.Security.Age;
using Xunit;

namespace PwVault.Core.Tests.Security.Age;

public class AgeBinaryInteropTests
{
    private const int TestWorkFactor = 10;

    [Fact]
    public void Our_encrypt_is_decryptable_by_age_binary()
    {
        if (!AgeBinaryInterop.IsAvailable) return;

        var gateway = new AgeV1Gateway(encryptWorkFactor: TestWorkFactor);
        const string plaintext = "hello from dotnet";
        var encrypted = gateway.Encrypt(plaintext, "test-pass");

        var decrypted = AgeBinaryInterop.Decrypt(encrypted, "test-pass");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Age_binary_encryption_is_decryptable_by_our_code()
    {
        if (!AgeBinaryInterop.IsAvailable) return;

        const string plaintext = "hello from age binary";
        var encrypted = AgeBinaryInterop.Encrypt(plaintext, "test-pass");

        // age default WF is 18 — allow up to 22
        var gateway = new AgeV1Gateway(encryptWorkFactor: 18, maxDecryptWorkFactor: 22);
        var decrypted = gateway.Decrypt(encrypted, "test-pass");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Our_encrypt_roundtrips_through_age_binary_with_unicode()
    {
        if (!AgeBinaryInterop.IsAvailable) return;

        var gateway = new AgeV1Gateway(encryptWorkFactor: TestWorkFactor);
        const string plaintext = "zażółć gęślą jaźń 🔐";
        var encrypted = gateway.Encrypt(plaintext, "unicode-pass");

        var decrypted = AgeBinaryInterop.Decrypt(encrypted, "unicode-pass");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Our_encrypt_roundtrips_through_age_binary_multi_chunk()
    {
        if (!AgeBinaryInterop.IsAvailable) return;

        var gateway = new AgeV1Gateway(encryptWorkFactor: TestWorkFactor);
        var plaintext = new string('x', 200_000);
        var encrypted = gateway.Encrypt(plaintext, "m");

        var decrypted = AgeBinaryInterop.Decrypt(encrypted, "m");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Age_encrypt_roundtrips_through_our_decrypt_empty()
    {
        if (!AgeBinaryInterop.IsAvailable) return;

        var encrypted = AgeBinaryInterop.Encrypt("", "pw");

        var gateway = new AgeV1Gateway(encryptWorkFactor: 18, maxDecryptWorkFactor: 22);
        var decrypted = gateway.Decrypt(encrypted, "pw");

        Assert.Equal("", decrypted);
    }
}
