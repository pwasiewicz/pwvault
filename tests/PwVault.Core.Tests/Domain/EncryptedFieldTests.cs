using PwVault.Core.Domain;
using Xunit;

namespace PwVault.Core.Tests.Domain;

public class EncryptedFieldTests
{
    private const string ValidArmor =
        "-----BEGIN AGE ENCRYPTED FILE-----\nYWdlLWVuY3J5cHRpb24ub3JnL3Yx\n-----END AGE ENCRYPTED FILE-----";

    [Fact]
    public void Valid_age_armor_is_accepted()
    {
        var field = new EncryptedField(ValidArmor);
        Assert.Equal(ValidArmor, field.AsciiArmor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-armored")]
    [InlineData("-----BEGIN AGE ENCRYPTED FILE-----\nno-footer")]
    [InlineData("just some plaintext password")]
    public void Invalid_values_are_rejected(string value) =>
        Assert.Throws<ArgumentException>(() => new EncryptedField(value));

    [Fact]
    public void IsValid_matches_constructor_behaviour()
    {
        Assert.True(EncryptedField.IsValid(ValidArmor));
        Assert.False(EncryptedField.IsValid("plain"));
    }

    [Fact]
    public void Equal_armors_compare_equal()
    {
        var a = new EncryptedField(ValidArmor);
        var b = new EncryptedField(ValidArmor);
        Assert.Equal(a, b);
    }
}
