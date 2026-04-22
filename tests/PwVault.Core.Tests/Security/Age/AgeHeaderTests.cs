using System.Text;
using PwVault.Core.Security.Age;
using Xunit;

namespace PwVault.Core.Tests.Security.Age;

public class AgeHeaderTests
{
    [Fact]
    public void Build_and_parse_roundtrip()
    {
        var salt = new byte[16];
        Random.Shared.NextBytes(salt);
        var wrappedKey = new byte[32];
        Random.Shared.NextBytes(wrappedKey);
        var mac = new byte[32];
        Random.Shared.NextBytes(mac);

        var forMac = AgeHeaderCodec.BuildBytesForMac(salt, 18, wrappedKey);
        var full = AgeHeaderCodec.AppendMac(forMac, mac);

        var parsed = AgeHeaderCodec.Parse(full);

        Assert.Equal(salt, parsed.Header.ScryptSalt);
        Assert.Equal(18, parsed.Header.WorkFactor);
        Assert.Equal(wrappedKey, parsed.Header.WrappedFileKey);
        Assert.Equal(mac, parsed.Header.Mac);
        Assert.Equal(forMac, parsed.BytesForMac);
        Assert.Equal(full.Length, parsed.HeaderEndIndex);
    }

    [Fact]
    public void BytesForMac_ends_with_three_dashes()
    {
        var forMac = AgeHeaderCodec.BuildBytesForMac(new byte[16], 18, new byte[32]);
        Assert.Equal((byte)'-', forMac[^3]);
        Assert.Equal((byte)'-', forMac[^2]);
        Assert.Equal((byte)'-', forMac[^1]);
    }

    [Fact]
    public void AppendMac_writes_space_and_mac_and_newline()
    {
        var forMac = AgeHeaderCodec.BuildBytesForMac(new byte[16], 18, new byte[32]);
        var mac = new byte[32];
        var full = AgeHeaderCodec.AppendMac(forMac, mac);

        Assert.Equal((byte)' ', full[forMac.Length]);
        Assert.Equal((byte)'\n', full[^1]);
    }

    [Fact]
    public void Parse_rejects_unknown_version()
    {
        var bogus = Encoding.UTF8.GetBytes("age-encryption.org/v2\n-> scrypt AAAAAAAAAAAAAAAAAAAAAA 18\nAAAAAA\n--- AA\n");
        Assert.Throws<FormatException>(() => AgeHeaderCodec.Parse(bogus));
    }

    [Fact]
    public void Parse_rejects_non_scrypt_stanza()
    {
        var bogus = Encoding.UTF8.GetBytes("age-encryption.org/v1\n-> X25519 Abc\nAAAAAA\n--- AA\n");
        Assert.Throws<FormatException>(() => AgeHeaderCodec.Parse(bogus));
    }

    [Fact]
    public void Parse_rejects_missing_terminator()
    {
        var bogus = Encoding.UTF8.GetBytes("age-encryption.org/v1\n-> scrypt AAAAAAAAAAAAAAAAAAAAAA 18\nAAAAAA\n");
        Assert.Throws<FormatException>(() => AgeHeaderCodec.Parse(bogus));
    }

    [Fact]
    public void Header_body_does_not_use_base64_padding()
    {
        var salt = new byte[16];
        var wrappedKey = new byte[32];
        var forMac = AgeHeaderCodec.BuildBytesForMac(salt, 10, wrappedKey);
        var text = Encoding.UTF8.GetString(forMac);

        Assert.DoesNotContain('=', text);
    }
}
