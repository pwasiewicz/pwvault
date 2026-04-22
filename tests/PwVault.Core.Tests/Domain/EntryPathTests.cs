using PwVault.Core.Domain;
using Xunit;

namespace PwVault.Core.Tests.Domain;

public class EntryPathTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("foo/..")]
    [InlineData("../foo")]
    [InlineData("foo//bar")]
    [InlineData("/..")]
    public void Invalid_paths_are_rejected(string value) =>
        Assert.Throws<ArgumentException>(() => new EntryPath(value));

    [Theory]
    [InlineData("foo", "foo")]
    [InlineData("foo/bar", "foo/bar")]
    [InlineData("/foo/bar/", "foo/bar")]
    [InlineData("foo\\bar", "foo/bar")]
    [InlineData("  foo/bar  ", "foo/bar")]
    public void Normalization_produces_expected_value(string input, string expected) =>
        Assert.Equal(expected, new EntryPath(input).Value);

    [Fact]
    public void Parent_is_null_for_single_segment() =>
        Assert.Null(new EntryPath("foo").Parent);

    [Fact]
    public void Parent_returns_preceding_segments()
    {
        var parent = new EntryPath("foo/bar/baz").Parent;
        Assert.NotNull(parent);
        Assert.Equal("foo/bar", parent.Value.Value);
    }

    [Fact]
    public void Name_returns_last_segment() =>
        Assert.Equal("baz", new EntryPath("foo/bar/baz").Name);

    [Fact]
    public void Equal_paths_have_equal_value_semantics()
    {
        var a = new EntryPath("foo/bar");
        var b = new EntryPath("/foo/bar/");
        Assert.Equal(a, b);
    }
}
