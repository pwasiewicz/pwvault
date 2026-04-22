namespace PwVault.Core.Domain;

public readonly record struct EntryPath
{
    public string Value { get; }

    public EntryPath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = Normalize(value);
        Validate(normalized);
        Value = normalized;
    }

    public IReadOnlyList<string> Segments => Value.Split('/');

    public string Name => Segments[^1];

    public EntryPath? Parent
    {
        get
        {
            var segments = Segments;
            if (segments.Count <= 1) return null;
            var parentValue = string.Join('/', segments.Take(segments.Count - 1));
            return new EntryPath(parentValue);
        }
    }

    public override string ToString() => Value;

    private static string Normalize(string value) =>
        value.Trim().Replace('\\', '/').Trim('/');

    private static void Validate(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            throw new ArgumentException("Entry path cannot be empty.", nameof(normalized));

        foreach (var segment in normalized.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(segment))
                throw new ArgumentException($"Entry path contains empty segment: '{normalized}'.");
            if (segment == "..")
                throw new ArgumentException($"Entry path cannot contain '..' segments: '{normalized}'.");
            if (segment.Any(char.IsControl))
                throw new ArgumentException($"Entry path contains control characters: '{normalized}'.");
        }
    }
}
