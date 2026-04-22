using PwVault.Cli.Infrastructure;
using PwVault.Core.IO;
using PwVault.Core.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PwVault.Cli.Commands;

public sealed class TagsCommand : Command<TagsCommand.Settings>
{
    private readonly IFileSystem _fs;
    private readonly PwVaultConfig _config;

    public TagsCommand(IFileSystem fs, PwVaultConfig config)
    {
        _fs = fs;
        _config = config;
    }

    public sealed class Settings : BaseCommandSettings
    {
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var vaultPath = VaultPathResolver.Resolve(settings, _config);
        using var storage = VaultStorage.Open(vaultPath, _fs);
        var tags = storage.ListTags();

        if (tags.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim]No tags in use yet.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Tag");
        table.AddColumn(new TableColumn("Entries").RightAligned());

        foreach (var tag in tags)
        {
            table.AddRow(
                $"[cyan]{Markup.Escape(tag.Tag)}[/]",
                tag.Count.ToString());
        }

        AnsiConsole.Write(table);
        return 0;
    }
}
