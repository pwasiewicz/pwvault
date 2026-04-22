using PwVault.Core.Domain;
using Xunit;

namespace PwVault.Core.Tests.Domain;

public sealed class TagEditorTests
{
    private static readonly IReadOnlyList<string> Start = new[] { "banking", "money" };

    [Fact]
    public void Add_inserts_and_normalizes()
    {
        var result = TagEditor.Apply(Start, add: new[] { "2fa" });
        Assert.Equal(new[] { "2fa", "banking", "money" }, result);
    }

    [Fact]
    public void Remove_drops_and_preserves_rest()
    {
        var result = TagEditor.Apply(Start, remove: new[] { "money" });
        Assert.Equal(new[] { "banking" }, result);
    }

    [Fact]
    public void Add_and_remove_same_tag_removes_then_adds_back()
    {
        var result = TagEditor.Apply(Start, add: new[] { "banking" }, remove: new[] { "banking" });
        Assert.Contains("banking", result);
    }

    [Fact]
    public void Add_existing_is_idempotent()
    {
        var result = TagEditor.Apply(Start, add: new[] { "banking", "BANKING" });
        Assert.Equal(new[] { "banking", "money" }, result);
    }

    [Fact]
    public void Remove_nonexistent_is_noop()
    {
        var result = TagEditor.Apply(Start, remove: new[] { "never-was" });
        Assert.Equal(Start, result);
    }

    [Fact]
    public void Inputs_case_insensitive()
    {
        var result = TagEditor.Apply(Start, add: new[] { "WORK" }, remove: new[] { "MONEY" });
        Assert.Equal(new[] { "banking", "work" }, result);
    }

    [Fact]
    public void Result_is_deterministically_sorted()
    {
        var result = TagEditor.Apply(
            new[] { "zed", "alpha" },
            add: new[] { "mid" });
        Assert.Equal(new[] { "alpha", "mid", "zed" }, result);
    }

    [Fact]
    public void Invalid_tag_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            TagEditor.Apply(Start, add: new[] { "has/slash" }));
    }
}
