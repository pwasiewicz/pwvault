namespace PwVault.Core.Domain;

public static class TagNormalizer
{
    public const int MaxLength = 50;

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? tags)
    {
        if (tags is null) return Array.Empty<string>();

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in tags)
        {
            var canonical = NormalizeOne(raw);
            if (canonical is null) continue;
            if (seen.Add(canonical)) result.Add(canonical);
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    public static string NormalizeOneOrThrow(string value)
    {
        var canonical = NormalizeOne(value)
            ?? throw new ArgumentException("Tag cannot be empty.", nameof(value));
        return canonical;
    }

    private static string? NormalizeOne(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;

        var lowered = trimmed.ToLowerInvariant();
        Validate(lowered);
        return lowered;
    }

    private static void Validate(string tag)
    {
        if (tag.Length > MaxLength)
            throw new ArgumentException(
                $"Tag '{tag}' exceeds max length of {MaxLength} characters.");
        if (tag.Contains('/'))
            throw new ArgumentException($"Tag '{tag}' cannot contain '/'.");
        foreach (var ch in tag)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch))
                throw new ArgumentException(
                    $"Tag '{tag}' cannot contain whitespace or control characters.");
        }
    }
}
