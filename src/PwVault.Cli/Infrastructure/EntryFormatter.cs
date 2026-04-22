using PwVault.Core.Domain;

namespace PwVault.Cli.Infrastructure;

public static class EntryFormatter
{
    public static string ForPicker(VaultEntry entry)
    {
        var parts = new List<string> { entry.Path.Value, "—", entry.Title };
        if (!string.IsNullOrEmpty(entry.Username))
            parts.Add($"· {entry.Username}");
        if (entry.Tags.Count > 0)
            parts.Add($"[{string.Join(", ", entry.Tags)}]");
        return string.Join(' ', parts);
    }
}
