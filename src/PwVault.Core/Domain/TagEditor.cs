namespace PwVault.Core.Domain;

public static class TagEditor
{
    public static IReadOnlyList<string> Apply(
        IReadOnlyList<string> current,
        IEnumerable<string>? add = null,
        IEnumerable<string>? remove = null)
    {
        var removeSet = (remove ?? Array.Empty<string>())
            .Select(TagNormalizer.NormalizeOneOrThrow)
            .ToHashSet(StringComparer.Ordinal);

        var addSet = (add ?? Array.Empty<string>())
            .Select(TagNormalizer.NormalizeOneOrThrow)
            .ToHashSet(StringComparer.Ordinal);

        var merged = current.Where(t => !removeSet.Contains(t)).Concat(addSet);
        return TagNormalizer.Normalize(merged);
    }
}
