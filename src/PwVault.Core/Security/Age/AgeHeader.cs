using System.Text;

namespace PwVault.Core.Security.Age;

internal sealed record AgeHeader(
    byte[] ScryptSalt,
    int WorkFactor,
    byte[] WrappedFileKey,
    byte[] Mac);

internal sealed record ParsedHeader(
    AgeHeader Header,
    int HeaderEndIndex,
    byte[] BytesForMac);

internal static class AgeHeaderCodec
{
    public const string VersionLine = "age-encryption.org/v1";

    public static byte[] BuildBytesForMac(byte[] scryptSalt, int workFactor, byte[] wrappedFileKey)
    {
        var saltB64 = Base64NoPadding(scryptSalt);
        var bodyB64 = Base64NoPadding(wrappedFileKey);
        var text = $"{VersionLine}\n-> scrypt {saltB64} {workFactor}\n{bodyB64}\n---";
        return Encoding.UTF8.GetBytes(text);
    }

    public static byte[] AppendMac(byte[] bytesForMac, byte[] mac)
    {
        var macB64 = Base64NoPadding(mac);
        var suffix = Encoding.UTF8.GetBytes($" {macB64}\n");
        var result = new byte[bytesForMac.Length + suffix.Length];
        Buffer.BlockCopy(bytesForMac, 0, result, 0, bytesForMac.Length);
        Buffer.BlockCopy(suffix, 0, result, bytesForMac.Length, suffix.Length);
        return result;
    }

    public static ParsedHeader Parse(ReadOnlySpan<byte> buffer)
    {
        var lines = new List<Range>();
        var lineStart = 0;
        int? macLineStart = null;
        int headerEnd = -1;

        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != (byte)'\n') continue;

            lines.Add(new Range(lineStart, i));

            if (i - lineStart >= 4
                && buffer[lineStart] == '-'
                && buffer[lineStart + 1] == '-'
                && buffer[lineStart + 2] == '-'
                && buffer[lineStart + 3] == ' ')
            {
                macLineStart = lineStart;
                headerEnd = i + 1;
                break;
            }

            lineStart = i + 1;
        }

        if (macLineStart is null)
            throw new FormatException("Age header missing '--- ' terminator line.");

        if (lines.Count < 3)
            throw new FormatException("Age header too short — expected version, stanza header, body, MAC line.");

        var versionLine = DecodeLine(buffer, lines[0]);
        if (versionLine != VersionLine)
            throw new FormatException($"Unsupported age version: '{versionLine}'.");

        var stanzaHeader = DecodeLine(buffer, lines[1]);
        var parts = stanzaHeader.Split(' ');
        if (parts.Length != 4 || parts[0] != "->" || parts[1] != "scrypt")
            throw new FormatException($"Only a single scrypt stanza is supported. Got: '{stanzaHeader}'.");

        var scryptSalt = FromBase64NoPadding(parts[2]);
        if (scryptSalt.Length != 16)
            throw new FormatException($"Scrypt salt must be 16 bytes, got {scryptSalt.Length}.");

        if (!int.TryParse(parts[3], out var workFactor) || workFactor < 1)
            throw new FormatException($"Invalid scrypt work factor: '{parts[3]}'.");

        var body = new StringBuilder();
        var macLineIdx = lines.Count - 1;
        for (var i = 2; i < macLineIdx; i++)
            body.Append(DecodeLine(buffer, lines[i]));

        var wrappedFileKey = FromBase64NoPadding(body.ToString());

        var macLineText = DecodeLine(buffer, lines[macLineIdx]);
        var macParts = macLineText.Split(' ', 2);
        if (macParts.Length != 2 || macParts[0] != "---")
            throw new FormatException($"Invalid MAC line: '{macLineText}'.");
        var mac = FromBase64NoPadding(macParts[1]);

        var bytesForMac = buffer[..(macLineStart.Value + 3)].ToArray();

        return new ParsedHeader(
            new AgeHeader(scryptSalt, workFactor, wrappedFileKey, mac),
            headerEnd,
            bytesForMac);
    }

    private static string DecodeLine(ReadOnlySpan<byte> buffer, Range range) =>
        Encoding.UTF8.GetString(buffer[range]);

    private static string Base64NoPadding(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=');

    private static byte[] FromBase64NoPadding(string value)
    {
        var pad = (4 - value.Length % 4) % 4;
        return Convert.FromBase64String(value + new string('=', pad));
    }
}
