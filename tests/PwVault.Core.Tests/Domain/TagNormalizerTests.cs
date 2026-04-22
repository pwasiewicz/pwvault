using PwVault.Core.Domain;
using Xunit;

namespace PwVault.Core.Tests.Domain;

public sealed class TagNormalizerTests
{
    [Fact]
    public void Null_returns_empty_list()
    {
        Assert.Empty(TagNormalizer.Normalize(null));
    }

    [Fact]
    public void Trims_and_lowercases()
    {
        var result = TagNormalizer.Normalize(new[] { "  Banking  ", "DEV" });
        Assert.Equal(new[] { "banking", "dev" }, result);
    }

    [Fact]
    public void Deduplicates_case_insensitively()
    {
        var result = TagNormalizer.Normalize(new[] { "Work", "work", "WORK" });
        Assert.Single(result);
        Assert.Equal("work", result[0]);
    }

    [Fact]
    public void Sorts_ordinally_for_deterministic_git_diff()
    {
        var result = TagNormalizer.Normalize(new[] { "zeta", "alpha", "mid" });
        Assert.Equal(new[] { "alpha", "mid", "zeta" }, result);
    }

    [Fact]
    public void Filters_empty_and_whitespace_entries()
    {
        var result = TagNormalizer.Normalize(new[] { "work", "", "  ", "home" });
        Assert.Equal(new[] { "home", "work" }, result);
    }

    [Fact]
    public void Rejects_slash()
    {
        Assert.Throws<ArgumentException>(() =>
            TagNormalizer.Normalize(new[] { "bad/tag" }));
    }

    [Fact]
    public void Rejects_internal_whitespace()
    {
        Assert.Throws<ArgumentException>(() =>
            TagNormalizer.Normalize(new[] { "two words" }));
    }

    [Fact]
    public void Rejects_control_chars()
    {
        Assert.Throws<ArgumentException>(() =>
            TagNormalizer.Normalize(new[] { "badtag" }));
    }

    [Fact]
    public void Rejects_oversized_tag()
    {
        var tooLong = new string('a', TagNormalizer.MaxLength + 1);
        Assert.Throws<ArgumentException>(() =>
            TagNormalizer.Normalize(new[] { tooLong }));
    }

    [Fact]
    public void Accepts_dashes_and_digits_and_unicode()
    {
        var result = TagNormalizer.Normalize(new[] { "2fa", "home-wifi", "książka" });
        Assert.Contains("2fa", result);
        Assert.Contains("home-wifi", result);
        Assert.Contains("książka", result);
    }

    [Fact]
    public void NormalizeOneOrThrow_rejects_empty()
    {
        Assert.Throws<ArgumentException>(() => TagNormalizer.NormalizeOneOrThrow("  "));
    }

    [Fact]
    public void NormalizeOneOrThrow_normalizes()
    {
        Assert.Equal("work", TagNormalizer.NormalizeOneOrThrow("  WORK  "));
    }
}
