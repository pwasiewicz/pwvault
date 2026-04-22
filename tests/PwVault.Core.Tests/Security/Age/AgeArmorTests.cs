using PwVault.Core.Security.Age;
using Xunit;

namespace PwVault.Core.Tests.Security.Age;

public class AgeArmorTests
{
    [Fact]
    public void Roundtrip_preserves_bytes()
    {
        var data = new byte[200];
        Random.Shared.NextBytes(data);

        var armored = AgeArmor.Encode(data);
        var decoded = AgeArmor.Decode(armored);

        Assert.Equal(data, decoded);
    }

    [Fact]
    public void Encoded_output_has_begin_and_end_markers()
    {
        var armored = AgeArmor.Encode([1, 2, 3]);
        Assert.StartsWith("-----BEGIN AGE ENCRYPTED FILE-----\n", armored);
        Assert.EndsWith("-----END AGE ENCRYPTED FILE-----\n", armored);
    }

    [Fact]
    public void Encoded_body_wraps_at_64_columns()
    {
        var data = new byte[200];
        Random.Shared.NextBytes(data);
        var armored = AgeArmor.Encode(data);

        var lines = armored.Split('\n');
        foreach (var line in lines[1..^2])
            Assert.True(line.Length <= 64, $"Line too long ({line.Length}): '{line}'.");
    }

    [Fact]
    public void Decode_accepts_crlf_line_endings()
    {
        var armored = AgeArmor.Encode([1, 2, 3, 4, 5]);
        var withCrlf = armored.Replace("\n", "\r\n");

        var decoded = AgeArmor.Decode(withCrlf);

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, decoded);
    }

    [Fact]
    public void Decode_rejects_missing_begin_marker()
    {
        var input = "SGVsbG8=\n-----END AGE ENCRYPTED FILE-----\n";
        Assert.Throws<FormatException>(() => AgeArmor.Decode(input));
    }

    [Fact]
    public void Decode_rejects_missing_end_marker()
    {
        var input = "-----BEGIN AGE ENCRYPTED FILE-----\nSGVsbG8=\n";
        Assert.Throws<FormatException>(() => AgeArmor.Decode(input));
    }

    [Fact]
    public void Decode_rejects_invalid_base64()
    {
        var input = "-----BEGIN AGE ENCRYPTED FILE-----\n!!!notbase64!!!\n-----END AGE ENCRYPTED FILE-----\n";
        Assert.Throws<FormatException>(() => AgeArmor.Decode(input));
    }
}
