using System.Text;

namespace PwVault.Core.Security.Age;

internal static class AgeArmor
{
    public const string BeginMarker = "-----BEGIN AGE ENCRYPTED FILE-----";
    public const string EndMarker = "-----END AGE ENCRYPTED FILE-----";
    private const int WrapColumns = 64;

    public static string Encode(ReadOnlySpan<byte> binary)
    {
        var base64 = Convert.ToBase64String(binary);
        var builder = new StringBuilder();
        builder.Append(BeginMarker).Append('\n');

        for (var i = 0; i < base64.Length; i += WrapColumns)
        {
            var length = Math.Min(WrapColumns, base64.Length - i);
            builder.Append(base64, i, length).Append('\n');
        }

        builder.Append(EndMarker).Append('\n');
        return builder.ToString();
    }

    public static byte[] Decode(string armored)
    {
        ArgumentNullException.ThrowIfNull(armored);

        var normalized = armored.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        var beginIndex = Array.FindIndex(lines, l => l.Trim() == BeginMarker);
        if (beginIndex < 0)
            throw new FormatException("Missing age armor BEGIN marker.");

        var endIndex = Array.FindIndex(lines, beginIndex + 1, l => l.Trim() == EndMarker);
        if (endIndex < 0)
            throw new FormatException("Missing age armor END marker.");

        var body = new StringBuilder();
        for (var i = beginIndex + 1; i < endIndex; i++)
            body.Append(lines[i].Trim());

        try
        {
            return Convert.FromBase64String(body.ToString());
        }
        catch (FormatException ex)
        {
            throw new FormatException("Age armor body is not valid Base64.", ex);
        }
    }
}
