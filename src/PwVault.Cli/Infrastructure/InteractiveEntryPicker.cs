using PwVault.Core.Domain;
using PwVault.Core.Storage;
using Spectre.Console;

namespace PwVault.Cli.Infrastructure;

public static class InteractiveEntryPicker
{
    public static StoredEntry? Pick(IVaultStorage storage, string title = "Select an entry")
    {
        var entries = storage.List()
            .OrderBy(e => e.Entry.Path.Value, StringComparer.Ordinal)
            .ToList();

        if (entries.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No entries in vault.[/]");
            return null;
        }

        return AnsiConsole.Prompt(
            new SelectionPrompt<StoredEntry>()
                .Title($"{title} [dim](type to filter)[/]:")
                .PageSize(15)
                .MoreChoicesText("[dim](↑/↓ to scroll)[/]")
                .EnableSearch()
                .AddChoices(entries)
                .UseConverter(e => EntryFormatter.ForPicker(e.Entry)));
    }
}
