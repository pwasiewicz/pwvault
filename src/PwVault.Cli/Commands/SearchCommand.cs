using System.ComponentModel;
using PwVault.Cli.Infrastructure;
using PwVault.Core.IO;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class SearchCommand : Command<SearchCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly PwVaultConfig _config;

    public SearchCommand(IFileSystem fs, PwVaultConfig config)
    {
        _fs = fs;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("Fuzzy-matched against path, title, username, url.")]
        public string Query { get; init; } = "";

        [CommandOption("-n|--max <N>")]
        public int? MaxResults { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);
        using var storage = VaultStorage.Open(vaultPath, _fs);
        var results = storage.Search(settings.Query, settings.MaxResults ?? 20);

        if (results.Count == 0)
        {
            AnsiConsole.MarkupLine($"[dim]No matches for '{Markup.Escape(settings.Query)}'.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Path");
        table.AddColumn("Title");
        table.AddColumn("Username");
        table.AddColumn("URL");

        foreach (var r in results)
        {
            table.AddRow(
                $"[green]{Markup.Escape(r.Entry.Path.Value)}[/]",
                Markup.Escape(r.Entry.Title),
                Markup.Escape(r.Entry.Username ?? ""),
                Markup.Escape(r.Entry.Url ?? ""));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
